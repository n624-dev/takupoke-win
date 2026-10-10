using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;
using Xunit;

namespace Takupoke.Integration.Tests;

// Compiled and executed only on Windows; the production Win32 identity provider
// cannot be evaluated by a Linux stand-in.
public sealed class WindowsSourceRefreshTests
{
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "fake-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fake-key");
        public void Dispose() => _cipher.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicReplacementUsesNewNativeFileIdAndRefreshesWithoutReselection(bool changed)
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-native-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx");
            var original = XlsxChangeReaderTests.Workbook();
            await File.WriteAllBytesAsync(path, original);
            SourceRecord refreshed;
            await using (var store = new SchoolDataStore(Path.Combine(root, "data"), protector))
            {
                var coordinator = new MaterialCoordinator(store, new(new WindowsFileIdentity()));
                Assert.True((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed);
                var lease = await store.BeginAsync();
                var selected = (await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!;
                var accepted = (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!;
                var updated = changed ? XlsxChangeReaderTests.Workbook(mutate: entries =>
                    entries["xl/worksheets/sheet1.xml"] = entries["xl/worksheets/sheet1.xml"]
                        .Replace("架空科目B", "架空更新科目D", StringComparison.Ordinal)) : original;
                var pending = Path.Combine(root, "owned-replacement.xlsx");
                await File.WriteAllBytesAsync(pending, updated);
                File.Move(pending, path, overwrite: true);
                using (var input = File.OpenRead(path))
                    Assert.NotEqual(selected.FileIdentity, new WindowsFileIdentity().Identity(input));
                var result = await coordinator.RefreshAsync(MaterialKind.Changes, 2032);
                Assert.True(result.Parsed); Assert.Equal(changed, result.Changed); Assert.Null(result.Error);
                refreshed = (await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!;
                Assert.NotEqual(selected.FileIdentity, refreshed.FileIdentity);
                Assert.Equal(path, refreshed.Path);
                var analysis = (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!;
                Assert.Equal(NotificationDiff.Digest(updated), analysis.SourceDigest);
                if (changed)
                {
                    Assert.NotEqual(selected.Id, refreshed.Id);
                    Assert.Equal("架空更新科目D", Assert.Single(analysis.Changes!).AfterSubject);
                    Assert.Equal(original, await store.ReadOriginalAsync(lease, accepted.OriginalId));
                }
                else
                {
                    Assert.Equal(selected.Id, refreshed.Id);
                    Assert.Equal(DataCodec.Encode(accepted), DataCodec.Encode(analysis));
                }
            }
            await using var reopened = new SchoolDataStore(Path.Combine(root, "data"), protector);
            var again = await new MaterialCoordinator(reopened, new(new WindowsFileIdentity()))
                .RefreshAsync(MaterialKind.Changes, 2032);
            Assert.True(again.Parsed); Assert.False(again.Changed); Assert.Null(again.Error);
            Assert.Equal(refreshed.Id, (await reopened.ReadAsync<SourceRecord>(await reopened.BeginAsync(), "selection.Changes"))!.Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
