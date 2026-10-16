using System.Collections;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryAuditCacheCallOrderTests
{
    private sealed class CountOnly : IReadOnlyList<string>
    {
        public int EnumerationEntries { get; private set; }
        public int Count => 20_000_001;
        public string this[int index] => "fictional-evidence";
        public IEnumerator<string> GetEnumerator()
        { EnumerationEntries++; throw new InvalidOperationException("must reject pre-hash Count without enumerating"); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static RecoveryAudit Audit(int origin)
    {
        var doc = RecoveryParallelProofTests.Build(RecoveryParallelProofTests.TwoPages("wholeformal"));
        var cells = doc.Cells.Select(c => c.ConfirmedEmpty ? new RecoveredCell(c.Id,RecoveryValueState.Empty,[]) : RecoveryRules.Recover(doc,c)!).ToArray();
        var result = new RecoveryResult(doc.PdfHash,doc.Kind,doc.SchoolYear,doc.Term,cells,new("rule","rules","3","3","1",2,origin,"test"));
        return new(doc,result,new(doc.PdfHash,RecoveryValidator.Fingerprint(result),RecoveryValidator.Fingerprint(doc),result.Metadata,DateTimeOffset.Parse("2032-04-01T00:00:00Z")));
    }
    [Fact]
    public void HistoricalCacheNeverFingerprintsBeforeRejectingOverLimitCount()
    {
        var old = Audit(8); var evidence = new CountOnly();
        old = old with { Document = old.Document with { TimeEvidence = evidence } };
        Assert.Null(RecoveryAuditCertification.Reusable(old));
        Assert.Equal(0,evidence.EnumerationEntries);
        Assert.False(RecoveryAuditCertification.IsCurrent(old));
        Assert.Equal(0,evidence.EnumerationEntries);
    }
    [Fact]
    public void OrdinaryCurrent9AuditKeepsExistingReuseWithoutCertification()
    {
        var current=Audit(RecoveryValidator.Version);
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.Same(current,RecoveryAuditCertification.Reusable(current));
        Assert.Null(current.CurrentCertification); Assert.Null(current.PreviousCertification);
    }
}
