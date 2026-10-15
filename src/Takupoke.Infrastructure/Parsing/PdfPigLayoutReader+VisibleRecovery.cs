using Takupoke.Core;
using Takupoke.Core.Recovery;

namespace Takupoke.Infrastructure.Parsing;

public static partial class PdfPigLayoutReader
{
    // Ordinary import follows the template acceptance contract independently of
    // recovery completeness. It can also reuse an independently verified native drawing
    // without OCR. Any unresolved visible source keeps the recovery/review
    // path; this entry can never silently adopt a flagged glyph.
    internal static IReadOnlyList<PdfPageLayout> ReadForImport(byte[] bytes,MaterialKind kind,CancellationToken token=default)
    {
        try { return ReadCore(bytes, kind, token, null, null, ordinary:true); }
        catch(PdfParseException original) when(RecoveryPolicy.Eligible(kind,original.Stage))
        {
            var capture=new RecoveryReadCapture();
            try
            {
                var pages=ReadWithBoundedVisibleUnicode(bytes,kind,token,capture);
                if(capture.Complete && pages.All(p=>p.Glyphs.All(g=>!g.RequiresVisualReview))) return pages;
            }
            catch(PdfParseException failure) when(RecoveryPolicy.Eligible(kind,failure.Stage)) { }
            throw;
        }
    }
    internal static bool SafeMarkedProperties(UglyToad.PdfPig.Tokens.NameToken name,
        UglyToad.PdfPig.Tokens.NameToken? propertyName,UglyToad.PdfPig.Tokens.DictionaryToken? properties) =>
        name.Data is "Part" or "Artifact" && propertyName is null && properties is not null && properties.Data.Count==1 &&
        properties.Data.TryGetValue("MCID",out var value) && value is UglyToad.PdfPig.Tokens.NumericToken { Data: >= 0 and <= 1000000 } n && n.Data==Math.Truncate(n.Data);

    private static bool PaintedRulesOverlapInk(IReadOnlyList<PdfPathEngine.PaintedRule> rules,IReadOnlyList<PdfGlyph> glyphs,
        IReadOnlyDictionary<int,PdfBox> bareInk,IReadOnlyDictionary<int,int> textOperations,IReadOnlyDictionary<int,PdfPathEngine.PaintColor> textColours,CancellationToken token,
        ISet<int> requiresReview,
        ISet<int> hidden,
        out (PdfBox Rule,PdfBox Ink,int? Order)? conflict)
    {
        conflict=null;
        var ordered=rules.OrderBy(b=>b.Box.Left).ToArray();var active=new List<PdfPathEngine.PaintedRule>();var next=0;var work=0;
        foreach(var g in glyphs.OrderBy(g=>g.X))
        {
            token.ThrowIfCancellationRequested();
            if(g.SourceOrder is not int order || !textOperations.TryGetValue(order,out var drawnAt) || !bareInk.TryGetValue(order,out var bare))throw new PdfParseException("P01", branch:"R1058");
            while(next<ordered.Length && ordered[next].Box.Left<g.X+g.Width)active.Add(ordered[next++]);
            active.RemoveAll(b=>b.Box.Right<=g.X);
            PdfBox? intersecting=null;
            // A later opaque rectangle covering the whole painted support
            // removes that original glyph from the visible document. Never
            // expose covered text as a recovered field or a review candidate.
            foreach (var paint in active)
            {
                if (++work>1_000_000) throw new PdfParseException("limit", branch:"R1059");
                if(work%128==0)token.ThrowIfCancellationRequested();
                if (paint.BackgroundColor is not null && paint.Operation>drawnAt && paint.Box.Left<=g.X && paint.Box.Top<=g.Y &&
                    paint.Box.Right>=g.X+g.Width && paint.Box.Bottom>=g.Y+g.Height) { hidden.Add(order);break; }
            }
            if (hidden.Contains(order)) continue;
            foreach(var paint in active)
            {
                var b=paint.Box;
                if(++work>1_000_000)throw new PdfParseException("limit", branch:"R1060");
                if(work%128==0)token.ThrowIfCancellationRequested();
                if(b.Left<g.X+g.Width && g.X<b.Right && b.Top<g.Y+g.Height && g.Y<b.Bottom)
                {
                    // Keep wholly hidden/camouflaged glyphs and multiple
                    // competing paint supports refused. One partial later
                    // grid intersection requires original-pixel human review;
                    // encoded Unicode is never presented as an OCR score.
                    if (paint.BackgroundColor is {} colour)
                    {
                        if (paint.Operation<drawnAt && textColours[order].Contrast(colour)>=.1) continue;
                        conflict=(b,new(g.X,g.Y,g.X+g.Width,g.Y+g.Height),g.SourceOrder);return true;
                    }
                    if(intersecting is not null && intersecting!=b ||
                        b.Left<=bare.Left && b.Right>=bare.Right && b.Top<=bare.Top && b.Bottom>=bare.Bottom)
                    { conflict=(b,new(g.X,g.Y,g.X+g.Width,g.Y+g.Height),g.SourceOrder);return true; }
                    if(paint.Operation>=drawnAt) requiresReview.Add(order);
                    intersecting=b;
                }
            }
        }
        return false;
    }
    // Separate recovery entry. Strict keeps rejecting unsupported paint.
    // Only explicit Unicode plus an independent original native glyph trace
    // with bounded visible ink can authorize this additional acquisition.
    internal static IReadOnlyList<PdfPageLayout> ReadWithBoundedVisibleUnicode(byte[] bytes, MaterialKind kind,
        CancellationToken token, RecoveryReadCapture capture)
        => ReadCore(bytes, kind, token, capture, null, boundedVisibleUnicode:true);

    internal static void ImproveVisibleUnicodeCapture(byte[] bytes, MaterialKind kind, RecoveryReadCapture capture,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (capture.Complete || kind is not (MaterialKind.Timetable or MaterialKind.Exam or MaterialKind.ExamReturn)) return;
        var alternate = new RecoveryReadCapture();
        try { ReadWithBoundedVisibleUnicode(bytes,kind,token,alternate); }
        catch (PdfParseException failure) when (RecoveryPolicy.Eligible(kind,failure.Stage)) { }
        if (alternate.Pages.Count==0) return;
        if (capture.Pages.Count==0) capture.Begin(alternate.Pages.Count);
        if (capture.Pages.Count!=alternate.Pages.Count) throw new PdfParseException("P01", branch:"R1061");
        foreach (var page in alternate.Pages)
        {
            token.ThrowIfCancellationRequested();
            if (capture.Pages[page.Page-1].State!=RecoveryInputState.Complete && page.State==RecoveryInputState.Complete && page.Layout is not null)
                capture.Record(page.Page,page.State,page.Layout,page.NativeInkAccounted);
        }
        if (alternate.ReaderCompleted) capture.Finish();
    }
}
