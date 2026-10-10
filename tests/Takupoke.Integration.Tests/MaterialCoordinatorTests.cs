using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class MaterialCoordinatorTests
{
    private sealed class FakeIdentity : IFileIdentityProvider
    {
        public string Value { get; set; } = "fake-file-identity";
        public Func<FileStream, string>? Resolve { get; set; }
        public string Identity(FileStream stream) => Resolve?.Invoke(stream) ?? Value;
    }
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "fake-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fake-key");
        public void Dispose() => _cipher.Dispose();
    }
    [Fact]
    public async Task DeletedOriginalReportsUnavailableAndKeepsAcceptedDataAndSavedCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-material-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx");
            var bytes = XlsxChangeReaderTests.Workbook();
            await File.WriteAllBytesAsync(path, bytes);
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector);
            var coordinator = new MaterialCoordinator(store, new(new FakeIdentity()));
            Assert.True((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed);
            var lease = await store.BeginAsync();
            var accepted = await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes");
            File.Delete(path);
            var result = await coordinator.RefreshAsync(MaterialKind.Changes, 2032);
            Assert.Equal(new SourceException(SourceFailure.Unavailable).Message, result.Error);
            Assert.False(result.Changed);
            Assert.Equal(accepted!.OriginalId, (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!.OriginalId);
            Assert.Equal(bytes, await store.ReadOriginalAsync(lease, accepted.OriginalId));
            Assert.Equal(result.Error, (await store.ReadAsync<MaterialAttempt>(lease, "acquisition.Changes"))!.Failure);
        }
        finally { Directory.Delete(root, recursive: true); }
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
    public async Task FirstSelectionAndOriginalSurviveRestartWhenPdfCannotBeParsed()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-material-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.pdf");
            var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n% Entirely fictional malformed PDF.\n");
            await File.WriteAllBytesAsync(path, bytes);
            await using (var store = new SchoolDataStore(Path.Combine(root, "data"), protector))
            {
                var result = await new MaterialCoordinator(store, new(new FakeIdentity())).SelectAsync(MaterialKind.Exam, path, 2032);
                Assert.True(result.Changed); Assert.False(result.Parsed); Assert.NotNull(result.Error);
            }
            await using var reopened = new SchoolDataStore(Path.Combine(root, "data"), protector);
            var lease = await reopened.BeginAsync();
            var source = await reopened.ReadAsync<SourceRecord>(lease, "selection.Exam");
            Assert.NotNull(source); Assert.Equal(path, source.Path);
            Assert.Equal(bytes, await reopened.ReadOriginalAsync(lease, source.Id));
            Assert.NotNull((await reopened.ReadAsync<MaterialAttempt>(lease, "attempt.Exam"))?.Failure);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task SamePathReplacementWithIdenticalBytesRetainsAnalysisAndRefreshesIdentity()
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
            var accepted = await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes");
            identity.Value = "fake-replacement-identity";
            var result = await coordinator.RefreshAsync(MaterialKind.Changes, 2032);
            Assert.True(result.Parsed); Assert.False(result.Changed); Assert.Null(result.Error);
            var refreshed = await store.ReadAsync<SourceRecord>(lease, "selection.Changes");
            Assert.Equal(source!.Id, refreshed!.Id);
            Assert.Equal(identity.Value, refreshed.FileIdentity);
            Assert.Equal(source.Path, refreshed.Path);
            Assert.Equal(DataCodec.Encode(accepted),
                DataCodec.Encode(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task SamePathAtomicReplacementWithChangedBytesParsesAndSurvivesRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-material-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx");
            var original = XlsxChangeReaderTests.Workbook();
            await File.WriteAllBytesAsync(path, original);
            var identity = new FakeIdentity();
            await using (var store = new SchoolDataStore(Path.Combine(root, "data"), protector))
            {
                var coordinator = new MaterialCoordinator(store, new(identity));
                Assert.True((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed);
                var updated = XlsxChangeReaderTests.Workbook(mutate: entries =>
                    entries["xl/worksheets/sheet1.xml"] = entries["xl/worksheets/sheet1.xml"]
                        .Replace("架空科目B", "架空更新科目D", StringComparison.Ordinal));
                var pending = Path.Combine(root, "owned-update.xlsx");
                await File.WriteAllBytesAsync(pending, updated);
                File.Move(pending, path, overwrite: true);
                identity.Value = "fake-atomic-replacement";
                var result = await coordinator.RefreshAsync(MaterialKind.Changes, 2032);
                Assert.True(result.Changed); Assert.True(result.Parsed); Assert.Null(result.Error);
                var lease = await store.BeginAsync();
                var analysis = await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes");
                Assert.Equal("架空更新科目D", Assert.Single(analysis!.Changes!).AfterSubject);
                Assert.Equal(NotificationDiff.Digest(updated), analysis.SourceDigest);
            }
            await using var reopened = new SchoolDataStore(Path.Combine(root, "data"), protector);
            var restarted = new MaterialCoordinator(reopened, new(identity));
            var again = await restarted.RefreshAsync(MaterialKind.Changes, 2032);
            Assert.True(again.Parsed); Assert.False(again.Changed);
            var selected = await reopened.ReadAsync<SourceRecord>(await reopened.BeginAsync(), "selection.Changes");
            Assert.Equal(identity.Value, selected!.FileIdentity);
            Assert.Equal(path, selected.Path);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task ReplacementDuringCurrentReadIsRejectedAndKeepsPreviousSourceAndAnalysis()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-material-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx");
            await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook());
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector);
            var identity = new FakeIdentity(); var coordinator = new MaterialCoordinator(store, new(identity));
            Assert.True((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed);
            var lease = await store.BeginAsync();
            var selected = await store.ReadAsync<SourceRecord>(lease, "selection.Changes");
            var accepted = await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes");
            var checks = 0;
            identity.Resolve = _ => ++checks == 1 ? "fake-current-handle" : "fake-replaced-during-read";
            var result = await coordinator.RefreshAsync(MaterialKind.Changes, 2032);
            Assert.Equal(new SourceException(SourceFailure.Changing).Message, result.Error);
            Assert.False(result.Changed); Assert.False(result.Parsed); Assert.Equal(2, checks);
            Assert.Equal(selected, await store.ReadAsync<SourceRecord>(lease, "selection.Changes"));
            Assert.Equal(DataCodec.Encode(accepted),
                DataCodec.Encode(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task WeekdayPreviewIsReadOnlyAndRejectsDifferentYearOrSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-material-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx"); await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook());
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector);
            var coordinator = new MaterialCoordinator(store, new(new FakeIdentity()));
            await coordinator.SelectAsync(MaterialKind.Changes, path, 2032);
            var lease = await store.BeginAsync(); var good = await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes");
            await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook(formula: true, cache: "火"));
            Assert.False((await coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
            var failure = await store.ReadAsync<MaterialAttempt>(lease, "attempt.Changes");
            var preview = await coordinator.PreviewChangesAsync(2032);
            Assert.Single(preview.Changes); Assert.Single(preview.Warnings);
            Assert.Equal(good!.OriginalId, (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!.OriginalId);
            Assert.Equal(failure, await store.ReadAsync<MaterialAttempt>(lease, "attempt.Changes"));
            await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.PreviewChangesAsync(2033));
            await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook());
            await coordinator.RefreshAsync(MaterialKind.Changes, 2032);
            await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.PreviewChangesAsync(2032));
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("fictional.txt")]
    [InlineData("~$fictional.xlsx")]
    public async Task WrongExtensionAndExcelTemporaryFilesCannotReplaceSelection(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-material-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, name); await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook());
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector);
            Assert.False((await new MaterialCoordinator(store, new(new FakeIdentity())).SelectAsync(MaterialKind.Changes, path, 2032)).Changed);
            Assert.Null(await store.ReadAsync<SourceRecord>(await store.BeginAsync(), "selection.Changes"));
        }
        finally { Directory.Delete(root, true); }
    }

}
