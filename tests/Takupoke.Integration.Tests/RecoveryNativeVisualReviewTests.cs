using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Win.ViewModels;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryNativeVisualReviewTests
{
    private static RecoveryDocument Native(bool header)
    {
        var doc=RecoveryManualAssistanceTests.Captured(0);
        var id=header ? doc.DayEvidence.First().Value[0] : doc.Cells.First(c=>c.LessonBindings.Count>0).LessonBindings[0].Subject[0];
        var sources=doc.Sources.Select(s=>s with { FromOcr=false,NativeConfidence=null,RequiresVisualReview=s.Id==id }).ToArray();
        doc=doc with { Sources=sources, Capture=doc.Capture! with { ManualSchemaVersion=4,SourceSnapshot=RecoveryValidator.Fingerprint(sources),OriginalCrops=null } };
        return RecoveryManualReviewBudgetTests.CaptureRequested(doc,manualSchemaVersion:4);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void NativeUnicodeNeedsOriginalCropAndExplicitAcknowledgementWithoutInventingConfidence(bool header)
    {
        var doc=Native(header);var before=JsonSerializer.SerializeToUtf8Bytes(doc);
        var plan=Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc));
        var target=Assert.Single(plan.Targets);Assert.Equal(header,target.HeaderProof is not null);
        Assert.All(doc.Sources,s=>{Assert.False(s.FromOcr);Assert.Null(s.NativeConfidence);});
        var correction=RecoveryManualAssistanceTests.Correction(plan,target,target.OriginalOcr) with { ManualSchemaVersion=4 };
        var result=RecoveryManualAssistance.Complete(plan,[correction],"fictional-control");
        Assert.True(RecoveryValidator.Validate(doc,result).CanAdopt);
        Assert.False(RecoveryValidator.Validate(doc,result with { HumanCorrections=null }).CanAdopt);
        Assert.Null(RecoveryManualAssistance.Prepare(doc with { Capture=doc.Capture! with { OriginalCrops=null } }));
        Assert.Equal(before,JsonSerializer.SerializeToUtf8Bytes(doc));
        Assert.Throws<InvalidRecoveryOutputException>(()=>RecoveryManualAssistance.Complete(plan,[correction with { ManualSchemaVersion=3 }],"control"));
        if(header) Assert.Throws<InvalidRecoveryOutputException>(()=>RecoveryManualAssistance.Complete(plan,[correction with { CorrectedText="架空の変更" }],"control"));
        else {
            var corrected=RecoveryManualAssistance.Complete(plan,[correction with { CorrectedText="架空の訂正" }],"control");
            Assert.True(RecoveryValidator.Validate(doc,corrected).CanAdopt);
        }
    }

    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(3)]
    public void HistoricalManualSchemasCannotAuthorizeNativeVisualUncertainty(int version)
    {
        var doc=Native(false);doc=doc with { Capture=doc.Capture! with { ManualSchemaVersion=version } };
        Assert.Null(RecoveryManualAssistance.Prepare(doc));
        Assert.Contains("nativeCapture",RecoveryValidator.InputErrors(doc));
    }

    [Fact]
    public void NativeReviewInputRequiresAcknowledgementAndKeepsHeadersReadOnly()
    {
        var state=new RecoveryManualInputState();state.Bind("native",new Dictionary<string,string>{{"header","架空見出し"}},4,readOnlyKeys:["header"]);
        Assert.False(state.Ready);Assert.Throws<InvalidOperationException>(()=>state.Edit("header","変更"));Assert.Equal("架空見出し",state.Values["header"]);
        state.Acknowledge("header",true);Assert.True(state.Ready);Assert.Single(state.Submission());
    }
}
