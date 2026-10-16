using Takupoke.Core.Recovery;

namespace Takupoke.Infrastructure.Recovery;

// Evidence context is distinct from the physical header that owns the body.
internal static class OcrHeaderContext
{
    public static RecoveryBox? Extend(RecoveryBox physical, RecoveryBox evidence, Func<RecoveryBox,bool> inkFree)
    {
        if (!physical.Valid || !evidence.Valid) throw new InvalidDataException("OCR見出しの範囲が不正です。");
        var x=Math.Min(evidence.X,physical.X);var y=Math.Min(evidence.Y,physical.Y);
        var endX=Math.Max(evidence.X+evidence.Width,physical.X+physical.Width);
        var endY=Math.Max(evidence.Y+evidence.Height,physical.Y+physical.Height);
        var fringes=new List<RecoveryBox>();
        // Prove the entire added rectangle, including corners far from a CTC peak.
        if(y<physical.Y)fringes.Add(new(x,y,endX-x,physical.Y-y));
        if(endY>physical.Y+physical.Height)fringes.Add(new(x,physical.Y+physical.Height,endX-x,endY-physical.Y-physical.Height));
        if(x<physical.X)fringes.Add(new(x,y,physical.X-x,endY-y));
        if(endX>physical.X+physical.Width)fringes.Add(new(physical.X+physical.Width,y,endX-physical.X-physical.Width,endY-y));
        return fringes.All(f=>f.Valid && inkFree(f)) ? new(x,y,endX-x,endY-y) : null;
    }
}
