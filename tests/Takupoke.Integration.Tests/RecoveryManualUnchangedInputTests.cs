using Takupoke.Win.ViewModels;
using Xunit;
namespace Takupoke.Integration.Tests;
public sealed class RecoveryManualUnchangedInputTests
{
    private static RecoveryManualInputState Bound()
    {
        var s = new RecoveryManualInputState();
        s.Bind("fictional-pdf/snapshot/lease", new Dictionary<string,string> { ["subject"] = "架空科目甲", ["teacher"] = "架空教員乙", ["room"] = "架空教室丙" });
        foreach (var k in s.Values.Keys) s.Acknowledge(k, true);
        return s;
    }
    [Fact]
    public void IdenticalTextSynchronizationPreservesAckAndReady()
    {
        var s=Bound();
        for(int i=0;i<5;i++) s.Edit("subject", "架空科目甲");
        Assert.True(s.IsAcknowledged("subject")); Assert.True(s.Ready);
        Assert.Equal("架空科目甲",s.Submission()["subject"]);
    }
    [Fact]
    public void ActualEditResetsOnlyItsOwnAcknowledgement()
    {
        var s=Bound();s.Edit("subject", "架空科目改訂");
        Assert.False(s.IsAcknowledged("subject"));Assert.True(s.IsAcknowledged("teacher"));Assert.True(s.IsAcknowledged("room"));Assert.False(s.Ready);
    }
    [Fact]
    public void ChangingBackToOriginalStillRequiresNewAcknowledgement()
    {
        var s=Bound();s.Edit("subject","架空別科目");s.Edit("subject","架空科目甲");Assert.False(s.Ready);
        s.Acknowledge("subject",true);s.Edit("subject","架空科目甲");Assert.True(s.Ready);
    }
    [Fact]
    public void ImeCompositionChangesRevokeAckButRepeatedCompositionValueDoesNot()
    {
        var s=Bound();s.Edit("subject","架空か");Assert.False(s.Ready);
        s.Acknowledge("subject",true);s.Edit("subject","架空か");Assert.True(s.Ready);
        s.Edit("subject","架空科");Assert.False(s.Ready);
    }
    [Fact]
    public void OrdinalUnicodeChangeIsAnActualEdit()
    {
        var s=Bound();s.Edit("subject","架空e\u0301");s.Acknowledge("subject",true);
        s.Edit("subject","架空é");Assert.False(s.Ready);
    }
    [Fact]
    public void HashSnapshotChangeStillClearsAcknowledgements()
    {
        var s=Bound();s.Bind("fictional-other-hash/snapshot/lease",new Dictionary<string,string>(s.Values));Assert.False(s.Ready);
    }
}
