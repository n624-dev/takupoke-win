using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
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
    public async Task WeekdayConsentSurvivesRestartButExpiresOnContentUpdateAndReselection()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-weekday-consent-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx"); var bytes = XlsxChangeReaderTests.Workbook(formula: true, cache: "火"); await File.WriteAllBytesAsync(path, bytes);
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector); var coordinator = new MaterialCoordinator(store, new(new FakeIdentity()));
            Assert.False((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed);
            var preview = await coordinator.PreviewChangesAsync(2032); Assert.True(preview.CanCorrectWeekdays);
            Assert.True((await coordinator.CorrectWeekdaysAsync(preview, 2032)).Parsed);
            var lease = await store.BeginAsync(); var source = (await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!;
            Assert.NotNull(source.WeekdayConsent); Assert.True((await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!.DateDerivedWeekdays);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            coordinator = new(store, new(new FakeIdentity()));
            Assert.True((await coordinator.ReparseAsync(MaterialKind.Changes, 2032)).Parsed);
            Assert.True((await coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
            Assert.Equal(source.Id, (await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!.Id);
            await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook(formula: true, cache: "木"));
            Assert.False((await coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
            Assert.Null((await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!.WeekdayConsent);
            Assert.Equal(source.Id, (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!.OriginalId);
            await File.WriteAllBytesAsync(path, bytes);
            Assert.False((await coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed); // A -> B -> A never resurrects consent.
            Assert.False((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed);
            Assert.Null((await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!.WeekdayConsent);
            await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.CorrectWeekdaysAsync(preview, 2032));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task ChangedOriginalDuringWeekdayConfirmationCannotInstallConsent()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-weekday-race-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx"); await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook(formula: true, cache: "火"));
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector); var coordinator = new MaterialCoordinator(store, new(new FakeIdentity()));
            await coordinator.SelectAsync(MaterialKind.Changes, path, 2032); var preview = await coordinator.PreviewChangesAsync(2032);
            await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook(formula: true, cache: "木"));
            await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.CorrectWeekdaysAsync(preview, 2032));
            var lease = await store.BeginAsync(); Assert.Null((await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!.WeekdayConsent);
            Assert.Null(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"));
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory] [InlineData(MaterialKind.Timetable)] [InlineData(MaterialKind.Exam)] [InlineData(MaterialKind.ExamReturn)]
    public async Task OlderParserSuccessForSameHashIsReparsedAndFailureKeepsPreviousFormalResult(MaterialKind kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-parser-version-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.pdf"); var bytes = PdfParsingTests.SyntheticPdf(); await File.WriteAllBytesAsync(path, bytes);
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector); var lease = await store.BeginAsync(); var now = DateTimeOffset.UtcNow;
            var source = new SourceRecord("old-parser-original", kind, path, "fake-file-identity", "fictional.pdf", NotificationDiff.Digest(bytes), bytes.Length, now, now, null);
            await store.SaveOriginalAsync(lease, source, bytes);
            var old = new MaterialAnalysis(source.Id, kind, MaterialCoordinator.ParserVersion(kind) - 1, source.Digest, source.OriginalName, now, 2032,
                kind == MaterialKind.Timetable ? new TimetableAnalysis(2032, "前期", [new("3_CN", 1, 1, new("架空旧科目"), "架空旧原文", 1)]) : null,
                Special: kind != MaterialKind.Timetable ? new SpecialAnalysis(kind, 2032, [], [], new Dictionary<int, TimeRange>(), []) : null);
            await store.SaveAnalysisAsync(lease, old);
            var result = await new MaterialCoordinator(store, new(new FakeIdentity())).RefreshAsync(kind, 2032);
            Assert.False(result.Changed); Assert.False(result.Parsed);
            var preserved = (await store.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind))!;
            Assert.Equal(old.ParserVersion, preserved.ParserVersion); Assert.Equal(old.OriginalId, preserved.OriginalId); Assert.Equal(bytes, await store.ReadOriginalAsync(lease, source.Id));
            var attempt = (await store.ReadAsync<MaterialAttempt>(lease, "attempt." + kind))!;
            Assert.NotNull(attempt.Failure); Assert.Equal(MaterialCoordinator.ParserVersion(kind), attempt.ParserVersion); Assert.True(attempt.RecoveryPending);
            Assert.Equal(RecoveryJobState.Pending, (await store.ReadAsync<RecoveryJob>(lease, "recovery." + kind))!.State);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task SameBytesRefreshUpdatesSourceModificationWithoutReparsingOrReacquiring()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-source-metadata-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); using var protector = new Protector();
        try
        {
            var path = Path.Combine(root, "fictional.xlsx"); await File.WriteAllBytesAsync(path, XlsxChangeReaderTests.Workbook());
            await using var store = new SchoolDataStore(Path.Combine(root, "data"), protector); var coordinator = new MaterialCoordinator(store, new(new FakeIdentity()));
            Assert.True((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed);
            var lease = await store.BeginAsync(); var original = (await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!;
            var analysis = (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!;
            File.SetLastWriteTimeUtc(path, new DateTime(2031, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            Assert.True((await coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
            var refreshed = (await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!;
            Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(path)), refreshed.SourceModifiedAt);
            Assert.NotEqual(original.SourceModifiedAt, refreshed.SourceModifiedAt);
            Assert.Equal(original.AcquiredAt, refreshed.AcquiredAt); Assert.Equal(original.Id, refreshed.Id);
            Assert.Equal(analysis.ParsedAt, (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!.ParsedAt);
        }
        finally { Directory.Delete(root, true); }
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
    public async Task ReplacedFileWithSameBytesUpdatesIdentityWithoutReacquiringOrReparsing()
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
            var previous = await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes");
            Assert.True((await coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed);
            var current = (await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!;
            Assert.Equal(source!.Id, current.Id); Assert.Equal(source.Digest, current.Digest);
            Assert.Equal(identity.Value, current.FileIdentity);
            Assert.Equal(previous!.ParsedAt, (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!.ParsedAt);
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
