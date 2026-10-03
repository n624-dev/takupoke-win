using Takupoke.Core;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.UITests;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PreferenceSnapshotTests
{
    [Fact]
    public async Task SharedSnapshotAllowsAtomicPreferenceReplacementAndKeepsCompleteOldValue()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-preference-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferencesStore(root); var path = Path.Combine(root, "preferences.json");
            await store.SaveAsync(new() { IncludesChanges = true, SelectedClasses = ["3_IT"] });
            using (var old = PreferenceSnapshot.Open(path))
            {
                await store.SaveAsync(new() { IncludesChanges = false, SelectedClasses = ["3_IT"] });
                using var bytes = new MemoryStream(); old.CopyTo(bytes);
                Assert.True(DataCodec.Decode<UserPreferences>(bytes.ToArray()).IncludesChanges);
                Assert.False(DataCodec.Decode<UserPreferences>(PreferenceSnapshot.ReadBytes(path)).IncludesChanges);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SnapshotRetriesRealSharingViolationUntilExclusiveHandleIsReleased()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-preference-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "preferences.json");
            await new PreferencesStore(root).SaveAsync(new() { IncludesChanges = false, MainColor = "purple" });
            var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var retried = new TaskCompletionSource<IOException>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<byte[]>? read = null;
            try
            {
                read = Task.Run(() => PreferenceSnapshot.ReadBytes(path, error => retried.TrySetResult(error)));
                var error = await retried.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(OperatingSystem.IsWindows() ? (error.HResult & 0xffff) is 32 or 33 : (error.HResult & 0xffff) == 11);
                Assert.False(read.IsCompleted);
            }
            finally { locked.Dispose(); }
            var saved = DataCodec.Decode<UserPreferences>(await read!);
            Assert.False(saved.IncludesChanges); Assert.Equal("purple", saved.MainColor);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
