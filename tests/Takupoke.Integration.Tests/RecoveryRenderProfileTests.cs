using System.Text.Json;
using Takupoke.Core.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryRenderProfileTests
{
    [Fact]
    public void LegacyPageJsonStaysByteExactAndPngProfileHasItsOwnFingerprint()
    {
        const string legacy = "{\"Page\":1,\"Width\":650,\"Height\":950,\"RasterHash\":\"aaaa\",\"SourceCount\":12,\"InkCoverageVerified\":true}";
        var page = JsonSerializer.Deserialize<RecoveryCapturedPage>(legacy)!;
        Assert.Null(page.RenderProfile); Assert.Equal(legacy,JsonSerializer.Serialize(page));
        var png = page with { RenderProfile = "windows-pdf-png-v1" };
        Assert.NotEqual(RecoveryValidator.Fingerprint(page),RecoveryValidator.Fingerprint(png));
        Assert.Equal(png,JsonSerializer.Deserialize<RecoveryCapturedPage>(JsonSerializer.Serialize(png)));
    }

    [Fact]
    public void RenderingProfileChangeCannotReplayManualProof()
    {
        var doc = RecoveryManualReviewBudgetTests.CaptureRequested(RecoveryManualAssistanceTests.Captured(1),manualSchemaVersion:3);
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc));
        var target = Assert.Single(plan.Targets);
        var correction = RecoveryManualAssistanceTests.Correction(plan,target,target.OriginalOcr) with { ManualSchemaVersion=3 };
        var result = RecoveryManualAssistance.Complete(plan,[correction],"control");
        var changed = doc with { Capture = doc.Capture! with { Pages = doc.Capture!.Pages.Select(p => p with { RenderProfile="windows-pdf-png-v1" }).ToArray() } };
        Assert.False(RecoveryValidator.Validate(changed,result).CanAdopt);
    }
}
