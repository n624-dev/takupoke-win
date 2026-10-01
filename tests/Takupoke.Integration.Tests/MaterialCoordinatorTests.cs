using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class MaterialCoordinatorTests
{
    private sealed class FakeIdentity : IFileIdentityProvider
    { public string Value { get; set; } = "fake-file-identity"; public string Identity(FileStream stream) => Value; }
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "fake-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fake-key");
        public void Dispose() => _cipher.Dispose();
    }
    [Fact]
    public async Task ChangedButInvalidXlsxRetainsPreviousAcceptedAnalysisAndBothOriginals()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-material-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx"); await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook());
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector);
            var coordinator = new MaterialCoordinator(store, new(new FakeIdentity()));
            Assert.True((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed);
            var lease = await store.BeginAsync(); var original = await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes");
            Assert.False((await coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Changed);
            await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook(mutate: entries => entries["xl/worksheets/sheet1.xml"] = "<invalid>"));
            var failed = await coordinator.RefreshAsync(MaterialKind.Changes, 2032);
            Assert.True(failed.Changed); Assert.False(failed.Parsed);
            var selected = await store.ReadAsync<SourceRecord>(lease, "selection.Changes");
            Assert.NotEqual(original!.OriginalId, selected!.Id);
            Assert.Equal(original.SourceDigest, (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!.SourceDigest);
            await store.CollectOriginalsAsync(lease);
            Assert.NotEmpty(await store.ReadOriginalAsync(lease, original.OriginalId)); Assert.NotEmpty(await store.ReadOriginalAsync(lease, selected.Id));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task ReplacedFileAtSamePathIsNotSilentlyAdopted()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-material-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx"); await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook());
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector);
            var identity = new FakeIdentity(); var coordinator = new MaterialCoordinator(store, new(identity));
            await coordinator.SelectAsync(MaterialKind.Changes, path, 2032);
            var lease = await store.BeginAsync(); var source = await store.ReadAsync<SourceRecord>(lease, "selection.Changes");
            identity.Value = "fake-replacement-identity";
            Assert.False((await coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
            Assert.Equal(source!.Id, (await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!.Id);
            Assert.True((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed);
        }
        finally { Directory.Delete(root, true); }
    }
}
