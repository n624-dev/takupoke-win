using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class SchoolDataStoreTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-store-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestProtector _protector = new();
    private readonly Clock _clock = new();
    private SchoolDataStore Store() => new(_root, _protector, _clock);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2032, 9, 30, 23, 59, 0, TimeSpan.FromHours(9));
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
    private sealed class TestProtector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "test-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "test-key");
        public void Dispose() => _cipher.Dispose();
    }
    public Task InitializeAsync() { Directory.CreateDirectory(_root); return Task.CompletedTask; }
    public Task DisposeAsync() { _protector.Dispose(); Directory.Delete(_root, recursive: true); return Task.CompletedTask; }
    [Fact]
    public async Task SchoolPayloadIsEncryptedAndSurvivesRestart()
    {
        await using (var store = Store())
        {
            var lease = await store.BeginAsync();
            await store.WriteAsync(lease, "test", "架空学校データA");
            var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "school", "school.sqlite"));
            Assert.DoesNotContain("架空学校データA", Encoding.UTF8.GetString(bytes));
        }
        await using var reopened = Store();
        Assert.Equal("架空学校データA", await reopened.ReadAsync<string>(await reopened.BeginAsync(), "test"));
    }
    [Fact]
    public async Task PeriodRolloverClearsSchoolDataPreservesPreferencesAndRejectsOldLease()
    {
        await using var store = Store();
        var preferences = new PreferencesStore(_root);
        await preferences.SaveAsync(new() { FavoriteIds = ["fake-link"], SelectedClasses = ["1_CN"] });
        var old = await store.BeginAsync();
        await store.WriteAsync(old, "test", "架空学校データA");
        _clock.Now = _clock.Now.AddMinutes(1);
        var current = await store.BeginAsync();
        Assert.Null(await store.ReadAsync<string>(current, "test"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.WriteAsync(old, "test", "架空古いデータB"));
        Assert.Contains("fake-link", (await preferences.LoadAsync()).FavoriteIds);
        Assert.Equal(new[] { "1_CN" }, (await preferences.LoadAsync()).SelectedClasses);
    }
    [Fact]
    public async Task LockPreventsReadsAndOldOperationsCannotResumeAfterUnlock()
    {
        await using var store = Store();
        var old = await store.BeginAsync();
        await store.WriteAsync(old, "test", "架空学校データA");
        await store.SetProtectedDataAvailableAsync(false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.BeginAsync());
        await store.SetProtectedDataAvailableAsync(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.ReadAsync<string>(old, "test"));
        Assert.Equal("架空学校データA", await store.ReadAsync<string>(await store.BeginAsync(), "test"));
    }
    [Fact]
    public async Task ParseFailureKeepsNewSelectionAndLastSuccessfulAnalysisWithItsOwnOriginal()
    {
        await using var store = Store();
        var lease = await store.BeginAsync();
        var first = Source("first", "%PDF-fake-A"u8.ToArray());
        await store.SaveOriginalAsync(lease, first, "%PDF-fake-A"u8.ToArray());
        var accepted = new MaterialAnalysis(first.Id, MaterialKind.Timetable, 1, first.Digest, first.OriginalName, _clock.Now, 2032,
            new(2032, "前期", [new("1_CN", 1, 1, new("架空科目A"), "架空原文A", 1)]));
        await store.SaveAnalysisAsync(lease, accepted);
        var second = Source("second", "%PDF-fake-B"u8.ToArray());
        await store.SaveOriginalAsync(lease, second, "%PDF-fake-B"u8.ToArray());
        await store.WriteAsync(lease, "attempt.Timetable", new MaterialAttempt(_clock.Now, "P01", true));
        await store.CollectOriginalsAsync(lease);
        Assert.Equal(second.Id, (await store.ReadAsync<SourceRecord>(lease, "selection.Timetable"))!.Id);
        Assert.Equal(first.Id, (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Timetable"))!.OriginalId);
        Assert.Equal("%PDF-fake-A"u8.ToArray(), await store.ReadOriginalAsync(lease, first.Id));
        Assert.Equal("%PDF-fake-B"u8.ToArray(), await store.ReadOriginalAsync(lease, second.Id));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAnalysisAsync(lease, accepted));
    }
    [Fact]
    public async Task CorruptDatabaseDoesNotGetReinitializedWithinCurrentPeriod()
    {
        await using var store = Store();
        var lease = await store.BeginAsync();
        await File.WriteAllTextAsync(Path.Combine(_root, "school", "school.sqlite"), "fake damaged database");
        await Assert.ThrowsAnyAsync<Exception>(() => store.ReadAsync<string>(lease, "test"));
        Assert.Equal("fake damaged database", await File.ReadAllTextAsync(Path.Combine(_root, "school", "school.sqlite")));
    }
    [Fact]
    public void CipherRejectsTamperingAndDifferentRecordPurpose()
    {
        using var cipher = new EnvelopeCipher(RandomNumberGenerator.GetBytes(32));
        var encrypted = cipher.Encrypt("架空学校データA"u8.ToArray(), "record-a");
        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(encrypted, "record-b"));
        encrypted[^1] ^= 1;
        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(encrypted, "record-a"));
    }
    [Fact]
    public async Task PendingFailureAndJobCommitTogetherAndNewSourceInvalidatesJob()
    {
        await using var store = Store(); var lease = await store.BeginAsync(); var bytes = "%PDF-synthetic-recovery"u8.ToArray(); var source = Source("recovery", bytes);
        await store.SaveOriginalAsync(lease, source, bytes);
        var attempt = new MaterialAttempt(_clock.Now, "P08", true, source.Digest, 2032, ParserVersion: 1, RecoveryPending: true);
        var job = new RecoveryJob(source.Digest, RecoveryDocumentKind.Timetable, RecoveryJobState.Pending, _clock.Now);
        await store.SavePdfFailureAsync(lease, source, attempt, job);
        Assert.True((await store.ReadAsync<MaterialAttempt>(lease, "attempt.Timetable"))!.RecoveryPending);
        Assert.Equal(source.Digest, (await store.ReadAsync<RecoveryJob>(lease, "recovery.Timetable"))!.PdfHash);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SavePdfFailureAsync(lease, source, attempt with { Failure = "limit", RecoveryPending = false }, null, cancelled.Token));
        Assert.NotNull(await store.ReadAsync<RecoveryJob>(lease, "recovery.Timetable"));
        await store.SavePdfFailureAsync(lease, source, attempt with { Failure = "limit", RecoveryPending = false }, null);
        Assert.Null(await store.ReadAsync<RecoveryJob>(lease, "recovery.Timetable"));
        await store.SavePdfFailureAsync(lease, source, attempt, job);
        var nextBytes = "%PDF-synthetic-new"u8.ToArray(); await store.SaveOriginalAsync(lease, Source("next", nextBytes), nextBytes);
        Assert.Null(await store.ReadAsync<RecoveryJob>(lease, "recovery.Timetable"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SavePdfFailureAsync(lease, source, attempt, job));
    }
    [Fact]
    public async Task SameHashReselectionInvalidatesFailureCacheBeforeAReplacementParseStarts()
    {
        await using var store = Store(); var lease = await store.BeginAsync();
        var bytes = "%PDF-synthetic-same-hash"u8.ToArray(); var first = Source("same-first", bytes);
        await store.SaveOriginalAsync(lease, first, bytes);
        var attempt = new MaterialAttempt(_clock.Now, "P08", true, first.Digest, 2032, ParserVersion: 1, RecoveryPending: true);
        await store.SavePdfFailureAsync(lease, first, attempt,
            new RecoveryJob(first.Digest, RecoveryDocumentKind.Timetable, RecoveryJobState.Pending, _clock.Now));
        var second = Source("same-second", bytes);
        await store.SaveOriginalAsync(lease, second, bytes);
        // This is the persisted boundary when cancellation occurs before parsing.
        Assert.Equal(second.Id, (await store.ReadAsync<SourceRecord>(lease, "selection.Timetable"))!.Id);
        Assert.Null(await store.ReadAsync<MaterialAttempt>(lease, "attempt.Timetable"));
        Assert.Null(await store.ReadAsync<RecoveryJob>(lease, "recovery.Timetable"));
        Assert.Equal(bytes, await store.ReadOriginalAsync(lease, second.Id));
    }

    private sealed record RecoveryFixture(RecoveryDocument Document, RecoveryResult Result);
    private async Task<(SchoolLease Lease, SourceRecord Source, RecoveryAudit Audit)> AwaitingRecoveryAsync(SchoolDataStore store, int documentYear = 2026, bool bypassPreviewValidation = false, bool wrongHalf = false)
    {
        _clock.Now = wrongHalf ? new(2026, 9, 30, 23, 59, 0, TimeSpan.FromHours(9)) : new(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(9));
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        var raw = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "recovery-exam.json"));
        var fixture = JsonSerializer.Deserialize<RecoveryFixture>(raw.Replace("2026", documentYear.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), options)!;
        // The immutable fixture records v4; these period/storage tests require
        // a semantically validated current-version result.
        fixture = fixture with { Result = fixture.Result with { Metadata = fixture.Result.Metadata with { ValidatorVersion = RecoveryValidator.Version } } };
        var lease = await store.BeginAsync(); var bytes = "%PDF-synthetic-adoption"u8.ToArray(); var source = Source("recovery-adopt", bytes) with { Kind = MaterialKind.Exam };
        var doc = fixture.Document with { PdfHash = source.Digest }; var result = fixture.Result with { PdfHash = source.Digest };
        var audit = new RecoveryAudit(doc, result, new(source.Digest, RecoveryValidator.Fingerprint(result), RecoveryValidator.Fingerprint(doc), result.Metadata, _clock.Now));
        await store.SaveOriginalAsync(lease, source, bytes);
        await store.SavePdfFailureAsync(lease, source, new(_clock.Now, "P08", true, source.Digest, 2026, ParserVersion: PdfScheduleParser.SpecialVersion, RecoveryPending: true), new(source.Digest, doc.Kind, RecoveryJobState.Pending, _clock.Now));
        var job = new RecoveryJob(source.Digest, doc.Kind, RecoveryJobState.AwaitingConfirmation, _clock.Now, ResultHash: RecoveryValidator.Fingerprint(result));
        var preview = new RecoveryPreview(source.Id, lease, doc, result, _clock.Now);
        if (bypassPreviewValidation)
        {
            // Reproduce a preview persisted by an older application version.
            await store.WriteAsync(lease, "recovery.Exam", job);
            await store.WriteAsync(lease, "recovery.preview.Exam", preview);
        }
        else await store.SaveRecoveryProgressAsync(lease, source, job, preview);
        return (lease, source, audit);
    }
    [Fact] public async Task PriorYearCannotReachRecoveryPreview()
    {
        await using var store = Store();
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => AwaitingRecoveryAsync(store, documentYear: 2025));
        Assert.Contains("年度・学期", failure.Message);
        var lease = await store.BeginAsync(); Assert.Null(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        Assert.Null(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task PriorYearPersistedPreviewOrAcceptanceCannotBeAdopted(bool reuseAcceptance)
    {
        await using var store = Store(); var (lease, source, audit) = await AwaitingRecoveryAsync(store, documentYear: 2025, bypassPreviewValidation: true);
        Assert.True(RecoveryValidator.Validate(audit.Document, audit.Result).CanAdopt);
        if (reuseAcceptance) await store.WriteAsync(lease, "recovery.accepted.Exam." + source.Digest, audit);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveRecoveryAsync(lease, source, audit, _clock.Now, reuseAccepted: reuseAcceptance));
        Assert.Contains("年度・学期", failure.Message); Assert.Null(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"));
        Assert.NotNull(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
    }
    [Theory] [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task WrongTermCannotReachPreviewOrReplaceTheCurrentFormalAnalysis(bool persistedPreview, bool reuseAcceptance)
    {
        _clock.Now = new(2026, 9, 30, 23, 59, 0, TimeSpan.FromHours(9));
        await using var store = Store(); var lease = await store.BeginAsync(); var bytes = "%PDF-synthetic-term-boundary"u8.ToArray(); var source = Source("term-boundary", bytes);
        await store.SaveOriginalAsync(lease, source, bytes);
        var layout = RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        var original = RecoveryDocumentBuilder.Build(source.Digest, MaterialKind.Timetable, [layout], (_, box) => !layout.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))));
        var metadata = new RecoveryMetadata("rule", "rules", "1", "1", "2", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, "test");
        RecoveryResult Result(RecoveryDocument doc) => new(source.Digest, doc.Kind, doc.SchoolYear, doc.Term, doc.Cells.Select(c => c.ConfirmedEmpty ? new RecoveredCell(c.Id, RecoveryValueState.Empty, []) : RecoveryRules.Recover(doc, c)!).ToArray(), metadata);
        var prior = RecoveryAnalysisConverter.Convert(source, original, Result(original), _clock.Now);
        await store.SaveAnalysisAsync(lease, prior);
        var doc = original with { Term = "後期", Sources = original.Sources.Select(s => original.TermEvidence.Contains(s.Id) ? s with { Text = "後期" } : s).ToArray() }; var result = Result(doc);
        Assert.True(RecoveryValidator.Validate(doc, result).CanAdopt); Assert.False(RecoveryPolicy.MatchesPeriod(doc, lease.Period));
        var acceptance = new RecoveryAcceptance(source.Digest, RecoveryValidator.Fingerprint(result), RecoveryValidator.Fingerprint(doc), metadata, _clock.Now); var audit = new RecoveryAudit(doc, result, acceptance);
        await store.SavePdfFailureAsync(lease, source, new(_clock.Now, "P08", true, source.Digest, 2026, ParserVersion: PdfScheduleParser.TimetableVersion, RecoveryPending: true), new(source.Digest, doc.Kind, RecoveryJobState.Pending, _clock.Now));
        var job = new RecoveryJob(source.Digest, doc.Kind, RecoveryJobState.AwaitingConfirmation, _clock.Now, RecoveryValidator.Fingerprint(result)); var preview = new RecoveryPreview(source.Id, lease, doc, result, _clock.Now);
        if (persistedPreview)
        {
            await store.WriteAsync(lease, "recovery.Timetable", job); await store.WriteAsync(lease, "recovery.preview.Timetable", preview);
            if (reuseAcceptance) await store.WriteAsync(lease, "recovery.accepted.Timetable." + source.Digest, audit);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveRecoveryAsync(lease, source, audit, _clock.Now, reuseAccepted: reuseAcceptance));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveRecoveryProgressAsync(lease, source, job, preview));
            Assert.Null(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Timetable"));
        }
        var kept = (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Timetable"))!;
        Assert.Equal("前期", kept.Timetable!.Term); Assert.Equal(prior.ParsedAt, kept.ParsedAt); Assert.Null(kept.Recovery);
        Assert.Equal("P08", (await store.ReadAsync<MaterialAttempt>(lease, "attempt.Timetable"))!.Failure);
    }
    [Fact] public async Task SpecialDatesFromAnotherHalfCannotReachRecoveryPreview()
    {
        await using var store = Store();
        await Assert.ThrowsAsync<InvalidDataException>(() => AwaitingRecoveryAsync(store, wrongHalf: true));
        var lease = await store.BeginAsync();
        Assert.Null(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        Assert.Null(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task SpecialDatesFromAnotherHalfCannotBeAdoptedFromOldPreviewOrAcceptance(bool reuseAcceptance)
    {
        await using var store = Store();
        var (lease, source, audit) = await AwaitingRecoveryAsync(store, bypassPreviewValidation: true, wrongHalf: true);
        Assert.True(RecoveryValidator.Validate(audit.Document, audit.Result).CanAdopt);
        Assert.False(RecoveryPolicy.MatchesPeriod(audit.Document, lease.Period));
        if (reuseAcceptance) await store.WriteAsync(lease, "recovery.accepted.Exam." + source.Digest, audit);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveRecoveryAsync(lease, source, audit, _clock.Now, reuseAccepted: reuseAcceptance));
        Assert.Null(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"));
        Assert.NotNull(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        Assert.Equal("P08", (await store.ReadAsync<MaterialAttempt>(lease, "attempt.Exam"))!.Failure);
    }
    [Fact] public async Task RecoveryAdoptionCommitsFormalAuditAcceptanceAndClearsPendingTogether()
    {
        await using var store = Store(); var (lease, source, audit) = await AwaitingRecoveryAsync(store);
        var preview = (await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"))!;
        var job = (await store.ReadAsync<RecoveryJob>(lease, "recovery.Exam"))!;
        Assert.Equal(lease, preview.Lease); Assert.Equal(source.Id, preview.SourceId); Assert.Equal(RecoveryValidator.Fingerprint(audit.Result), job.ResultHash);
        Assert.Equal(RecoveryValidator.Fingerprint(audit.Document), RecoveryValidator.Fingerprint(preview.Document)); Assert.Equal(RecoveryValidator.Fingerprint(audit.Result), RecoveryValidator.Fingerprint(preview.Result));
        await store.SaveRecoveryAsync(lease, source, audit, _clock.Now);
        var formal = await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"); Assert.Equal(source.Digest, formal!.SourceDigest); Assert.Equal(audit.Acceptance, formal.Recovery!.Acceptance);
        Assert.NotNull(await store.ReadAsync<RecoveryAudit>(lease, "recovery.accepted.Exam." + source.Digest));
        Assert.Null(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam")); Assert.Null(await store.ReadAsync<RecoveryJob>(lease, "recovery.Exam"));
        Assert.Null((await store.ReadAsync<MaterialAttempt>(lease, "attempt.Exam"))!.Failure);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task OldStrictFailureCannotAuthorizePreviewAdoptionOrAcceptanceReuse(bool reuseAcceptance)
    {
        await using var store = Store(); var (lease, source, audit) = await AwaitingRecoveryAsync(store);
        var oldAttempt = (await store.ReadAsync<MaterialAttempt>(lease, "attempt.Exam"))! with { ParserVersion = PdfScheduleParser.SpecialVersion - 1 };
        await store.WriteAsync(lease, "attempt.Exam", oldAttempt);
        if (reuseAcceptance) await store.WriteAsync(lease, "recovery.accepted.Exam." + source.Digest, audit);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveRecoveryAsync(lease, source, audit, _clock.Now, reuseAccepted: reuseAcceptance));
        Assert.Null(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"));
        Assert.NotNull(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        Assert.Equal(oldAttempt, await store.ReadAsync<MaterialAttempt>(lease, "attempt.Exam"));
    }
    [Fact] public async Task StrictSuccessInvalidatesOldPreviewAndCannotBeOverwrittenByRecovery()
    {
        await using var store = Store(); var (lease, source, audit) = await AwaitingRecoveryAsync(store);
        var strict = Takupoke.Infrastructure.Recovery.RecoveryAnalysisConverter.Convert(source, audit.Document, audit.Result, _clock.Now);
        var staleJob = (await store.ReadAsync<RecoveryJob>(lease, "recovery.Exam"))!;
        var stalePreview = (await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"))!;
        await store.SaveAnalysisAsync(lease, strict);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveRecoveryProgressAsync(lease, source, staleJob, stalePreview));
        Assert.Null(await store.ReadAsync<RecoveryJob>(lease, "recovery.Exam"));
        Assert.Null(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveRecoveryAsync(lease, source, audit, _clock.Now));
        Assert.Null((await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"))!.Recovery);
    }
    [Fact] public async Task CancelledAdoptionPreservesPreviewAndSelectionChangeRejectsIt()
    {
        await using var store = Store(); var (lease, source, audit) = await AwaitingRecoveryAsync(store);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveRecoveryAsync(lease, source, audit, _clock.Now, cancellation.Token));
        Assert.NotNull(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam")); Assert.Null(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"));
        var bytes = "%PDF-synthetic-reselected"u8.ToArray(); await store.SaveOriginalAsync(lease, Source("replacement", bytes) with { Kind = MaterialKind.Exam }, bytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveRecoveryAsync(lease, source, audit, _clock.Now));
        Assert.Null(await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"));
    }
    private SourceRecord Source(string id, byte[] bytes) => new(id, MaterialKind.Timetable, Path.Combine(_root, "fake.pdf"), "fake-identity", "架空資料A.pdf",
        NotificationDiff.Digest(bytes), bytes.Length, _clock.Now, _clock.Now, null);
    private sealed class RecoveryIdentity : Takupoke.Infrastructure.Materials.IFileIdentityProvider
    { public string Identity(FileStream stream) => "fictional-recovery-identity"; }
    [Fact] public async Task ForegroundRecoveryPreservesPersistedJobStartWhileCreatingPreview()
    {
        _clock.Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9));
        await using var store = Store(); var lease = await store.BeginAsync();
        var bytes = PdfParsingTests.SyntheticPdf(); var path = Path.Combine(_root, "fictional-coordinator.pdf"); await File.WriteAllBytesAsync(path, bytes);
        var source = Source("fictional-coordinator", bytes) with { Kind = MaterialKind.Exam, Path = path, FileIdentity = "fictional-recovery-identity" };
        await store.SaveOriginalAsync(lease, source, bytes);
        var material = new Takupoke.Infrastructure.Materials.MaterialCoordinator(store, new(new RecoveryIdentity()), _clock);
        var layout = RecoveryPipelineTests.Layout(MaterialKind.Exam);
        var coordinator = new RecoveryCoordinator(store, material,
            (_, kind, hash, _, _) => Task.FromResult(RecoveryDocumentBuilder.Build(hash, kind, [layout], (_, box) => !layout.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))))),
            _ => Task.FromResult<IReadOnlyList<ILocalRecoveryProvider>>([]), TimeProvider.System);
        var prepared = await coordinator.PrepareAsync(MaterialKind.Exam, 2026);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, prepared.State); Assert.NotNull(prepared.Preview);
        Assert.NotNull(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        Assert.Equal(_clock.GetUtcNow(), (await store.ReadAsync<RecoveryJob>(lease, "recovery.Exam"))!.CreatedAt);
    }

}
