using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class LatestSourceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-latest-source-" + Guid.NewGuid().ToString("N"));
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] bytes) => _cipher.Encrypt(bytes, "fictional-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fictional-key");
        public void Dispose() => _cipher.Dispose();
    }
    private sealed class FakeIdentity : IFileIdentityProvider
    {
        public Func<int, string> Read { get; set; } = _ => "latest-file";
        private int _calls;
        public string Identity(FileStream stream) => Read(++_calls);
    }
    private readonly Protector _protector = new();
    private readonly Clock _clock = new();
    private SchoolDataStore _store = null!;
    public Task InitializeAsync() { Directory.CreateDirectory(_root); _store = new(_root, _protector, _clock); return Task.CompletedTask; }
    public async Task DisposeAsync() { await _store.DisposeAsync(); _protector.Dispose(); Directory.Delete(_root, true); }
    private async Task<(SchoolLease Lease, SourceRecord Source, MaterialAnalysis Good)> Seed(MaterialKind kind)
    {
        var bytes = PdfParsingTests.SyntheticPdf(); var path = Path.Combine(_root, "fictional.pdf"); await File.WriteAllBytesAsync(path, bytes);
        var lease = await _store.BeginAsync(); var now = _clock.GetUtcNow();
        var source = new SourceRecord("previous-original", kind, path, "previous-file", "fictional.pdf", NotificationDiff.Digest(bytes), bytes.Length, now, now, now);
        await _store.SaveOriginalAsync(lease, source, bytes);
        var good = new MaterialAnalysis(source.Id, kind, MaterialCoordinator.ParserVersion(kind), source.Digest, source.OriginalName, now, 2026,
            Timetable: kind == MaterialKind.Timetable ? new(2026, "後期", [new("3_CN", 1, 1, new("架空旧科目"), "架空旧原文", 1)]) : null,
            Special: kind != MaterialKind.Timetable ? new(kind, 2026, [], [], new Dictionary<int, TimeRange>(), []) : null);
        await _store.SaveAnalysisAsync(lease, good);
        return (lease, source, good);
    }
    private static byte[] UpdatedPdf() => PdfParsingTests.SyntheticPdf().Concat("\n% Fictional newly synchronized version\n"u8.ToArray()).ToArray();
    private MaterialCoordinator Materials(FakeIdentity? identity = null) => new(_store, new(identity ?? new()), _clock);

    [Theory]
    [InlineData(MaterialKind.Timetable, false)] [InlineData(MaterialKind.Exam, false)] [InlineData(MaterialKind.ExamReturn, false)]
    [InlineData(MaterialKind.Timetable, true)] [InlineData(MaterialKind.Exam, true)] [InlineData(MaterialKind.ExamReturn, true)]
    public async Task AtomicReplacementReadsLatestBytesAndPreservesLastGoodOnStrictFailure(MaterialKind kind, bool reparse)
    {
        var (lease, previous, good) = await Seed(kind); var bytes = UpdatedPdf();
        var replacement = Path.Combine(_root, "replacement.pdf"); await File.WriteAllBytesAsync(replacement, bytes); File.Move(replacement, previous.Path, true);
        var materials = Materials(); var result = reparse ? await materials.ReparseAsync(kind, 2026) : await materials.RefreshAsync(kind, 2026);
        Assert.True(result.Changed); Assert.False(result.Parsed); Assert.NotEqual(new SourceException(SourceFailure.Replaced).Message, result.Error);
        var current = (await _store.ReadAsync<SourceRecord>(lease, "selection." + kind))!;
        Assert.NotEqual(previous.Id, current.Id); Assert.Equal(previous.Path, current.Path); Assert.Equal(kind, current.Kind);
        Assert.Equal("latest-file", current.FileIdentity); Assert.Equal(NotificationDiff.Digest(bytes), current.Digest);
        Assert.Equal(bytes, await _store.ReadOriginalAsync(lease, current.Id));
        Assert.Equal(good.SourceDigest, (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind))!.SourceDigest);
        Assert.Equal(previous.Digest, NotificationDiff.Digest(await _store.ReadOriginalAsync(lease, good.OriginalId)));
        var attempt = (await _store.ReadAsync<MaterialAttempt>(lease, "attempt." + kind))!;
        Assert.Equal(current.Digest, attempt.SourceDigest); Assert.True(attempt.RecoveryPending);
        var job = (await _store.ReadAsync<RecoveryJob>(lease, "recovery." + kind))!;
        Assert.Equal(current.Digest, job.PdfHash); Assert.Equal(RecoveryPolicy.Kind(kind), job.Kind);
    }
    [Theory] [InlineData(MaterialKind.Timetable)] [InlineData(MaterialKind.Exam)] [InlineData(MaterialKind.ExamReturn)]
    public async Task SameBytesReplacementRefreshesIdentityAndExplicitReparseStillRunsStrict(MaterialKind kind)
    {
        var (lease, previous, good) = await Seed(kind); var materials = Materials();
        var refresh = await materials.RefreshAsync(kind, 2026);
        Assert.True(refresh.Parsed); Assert.False(refresh.Changed);
        var current = (await _store.ReadAsync<SourceRecord>(lease, "selection." + kind))!;
        Assert.Equal(previous.Id, current.Id); Assert.Equal(previous.Digest, current.Digest); Assert.Equal("latest-file", current.FileIdentity);
        Assert.Equal(good.ParsedAt, (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind))!.ParsedAt);
        Assert.False((await materials.ReparseAsync(kind, 2026)).Parsed);
        Assert.True((await _store.ReadAsync<MaterialAttempt>(lease, "attempt." + kind))!.RecoveryPending);
        Assert.Equal(good.SourceDigest, (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind))!.SourceDigest);
    }
    [Fact]
    public async Task ChangedPdfInvalidatesPreviewAndCannotAdoptTheOldHash()
    {
        var (lease, previous, good) = await Seed(MaterialKind.Exam);
        var preview = await PendingPreview(lease, previous);
        await File.WriteAllBytesAsync(previous.Path, UpdatedPdf()); var materials = Materials();
        var coordinator = new RecoveryCoordinator(_store, materials, (_, _, _, _, _) => throw new Exception("Unexpected build"), _ => throw new Exception("Unexpected providers"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.AdoptAsync(MaterialKind.Exam, preview));
        Assert.Null(await _store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        Assert.Equal(good.SourceDigest, (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"))!.SourceDigest);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CancellationOrChangeDuringReadKeepsSelectedSnapshotAndLastGood(bool cancel)
    {
        var (lease, previous, good) = await Seed(MaterialKind.Timetable); await File.WriteAllBytesAsync(previous.Path, UpdatedPdf());
        using var cancellation = new CancellationTokenSource();
        var identity = new FakeIdentity { Read = call => { if (cancel) cancellation.Cancel(); return call == 1 ? "latest-file" : "raced-file"; } };
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Materials(identity).ReparseAsync(MaterialKind.Timetable, 2026, cancellation.Token));
        else Assert.Equal(new SourceException(SourceFailure.Changing).Message, (await Materials(identity).RefreshAsync(MaterialKind.Timetable, 2026)).Error);
        Assert.Equal(previous.Id, (await _store.ReadAsync<SourceRecord>(lease, "selection.Timetable"))!.Id);
        Assert.Equal(good.SourceDigest, (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis.Timetable"))!.SourceDigest);
        Assert.Equal(previous.Digest, NotificationDiff.Digest(await _store.ReadOriginalAsync(lease, previous.Id)));
    }
    [Fact]
    public async Task InvalidLatestFileDoesNotReplaceTheSelectedPdf()
    {
        var (lease, previous, _) = await Seed(MaterialKind.Exam); await File.WriteAllBytesAsync(previous.Path, XlsxChangeReaderTests.Workbook());
        Assert.Equal(new SourceException(SourceFailure.Invalid).Message, (await Materials().ReparseAsync(MaterialKind.Exam, 2026)).Error);
        Assert.Equal(previous.Id, (await _store.ReadAsync<SourceRecord>(lease, "selection.Exam"))!.Id);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task RecoveryDoesNotUseCachedOriginalWhenLatestAcquisitionFails(bool invalid)
    {
        var (lease, previous, good) = await Seed(MaterialKind.Exam);
        await _store.WriteAsync(lease, "attempt.Exam", new MaterialAttempt(_clock.GetUtcNow(), "P13", true, previous.Digest, 2026, ParserVersion: MaterialCoordinator.ParserVersion(MaterialKind.Exam), RecoveryPending: true));
        await _store.WriteAsync(lease, "recovery.Exam", new RecoveryJob(previous.Digest, RecoveryDocumentKind.Exam, RecoveryJobState.Pending, _clock.GetUtcNow()));
        if (invalid) await File.WriteAllTextAsync(previous.Path, "fictional invalid latest PDF"); else File.Delete(previous.Path);
        var coordinator = new RecoveryCoordinator(_store, Materials(), (_, _, _, _, _) => throw new Exception("Cached bytes must not reach recovery"), _ => throw new Exception("Must not initialize model"));
        var result = await coordinator.PrepareAsync(MaterialKind.Exam, 2026);
        Assert.Equal(RecoveryJobState.Failed, result.State); Assert.Null(result.Preview);
        Assert.Equal(good.SourceDigest, (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"))!.SourceDigest);
        Assert.Equal(previous.Id, (await _store.ReadAsync<SourceRecord>(lease, "selection.Exam"))!.Id);
    }
    [Fact]
    public async Task AiDisabledStillRunsRuleRecoveryWithoutInitializingProviders()
    {
        var (lease, _, good) = await Seed(MaterialKind.Exam);
        var permission = new AiFeaturePermission(); var captures = 0;
        var coordinator = new RecoveryCoordinator(_store, Materials(), (_, kind, hash, _, _) =>
        {
            captures++;
            return Task.FromResult(RecoveryDocumentBuilder.Build(hash, kind, [RecoveryPipelineTests.Layout(kind)], (_, _) => true));
        }, _ => throw new Exception("AI provider factory must not run when OFF"), aiPermission: permission);
        var preparation = await coordinator.PrepareAsync(MaterialKind.Exam, 2026);
        Assert.Equal(1, captures); Assert.Equal(RecoveryJobState.AwaitingConfirmation, preparation.State);
        var preview = Assert.IsType<RecoveryPreview>(preparation.Preview);
        Assert.Equal("rule", preview.Result.Metadata.Provider);
        Assert.Equal(good.SourceDigest, (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"))!.SourceDigest);
        await coordinator.AdoptAsync(MaterialKind.Exam, preview);
        Assert.NotNull(await _store.ReadAsync<RecoveryAudit>(lease, "recovery.accepted.Exam." + preview.Document.PdfHash));
    }
    [Fact]
    public async Task SwitchingAiOffCancelsEvenAProviderFactoryThatIgnoresCancellation()
    {
        var (lease, _, good) = await Seed(MaterialKind.Exam);
        var permission = new AiFeaturePermission(); permission.SetEnabled(true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new RecoveryCoordinator(_store, Materials(), (_, kind, hash, _, _) =>
            Task.FromResult(RecoveryDocumentBuilder.Build(hash, kind, [RecoveryPipelineTests.Layout(kind)], (_, _) => true)),
            async _ => { entered.SetResult(); await release.Task; return Array.Empty<ILocalRecoveryProvider>(); }, aiPermission: permission);
        var pending = coordinator.PrepareAsync(MaterialKind.Exam, 2026);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        permission.SetEnabled(false); permission.SetEnabled(true); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(await _store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        Assert.Equal(good.SourceDigest, (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"))!.SourceDigest);
    }
    private async Task<RecoveryPreview> PendingPreview(SchoolLease lease, SourceRecord previous)
    {
        var doc = RecoveryDocumentBuilder.Build(previous.Digest, MaterialKind.Exam, [RecoveryPipelineTests.Layout(MaterialKind.Exam)], (_, _) => true);
        var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null);
        var preview = new RecoveryPreview(previous.Id, lease, doc, Assert.IsType<RecoveryResult>(run.Result), _clock.GetUtcNow());
        await _store.WriteAsync(lease, "attempt.Exam", new MaterialAttempt(_clock.GetUtcNow(), "P13", true, previous.Digest, 2026, ParserVersion: MaterialCoordinator.ParserVersion(MaterialKind.Exam), RecoveryPending: true));
        var job = new RecoveryJob(previous.Digest, doc.Kind, RecoveryJobState.Pending, _clock.GetUtcNow());
        await _store.WriteAsync(lease, "recovery.Exam", job);
        await _store.SaveRecoveryProgressAsync(lease, previous, job with { State = RecoveryJobState.AwaitingConfirmation, ResultHash = RecoveryValidator.Fingerprint(preview.Result) }, preview);
        return preview;
    }
    [Fact]
    public async Task ExplicitReaderIdentityGuardStillRejectsADifferentFileVersion()
    {
        var (_, previous, _) = await Seed(MaterialKind.Exam);
        var failure = await Assert.ThrowsAsync<SourceException>(() => new FileSourceReader(new FakeIdentity()).ReadAsync(previous.Path, MaterialKind.Exam, previous.FileIdentity));
        Assert.Equal(SourceFailure.Replaced, failure.Failure);
    }
    [Fact]
    public async Task CancelledAdoptionDuringLatestReadKeepsPreviewAndLastGood()
    {
        var (lease, previous, good) = await Seed(MaterialKind.Exam); var preview = await PendingPreview(lease, previous);
        using var cancellation = new CancellationTokenSource();
        var identity = new FakeIdentity { Read = _ => { cancellation.Cancel(); return "latest-file"; } };
        var coordinator = new RecoveryCoordinator(_store, Materials(identity), (_, _, _, _, _) => throw new Exception("Unexpected build"), _ => throw new Exception("Unexpected providers"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.AdoptAsync(MaterialKind.Exam, preview, cancellation.Token));
        Assert.NotNull(await _store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        Assert.Equal(previous.Id, (await _store.ReadAsync<SourceRecord>(lease, "selection.Exam"))!.Id);
        Assert.Equal(good.SourceDigest, (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"))!.SourceDigest);
        Assert.Null((await _store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"))!.Recovery);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task FailedLatestAcquisitionBlocksOldPendingAdoptionButKeepsLastGood(bool invalid)
    {
        var (lease, previous, good) = await Seed(MaterialKind.Exam);
        var preview = await PendingPreview(lease, previous);
        if (invalid) await File.WriteAllTextAsync(previous.Path, "fictional invalid replacement"); else File.Delete(previous.Path);
        var coordinator = new RecoveryCoordinator(_store, Materials(), (_, _, _, _, _) => throw new Exception("Unexpected build"), _ => throw new Exception("Unexpected providers"));
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.AdoptAsync(MaterialKind.Exam, preview));
        Assert.NotNull(await _store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Exam"));
        Assert.Equal(previous.Id, (await _store.ReadAsync<SourceRecord>(lease, "selection.Exam"))!.Id);
        var preserved = (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"))!;
        Assert.Equal(good.SourceDigest, preserved.SourceDigest); Assert.Null(preserved.Recovery);
    }
    [Fact]
    public async Task IdenticalByteReplacementKeepsPendingPreviewBoundToSameHashAndCanBeAdopted()
    {
        var (lease, previous, _) = await Seed(MaterialKind.Exam); var preview = await PendingPreview(lease, previous);
        var coordinator = new RecoveryCoordinator(_store, Materials(), (_, _, _, _, _) => throw new Exception("Unexpected build"), _ => throw new Exception("Unexpected providers"));
        await coordinator.AdoptAsync(MaterialKind.Exam, preview);
        var current = (await _store.ReadAsync<SourceRecord>(lease, "selection.Exam"))!;
        Assert.Equal(previous.Id, current.Id); Assert.Equal("latest-file", current.FileIdentity);
        var adopted = (await _store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam"))!;
        Assert.Equal(previous.Digest, adopted.SourceDigest); Assert.Equal(previous.Digest, adopted.Recovery!.Acceptance.PdfHash);
    }
    [Fact]
    public async Task ForegroundRecoveryBuildReceivesLatestPdfAndFreshCaptureAfterStrict()
    {
        var (lease, previous, _) = await Seed(MaterialKind.Exam); var latest = UpdatedPdf(); await File.WriteAllBytesAsync(previous.Path, latest);
        var built = false;
        var coordinator = new RecoveryCoordinator(_store, Materials(), (bytes, kind, hash, capture, _) =>
        {
            built = true; Assert.Equal(latest, bytes); Assert.Equal(MaterialKind.Exam, kind); Assert.Equal(NotificationDiff.Digest(latest), hash); Assert.NotNull(capture);
            throw new InvalidDataException("Fictional build stop after freshness assertions");
        }, _ => throw new Exception("Must not initialize model"));
        var result = await coordinator.PrepareAsync(MaterialKind.Exam, 2026);
        Assert.True(built); Assert.Equal(RecoveryJobState.Failed, result.State);
        Assert.NotEqual(previous.Id, (await _store.ReadAsync<SourceRecord>(lease, "selection.Exam"))!.Id);
    }
}
