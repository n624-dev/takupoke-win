using Takupoke.Infrastructure.Materials;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class SourceWatcherTests
{
    [Fact]
    public async Task UnavailableFolderDoesNotPreventOtherSelectedFilesFromReportingUpdates()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-watcher-tests-" + Guid.NewGuid().ToString("N"));
        var unavailable = Path.Combine(root, "unavailable");
        var available = Path.Combine(root, "available");
        Directory.CreateDirectory(unavailable); Directory.CreateDirectory(available);
        var source = Path.Combine(available, "fake-school-document.pdf");
        await File.WriteAllTextAsync(source, "%PDF-fake-original");
        try
        {
            // Linux exercises the actual permission error without changing Windows user ACLs.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(unavailable, UnixFileMode.None);
            using var watcher = new SourceWatcher();
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Changed += () => changed.TrySetResult();
            watcher.Replace([Path.Combine(unavailable, "fake.pdf"), Path.Combine(root, "missing", "fake.pdf"), source]);
            await File.WriteAllTextAsync(source, "%PDF-fake-updated");
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            watcher.Replace([]);
        }
        finally
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(unavailable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(root, recursive: true);
        }
    }
}
