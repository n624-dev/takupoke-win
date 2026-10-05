using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryManualLifecycleTests
{
    [Fact]
    public async Task PendingDraftAndWholePreviewDoNotSaveUntilExplicitAdoptionAndSurviveRestart()
    {
        await using var c = await Context.CreateAsync();
        var pending = await c.Coordinator.PrepareAsync(MaterialKind.Timetable, 2032);
        var session = Assert.IsType<RecoveryManualSession>(pending.ManualSession);
        Assert.Equal(RecoveryJobState.AwaitingManualCorrection, pending.State);
        await c.AssertLastGoodAsync();
        await c.RestartAsync();
        var job = Assert.IsType<RecoveryJob>(await c.Store.ReadAsync<RecoveryJob>(c.Lease, "recovery.Timetable"));
        var resumed = new RecoveryManualSession(c.Source.Id, c.Lease, job.ManualPlan!, job.CreatedAt);
        Assert.Equal(RecoveryValidator.Fingerprint(session.Plan), RecoveryValidator.Fingerprint(resumed.Plan));
        var preview = await c.Coordinator.CompleteManualAsync(MaterialKind.Timetable, resumed, c.Values(resumed));
        Assert.Single(preview.Result.HumanCorrections!); await c.AssertLastGoodAsync();
        Assert.Null((await c.Store.ReadAsync<RecoveryJob>(c.Lease, "recovery.Timetable"))!.ManualPlan);
        await c.Coordinator.AdoptAsync(MaterialKind.Timetable, preview);
        var formal = Assert.IsType<MaterialAnalysis>(await c.Store.ReadAsync<MaterialAnalysis>(c.Lease, "analysis.Timetable"));
        Assert.Contains(formal.Timetable!.Lessons, l => l.Names.Subject == "架空訂正科目");
        Assert.Single(formal.Recovery!.Result.HumanCorrections!);
        Assert.True(RecoveryAuditCertification.IsCurrent(formal.Recovery));
        await c.RestartAsync();
        var after = Assert.IsType<MaterialAnalysis>(await c.Store.ReadAsync<MaterialAnalysis>(c.Lease, "analysis.Timetable"));
        Assert.Equal(RecoveryValidator.Fingerprint(formal), RecoveryValidator.Fingerprint(after));
        Assert.True(RecoveryAnalysisConverter.MayDisplay(after));
        await c.AssertHistoryAndBlobAsync();
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("incomplete")]
    [InlineData("stale")]
    [InlineData("sourceFailure")]
    [InlineData("snapshot")]
    public async Task CancelRefusalAndChangedOrUnreadableSourcePreserveLastGoodHistoryAndBlob(string fault)
    {
        await using var c = await Context.CreateAsync();
        var pending = await c.Coordinator.PrepareAsync(MaterialKind.Timetable, 2032);
        var session = Assert.IsType<RecoveryManualSession>(pending.ManualSession);
        if (fault == "cancel")
        {
            await c.Coordinator.CancelManualAsync(MaterialKind.Timetable, session);
            Assert.Null((await c.Store.ReadAsync<RecoveryJob>(c.Lease, "recovery.Timetable"))!.ManualPlan);
        }
        else
        {
            var values = c.Values(session);
            if (fault == "incomplete") values.Clear();
            if (fault == "stale") await File.AppendAllTextAsync(c.Source.Path, "\n% independently fictional updated source\n");
            if (fault == "sourceFailure") File.Delete(c.Source.Path);
            if (fault == "snapshot") session = session with { Plan = session.Plan with { DocumentSnapshot = new string('b', 64) } };
            var failure = await Record.ExceptionAsync(() => c.Coordinator.CompleteManualAsync(MaterialKind.Timetable, session, values));
            Assert.True(failure is InvalidDataException or InvalidRecoveryOutputException or OperationCanceledException, failure?.ToString());
            if (fault == "sourceFailure") Assert.Null(await c.Store.ReadAsync<RecoveryJob>(c.Lease, "recovery.Timetable"));
        }
        await c.AssertLastGoodAsync();
        Assert.Null(await c.Store.ReadAsync<RecoveryPreview>(c.Lease, "recovery.preview.Timetable"));
    }
    [Theory]
    [InlineData(4, true)]
    [InlineData(1, false)]
    public async Task IneligibleLowConfidenceAcquisitionFailsInsteadOfWaitingForAnotherModel(int fields, bool completeCapture)
    {
        await using var c = await Context.CreateAsync(fields, completeCapture);
        var pending = await c.Coordinator.PrepareAsync(MaterialKind.Timetable,2032);
        Assert.Equal(RecoveryJobState.Failed,pending.State);
        Assert.Null(pending.ManualSession); Assert.Null(pending.Preview);
        var job = Assert.IsType<RecoveryJob>(await c.Store.ReadAsync<RecoveryJob>(c.Lease,"recovery.Timetable"));
        Assert.Equal(RecoveryJobState.Failed,job.State); Assert.Null(job.ManualPlan);
        await c.AssertLastGoodAsync();
    }
    [Fact]
    public async Task UnreadableOriginalAfterHumanPreviewInvalidatesOnlyPendingCorrection()
    {
        await using var c = await Context.CreateAsync();
        var session = (await c.Coordinator.PrepareAsync(MaterialKind.Timetable, 2032)).ManualSession!;
        var preview = await c.Coordinator.CompleteManualAsync(MaterialKind.Timetable, session, c.Values(session));
        File.Delete(c.Source.Path);
        await Assert.ThrowsAsync<InvalidDataException>(() => c.Coordinator.AdoptAsync(MaterialKind.Timetable, preview));
        Assert.Null(await c.Store.ReadAsync<RecoveryPreview>(c.Lease, "recovery.preview.Timetable"));
        Assert.Null(await c.Store.ReadAsync<RecoveryJob>(c.Lease, "recovery.Timetable"));
        await c.AssertLastGoodAsync();
    }
    private sealed class Context : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-fictional-manual-" + Guid.NewGuid().ToString("N"));
        private readonly Protector _protector = new(); private readonly Clock _clock = new();
        public SchoolDataStore Store { get; private set; } = null!;
        public SchoolLease Lease { get; private set; }
        public SourceRecord Source { get; private set; } = null!;
        public RecoveryCoordinator Coordinator { get; private set; } = null!;
        private byte[] _bytes = PdfParsingTests.SyntheticPdf();
        private string _historyHash = "", _lastGoodHash = "";
        private int _fields = 1; private bool _completeCapture = true;
        private const string HistoryKey = "recovery.accepted.Timetable.independent-last-good";
        public static async Task<Context> CreateAsync(int fields = 1, bool completeCapture = true)
        {
            var c = new Context { _fields = fields, _completeCapture = completeCapture }; try { await c.InitializeAsync(); return c; } catch { await c.DisposeAsync(); throw; }
        }
        private async Task InitializeAsync()
        {
            Directory.CreateDirectory(_root); var path = Path.Combine(_root, "fictional.pdf"); await File.WriteAllBytesAsync(path, _bytes);
            Store = new(Path.Combine(_root, "data"), _protector, _clock); Lease = await Store.BeginAsync(); var now = _clock.GetUtcNow();
            Source = new("fictional-manual", MaterialKind.Timetable, path, "fictional-identity", "fictional.pdf", NotificationDiff.Digest(_bytes), _bytes.Length, now, now, null);
            await Store.SaveOriginalAsync(Lease, Source, _bytes);
            var lastGood = new MaterialAnalysis(Source.Id, Source.Kind, 24, Source.Digest, Source.OriginalName, now.AddDays(-3), 2032,
                Timetable: new(2032, "前期", [new("3_CN", 1, 1, new("架空以前正常科目"), "架空以前原文", 1)]));
            await Store.SaveAnalysisAsync(Lease, lastGood); _lastGoodHash = RecoveryValidator.Fingerprint(lastGood);
            var original = RecoveryManualAssistanceTests.Captured(1);
            var historyDoc = original with { PdfHash = new string('d',64), Capture = null, Sources = original.Sources.Select(s => s with { NativeConfidence = null }).ToArray() };
            var run = await RecoveryEngine.RunAsync(historyDoc, "windows", 10, true, [], _ => null);
            Assert.NotNull(run.Result);
            var audit = new RecoveryAudit(historyDoc, run.Result!, new(historyDoc.PdfHash, RecoveryValidator.Fingerprint(run.Result!), RecoveryValidator.Fingerprint(historyDoc), run.Result!.Metadata, now.AddDays(-3)));
            await Store.WriteAsync(Lease, HistoryKey, audit); _historyHash = RecoveryValidator.Fingerprint(audit);
            Wire();
        }
        private void Wire()
        {
            var materials = new MaterialCoordinator(Store, new(new FakeIdentity()), _clock);
            // Actual selection/refresh/coordinator/encrypted-store lifecycle, with a documented
            // source-only Builder seam. The minimal PDF is not an OCR/extraction truth fixture.
            Coordinator = new(Store, materials, (_,_,hash,_,token) => {
                token.ThrowIfCancellationRequested(); var doc = RecoveryManualAssistanceTests.Captured(_fields);
                return Task.FromResult(doc with { PdfHash = hash, Capture = _completeCapture ? doc.Capture! with { PdfHash = hash } : null });
            }, _ => Task.FromResult<IReadOnlyList<ILocalRecoveryProvider>>([]), _clock);
        }
        public Dictionary<string,string> Values(RecoveryManualSession session) => session.Plan.Targets.ToDictionary(t => t.Target.Key, _ => "架空訂正科目");
        public async Task RestartAsync() { await Store.DisposeAsync(); Store = new(Path.Combine(_root, "data"), _protector, _clock); Lease = await Store.BeginAsync(); Wire(); }
        public async Task AssertLastGoodAsync()
        {
            Assert.Equal(_lastGoodHash, RecoveryValidator.Fingerprint((await Store.ReadAsync<MaterialAnalysis>(Lease, "analysis.Timetable"))!));
            await AssertHistoryAndBlobAsync();
        }
        public async Task AssertHistoryAndBlobAsync()
        {
            Assert.Equal(_historyHash, RecoveryValidator.Fingerprint((await Store.ReadAsync<RecoveryAudit>(Lease, HistoryKey))!));
            Assert.Equal(_bytes, await Store.ReadOriginalAsync(Lease, Source.Id));
        }
        public async ValueTask DisposeAsync() { if (Store is not null) await Store.DisposeAsync(); _protector.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2032,4,1,12,0,0,TimeSpan.Zero); }
    private sealed class FakeIdentity : IFileIdentityProvider { public string Identity(FileStream stream) => "fictional-identity"; }
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key,"fictional-key"); public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes,"fictional-key");
        public void Dispose() => _cipher.Dispose();
    }
}
