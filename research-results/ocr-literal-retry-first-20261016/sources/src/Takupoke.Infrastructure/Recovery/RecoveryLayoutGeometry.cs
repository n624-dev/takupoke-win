using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;

public static class RecoveryLayoutGeometry
{
    public static RecoveryBox Scale(RecoveryBox box,double sx,double sy)
    {
        if(!double.IsFinite(sx) || !double.IsFinite(sy) || sx<=0 || sy<=0) throw new ArgumentOutOfRangeException(nameof(sx));
        var x=box.X*sx;var y=box.Y*sy;
        return new(x,y,(box.X+box.Width)*sx-x,(box.Y+box.Height)*sy-y);
    }
    // Only callers holding the bounded native command-coverage proof may use
    // this predicate. Opaque cell shading is not unrecognized text; no pixels
    // are whitened, and original page/crop receipts still use native rendering.
    public static bool NativeTextFree(PdfPageLayout page,RecoveryBox box,CancellationToken token=default)
        => NativeBlankScanner(page,token)(box);

    public static Func<RecoveryBox,bool> NativeBlankScanner(PdfPageLayout page,CancellationToken token=default)
    {
        long work=0;
        return box=>
        {
            token.ThrowIfCancellationRequested();
            foreach(var glyph in page.Glyphs)
            {
                if(++work>64_000_000)throw RecoveryWorkLimits.Exceeded("原文の空欄確認数が上限を超えています。");
                if(work%128==0)token.ThrowIfCancellationRequested();
                if(!string.IsNullOrWhiteSpace(glyph.Text) && RecoverySourceCoverage.Intersects(box,glyph.InkBounds is {} b ? new(b.Left,b.Top,b.Right-b.Left,b.Bottom-b.Top) : new(glyph.X,glyph.Y,glyph.Width,glyph.Height))) return false;
            }
            token.ThrowIfCancellationRequested();return true;
        };
    }
    // Parse native text in its original point geometry. Project ALL resulting
    // provenance into rendered pixels together, so raster resolution does not
    // change row grouping or source ownership.
    public static RecoveryDocument Project(RecoveryDocument doc,IReadOnlyList<(double X,double Y)> scales,CancellationToken token=default)
    {
        if(doc.Capture is not null)throw new ArgumentException("Project before raster receipts are attached.",nameof(doc));
        RecoveryBox Box(RecoveryBox b,int page)
        { token.ThrowIfCancellationRequested();if(page<1 || page>scales.Count)throw new ArgumentException(nameof(scales));return Scale(b,scales[page-1].X,scales[page-1].Y); }
        RecoveryHeaderRegion? Region(RecoveryHeaderRegion? r) => r is null ? null : r with {Box=Box(r.Box,r.Page)};
        RecoveryClockBinding Clock(RecoveryClockBinding c) => c with {Box=Box(c.Box,c.Page),DayRegion=Region(c.DayRegion),PeriodRegion=Region(c.PeriodRegion)!};
        var projected=doc with {
            Sources=doc.Sources.Select(s=>s with {Box=Box(s.Box,s.Page),NativeLayoutBox=s.NativeLayoutBox is {} layout ? Box(layout,s.Page) : null}).ToArray(),
            Cells=doc.Cells.Select(c=>c with {
                Box=Box(c.Box,c.Page),ClassRegion=Region(c.ClassRegion),DayRegion=Region(c.DayRegion),
                PeriodRegions=c.PeriodRegions.ToDictionary(p=>p.Key,p=>Region(p.Value)!),
                RoleScopes=c.RoleScopes.Select(r=>r with {Box=Box(r.Box,r.Page),LabelRegion=Region(r.LabelRegion)!}).ToArray(),
                OrderedRowProof=c.OrderedRowProof is not {} proof ? null : proof with {Rows=proof.Rows.Select(row=>(IReadOnlyList<RecoveryOrderedRowPiece>)row.Select(p=>p with {Box=Box(p.Box,c.Page)}).ToArray()).ToArray()}
            }).ToArray(),
            CommonClockRegions=doc.CommonClockRegions.ToDictionary(p=>p.Key,p=>Region(p.Value)!),
            ClockBindings=doc.ClockBindings.ToDictionary(p=>p.Key,p=>Clock(p.Value)),
            ClockReplicas=doc.ClockReplicas.ToDictionary(p=>p.Key,p=>(IReadOnlyList<RecoveryClockBinding>)p.Value.Select(Clock).ToArray()),
            ReferenceNotes=doc.ReferenceNotes?.Select(n=>n with {Box=Box(n.Box,n.Page)}).ToArray()
        };
        if(projected.ReferenceNotes is not null)
        {
            var sources=projected.Sources.ToDictionary(s=>s.Id);
            projected=projected with {ReferenceNotes=projected.ReferenceNotes.Select(n=> {
                var boxes=n.SourceIds.Select(id=>sources[id].Box).ToArray();
                var x=boxes.Min(b=>b.X);var y=boxes.Min(b=>b.Y);var right=boxes.Max(b=>b.X+b.Width);var bottom=boxes.Max(b=>b.Y+b.Height);
                var width=right-x;var height=bottom-y;
                while(x+width<right)width=Math.BitIncrement(width);
                while(y+height<bottom)height=Math.BitIncrement(height);
                return n with {Box=new(x,y,width,height)};
            }).ToArray()};
        }
        return projected;
    }
}
