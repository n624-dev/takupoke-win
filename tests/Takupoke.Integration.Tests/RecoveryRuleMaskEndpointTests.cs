using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;
namespace Takupoke.Integration.Tests;
public sealed class RecoveryRuleMaskEndpointTests
{
    private static RecoveryRaster Border()
    {
        var pixels=Enumerable.Repeat((byte)255,100*100*4).ToArray();var raster=new RecoveryRaster(100,100,pixels);
        for(var p=10;p<=90;p++){Ink(raster,p,10);Ink(raster,p,11);Ink(raster,p,90);Ink(raster,p,91);Ink(raster,10,p);Ink(raster,11,p);Ink(raster,90,p);Ink(raster,91,p);}
        return raster;
    }
    private static void Ink(RecoveryRaster r,int x,int y,byte color=0){var p=(y*r.Width+x)*4;for(var c=0;c<3;c++)r.Bgra[p+c]=color;}
    private static bool Masked(bool[] mask, int x,int y)=>mask[y*100+x];
    [Fact] public void ActualTwoLaneCornerUnionDoesNotLoseEntirePhysicalBorders()
    {
        var r=Border();var rules=r.Rules();Assert.Equal(4,rules.Count);Assert.Contains(rules,l=>l.Vertical&&l.X1==90.5&&l.Y2==91);Assert.Contains(rules,l=>l.Horizontal&&l.Y1==90.5&&l.X2==91);
        Assert.Equal(255,r.Bgra[(91*100+91)*4]);
        var mask=r.RuleMask(rules);Assert.True(Masked(mask,50,91));Assert.True(Masked(mask,91,50));Assert.False(Masked(mask,91,91));
        Assert.False(r.HasUnrecognizedInk([],rules));Assert.True(r.InkFree(new(0,0,100,100),mask));
        Assert.All(Enumerable.Range(0,mask.Length).Where(i=>mask[i]),i=>Assert.NotEqual(255,r.Bgra[i*4]));
    }
    [Theory] [InlineData(12,50)] [InlineData(50,12)] [InlineData(89,50)] [InlineData(50,89)] [InlineData(92,50)]
    public void NearbyMissedGlyphOnePixelAwayRemainsUnmasked(int x,int y)
    {
        var r=Border();var rules=r.Rules();Ink(r,x,y,254);var mask=r.RuleMask(rules);Assert.False(Masked(mask,x,y));Assert.True(r.HasUnrecognizedInk([],rules));Assert.False(r.InkFree(new(0,0,100,100),mask));
    }
    [Fact] public void AttachedEndpointShortStrokeOutsideDeclaredRangeIsNeverMasked()
    {
        var r=Border();var rules=r.Rules();Ink(r,92,90);Ink(r,93,90);var mask=r.RuleMask(rules);Assert.False(Masked(mask,92,90));Assert.False(Masked(mask,93,90));Assert.True(r.HasUnrecognizedInk([],rules));
    }
    [Fact] public void InternalWhiteHoleCannotBeBridgedByEndpointCorrection()
    {
        var r=Border();var rules=r.Rules();Ink(r,50,91,255);var mask=r.RuleMask(rules);Assert.False(Masked(mask,49,91));Assert.False(Masked(mask,51,91));Assert.True(r.HasUnrecognizedInk([],rules));
    }
    [Fact] public void GeometricPhantomPerpendicularRulesDoNotProvePrintedEndpointSupport()
    {
        var r=new RecoveryRaster(100,100,Enumerable.Repeat((byte)255,100*100*4).ToArray());for(var x=10;x<=90;x++)Ink(r,x,50);
        PdfRule[] rules=[new(10,50,91,50),new(10.5,10,10.5,90),new(90.5,10,90.5,90)];Assert.False(Masked(r.RuleMask(rules),50,50));
    }
    [Fact] public void ShortPrintedEndpointStubsCannotReplaceFullPerpendicularBorders()
    {
        var r=new RecoveryRaster(100,100,Enumerable.Repeat((byte)255,100*100*4).ToArray());for(var x=10;x<=90;x++)Ink(r,x,50);for(var y=49;y<=51;y++){Ink(r,10,y);Ink(r,90,y);}
        PdfRule[] rules=[new(10,50,91,50),new(10.5,10,10.5,90),new(90.5,10,90.5,90)];Assert.False(Masked(r.RuleMask(rules),50,50));
    }
    [Fact] public void MoreThanTwoWhiteEndpointPixelsCannotBeSkipped()
    {
        var r=Border();var rules=r.Rules();var bottom=rules.Single(l=>l.Horizontal&&l.Y1==90.5);var altered=rules.Select(l=>l==bottom?l with { X1=7 }:l).ToArray();Assert.False(Masked(r.RuleMask(altered),50,91));
    }
    [Fact] public void MissingOrCompetingPerpendicularEndpointCannotAuthorizeTrim()
    {
        var r=Border();var rules=r.Rules();var bottom=rules.Single(l=>l.Horizontal&&l.Y1==90.5);
        Assert.False(Masked(r.RuleMask([bottom]),50,91));
        var ambiguous=rules.Append(new PdfRule(90,10,90,91)).ToArray();Assert.False(Masked(r.RuleMask(ambiguous),50,91));
    }
    [Fact] public void TwoWhiteEndPixelsFailWhenRemainingSupportMissesMeasuredCenter()
    {
        var r=Border();var rules=r.Rules();Ink(r,90,91,255);Assert.False(Masked(r.RuleMask(rules),50,91));
    }
    [Fact] public void TrimmedStartMustCoverMeasuredCenterWithoutRoundingTolerance()
    {
        var r=Border();var rules=r.Rules();Ink(r,10,91,255);Assert.False(Masked(r.RuleMask(rules),50,91));
    }
    [Fact] public void GrayEndpointInkIsNeverRemovedAsWhite()
    {
        var r=Border();var rules=r.Rules();Ink(r,91,91,254);var mask=r.RuleMask(rules);Assert.True(Masked(mask,91,91));Assert.Equal(254,r.Bgra[(91*100+91)*4]);
    }
    [Fact] public void SharedPixelBudgetAndCancellationRemainTerminal()
    {
        var r=Border();var rules=r.Rules();using var cancelled=new CancellationTokenSource();cancelled.Cancel();Assert.Throws<OperationCanceledException>(()=>r.RuleMask(rules,cancelled.Token));
        var work=new RecoveryRaster.PixelWork(default);work.Step(63_999_999);var error=Assert.Throws<InvalidDataException>(()=>r.RuleMask(rules,default,work));Assert.True(RecoveryWorkLimits.IsExceeded(error));
    }
    private static RecoveryRaster UnevenCorner()
    {
        var r=Border(); Ink(r,10,91,255); Ink(r,11,91); return r;
    }
    [Fact] public void OriginalPhysicalIntersectionSurvivesAveragedCenterOutsideFringe()
    {
        var r=UnevenCorner(); var rules=r.Rules();
        Assert.Contains(rules,l=>l.Vertical&&l.X1==10.5);
        var mask=r.RuleMask(rules); Assert.True(Masked(mask,50,91));
        Assert.False(Masked(mask,10,91)); Assert.False(r.HasUnrecognizedInk([],rules));
    }
    [Fact] public void PlainCopiedRulesCannotClaimOriginalPixelProvenance()
    {
        var r=UnevenCorner(); var rules=r.Rules();
        Assert.False(Masked(r.RuleMask(rules.ToArray()),50,91));
        var other=new RecoveryRaster(r.Width,r.Height,r.Bgra.ToArray());
        Assert.False(Masked(other.RuleMask(rules),50,91));
    }
    [Fact] public void PixelMutationInvalidatesEvenUnchangedBorderInventory()
    {
        var r=UnevenCorner(); var rules=r.Rules(); Ink(r,50,50,254);
        Assert.False(Masked(r.RuleMask(rules),50,91));
        Assert.True(r.HasUnrecognizedInk([],rules));
    }
    [Theory] [InlineData(12,50)] [InlineData(92,50)]
    public void FreshNearbyGlyphCannotBeOwnedByPhysicalCornerProof(int x,int y)
    {
        var r=UnevenCorner(); Ink(r,x,y,254); var rules=r.Rules(); var mask=r.RuleMask(rules);
        Assert.False(Masked(mask,x,y)); Assert.True(r.HasUnrecognizedInk([],rules));
    }
    [Fact] public void FreshWhiteHoleCannotBeHiddenUsingAnotherPhysicalLane()
    {
        var r=UnevenCorner(); Ink(r,50,91,255); var rules=r.Rules();
        var mask=r.RuleMask(rules); Assert.False(Masked(mask,49,91)); Assert.False(Masked(mask,51,91));
        Assert.True(r.HasUnrecognizedInk([],rules));
    }
    [Fact] public void FreshAttachedTailOutsidePhysicalIntersectionStaysUnmasked()
    {
        var r=UnevenCorner(); Ink(r,92,91); Ink(r,93,91);
        var rules=r.Rules(); var mask=r.RuleMask(rules);
        Assert.False(Masked(mask,92,91)); Assert.False(Masked(mask,93,91));
        Assert.True(r.HasUnrecognizedInk([],rules));
    }
}
