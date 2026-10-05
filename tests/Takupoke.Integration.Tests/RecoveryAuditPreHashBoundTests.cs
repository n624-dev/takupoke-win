using System.Collections;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryAuditPreHashBoundTests
{
    private sealed class CountOnly<T>(T value) : IReadOnlyList<T>
    {
        public int Count => 20_000_001;
        public T this[int index] => value;
        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("must reject Count before enumeration or hashing");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static RecoveryAudit Original8()
    {
        var doc = RecoveryParallelProofTests.Build(RecoveryParallelProofTests.TwoPages("wholeformal"));
        var cells = doc.Cells.Select(c => c.ConfirmedEmpty ? new RecoveredCell(c.Id, RecoveryValueState.Empty, []) : RecoveryRules.Recover(doc, c)!).ToArray();
        var result = new RecoveryResult(doc.PdfHash, doc.Kind, doc.SchoolYear, doc.Term, cells, new("rule", "rules", "3", "3", "1", 2, 8, "test"));
        return new(doc, result, new(doc.PdfHash, RecoveryValidator.Fingerprint(result), RecoveryValidator.Fingerprint(doc), result.Metadata, DateTimeOffset.Parse("2032-04-01T00:00:00Z")));
    }
    private static RecoverySemanticCertification Certificate(RecoveryAudit audit) => new(2, 9,
        audit.Acceptance.ScopeHash, audit.Acceptance.ResultHash, RecoveryValidator.Fingerprint(audit.Acceptance),
        RecoveryValidator.Fingerprint(audit.PreviousAcceptance)) { PreviousCertificationHash = RecoveryValidator.Fingerprint(null as RecoverySemanticCertification) };

    [Theory]
    [InlineData("metadata")] [InlineData("result")] [InlineData("certificate")] [InlineData("predecessor")]
    [InlineData("aggregateSources")] [InlineData("cropBytes")]
    [InlineData("evidenceCount")] [InlineData("bindingCount")] [InlineData("replicaCount")]
    public void CompleteAuditBudgetPrecedesEveryCertificationHash(string kind)
    {
        var audit = Original8(); var cert = Certificate(audit); RecoverySemanticCertification? predecessor = null;
        if (kind is "metadata" or "result" or "certificate" or "predecessor")
        {
            var huge = new string('x', 20_000_001);
            if (kind == "metadata") audit = audit with { Result = audit.Result with { Metadata = audit.Result.Metadata with { ModelVersion = huge } } };
            if (kind == "result") {
                var cells = audit.Result.Cells.ToArray(); var ci = Array.FindIndex(cells, c => c.Lessons.Count > 0);
                var lessons = cells[ci].Lessons.ToArray(); lessons[0] = lessons[0] with { Teacher = lessons[0].Teacher with { Value = huge } };
                cells[ci] = cells[ci] with { Lessons = lessons }; audit = audit with { Result = audit.Result with { Cells = cells } };
            }
            if (kind == "certificate") cert = cert with { ScopeHash = huge };
            if (kind == "predecessor") predecessor = cert with { ValidatorVersion = 8, ScopeHash = huge };
        }
        if (kind == "aggregateSources") {
            var source = audit.Document.Sources[0] with { Text = new string('x', 4096) };
            audit = audit with { Document = audit.Document with { Sources = Enumerable.Repeat(source, 100_000).ToArray() } };
        }
        if (kind == "cropBytes") audit = audit with { Document = audit.Document with { Capture = new("pdf", 1, "snapshot", []) {
            OriginalCrops = [new(new("fictional", 0, RecoveryFieldRole.Teacher), 1, 0, 0, 1, 1, "raster", new byte[20_000_001], "hash")] } } };
        if (kind == "evidenceCount") audit = audit with { Document = audit.Document with { TimeEvidence = new CountOnly<string>("id") } };
        if (kind == "bindingCount") {
            var cells = audit.Document.Cells.ToArray(); cells[0] = cells[0] with { LessonBindings = new CountOnly<RecoveryLessonBinding>(new([], [], [])) };
            audit = audit with { Document = audit.Document with { Cells = cells } };
        }
        if (kind == "replicaCount") audit = audit with { Document = audit.Document with {
            ClockReplicas = new Dictionary<string, IReadOnlyList<RecoveryClockBinding>> { ["key"] = new CountOnly<RecoveryClockBinding>(new(1, new(0,0,1,1), "日", 1,1, [], null, [], new(1,new(0,0,1,1),RecoveryHeaderAxis.Above))) } } };
        Assert.Equal(new[] { "validationLimit" }, RecoveryValidator.ValidateCertifiedAudit(audit.Document, audit.Result, audit.Acceptance, audit.PreviousAcceptance, cert, predecessor).Errors);
        if (kind is not ("certificate" or "predecessor")) {
            Assert.False(RecoveryValidator.HistoricalAcceptanceEnvelopeValid(audit.Document, audit.Result, audit.Acceptance, audit.PreviousAcceptance));
            Assert.Null(RecoveryAuditCertification.Reusable(audit));
        }
    }

    [Fact]
    public void OversizedManualPredecessorFailsClosedBeforeItsHashIsComputed()
    {
        var old = RecoveryManualCertificationTests.Historical(true);
        var huge = new RecoverySemanticCertification(2,8,old.Acceptance.ScopeHash,old.Acceptance.ResultHash,
            RecoveryValidator.Fingerprint(old.Acceptance),RecoveryValidator.Fingerprint(old.PreviousAcceptance))
            { PreviousCertificationHash = new string('x',20_000_001) };
        Assert.Null(RecoveryAuditCertification.Reusable(old with { CurrentCertification = huge }));
    }
    [Fact]
    public void SafeOriginal8StillCertifiesWithoutRewritingOriginalBytes()
    {
        var old = Original8(); var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.Reusable(old));
        Assert.Equal(RecoveryValidator.Fingerprint(old),RecoveryValidator.Fingerprint(current with { CurrentCertification = null }));
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
    }
    [Fact]
    public void HistoricalManualWrapperKeepsMalformedInputBoundary()
    {
        var old = Original8(); var cert = Certificate(old);
        Assert.Equal(new[] { "malformedInput" },RecoveryValidator.ValidateHistoricalManual(old.Document,null!,old.Acceptance,null,cert).Errors);
        Assert.Equal(new[] { "malformedInput" },RecoveryValidator.ValidateHistoricalManual(old.Document,old.Result with { Metadata = null! },old.Acceptance,null,cert).Errors);
    }
    [Fact]
    public void HistoricalManualWrapperPreservesCancellationEvenForIneligibleOrigins()
    {
        var old=Original8(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>RecoveryValidator.ValidateHistoricalManual(old.Document,old.Result,old.Acceptance,null,Certificate(old),cancelled.Token));
    }
    [Fact]
    public void CertificationAndHistoricalEnvelopeRespectCancellation()
    {
        var old=Original8(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>RecoveryValidator.ValidateCertifiedAudit(old.Document,old.Result,old.Acceptance,null,Certificate(old),token:cancelled.Token));
        Assert.Throws<OperationCanceledException>(()=>RecoveryValidator.HistoricalAcceptanceEnvelopeValid(old.Document,old.Result,old.Acceptance,null,cancelled.Token));
    }
}
