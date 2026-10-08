using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.PdfFonts;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace Takupoke.Infrastructure.Parsing;

// Observe original drawing events, not PdfPig's fallback Unicode string. This
// does not certify visibility: the existing bounded interpreter still does so.
internal sealed record PdfNativeGlyphTrace(string Resource, int Code, string Text, bool UnicodeKnown,
    PdfPoint Origin, PdfPoint Horizontal, PdfPoint Vertical, bool Drawable = true, PdfRectangle? InkBounds = null);

internal sealed record PdfNativeTraceInputs(PdfNativeTraceFactory Factory, CropBox Crop, UserSpaceUnit Unit,
    PageRotationDegrees Rotation, TransformationMatrix Initial, IReadOnlyList<IGraphicsStateOperation> Operations);

internal sealed class PdfNativeTraceFactory(IPdfTokenScanner scanner, IResourceStore resources,
    ILookupFilterProvider filters, IPageContentParser parser, ParsingOptions options)
    : BasePageFactory<PdfNativeTraceInputs>(scanner, resources, filters, parser, options)
{
    protected override PdfNativeTraceInputs ProcessPage(int number, DictionaryToken dictionary, NamedDestinations destinations,
        MediaBox media, CropBox crop, UserSpaceUnit unit, PageRotationDegrees rotation,
        TransformationMatrix initial, IReadOnlyList<IGraphicsStateOperation> operations)
        => new(this, crop, unit, rotation, initial, operations);
}

internal sealed class PdfNativeTraceProcessor(int number, PdfNativeTraceInputs input, CancellationToken token, IReadOnlySet<string> recoveryResources)
    : BaseStreamProcessor<IReadOnlyList<PdfNativeGlyphTrace>>(number, input.Factory.ResourceStore,
        input.Factory.PdfScanner, input.Factory.PageContentParser, input.Factory.FilterProvider,
        input.Crop, input.Unit, input.Rotation, input.Initial, input.Factory.ParsingOptions)
{
    private readonly List<PdfNativeGlyphTrace> _glyphs = [];
    private readonly Dictionary<(IFont Font,int Code),PdfRectangle?> _outlines = [];
    private int _outlineWork;
    public override IReadOnlyList<PdfNativeGlyphTrace> Process(int pageNumber, IReadOnlyList<IGraphicsStateOperation> operations)
    {
        if (operations.Count > 1_000_000) throw new PdfParseException("limit");
        CloneAllStates();
        // Keep native state between operations while checking cancellation.
        foreach (var operation in operations) { token.ThrowIfCancellationRequested(); ProcessOperations([operation]); }
        return _glyphs;
    }
    public override void RenderGlyph(IFont font, CurrentGraphicsState state, double fontSize, double pointSize,
        int code, string unicode, long offset, in TransformationMatrix rendering,
        in TransformationMatrix text, in TransformationMatrix transform, CharacterBoundingBox bounds)
    {
        token.ThrowIfCancellationRequested();
        if (_glyphs.Count >= 100000) throw new PdfParseException("limit");
        var resource = state.FontState.FontName;
        if (state.FontState.FromExtendedGraphicsState || resource is null || font.IsVertical
            || !ReferenceEquals(font, ResourceStore.GetFont(resource))) throw new PdfParseException("P01");
        var matrix = rendering.Multiply(text).Multiply(transform);
        var known = font.TryGetUnicode(code, out var original);
        PdfRectangle? outline=null;
        if(recoveryResources.Contains(resource.Data) && !_outlines.TryGetValue((font,code),out outline))
        {
            if(font.TryGetNormalisedPath(code,out var path)) {
                foreach(var subpath in path) {
                    token.ThrowIfCancellationRequested();
                    if(subpath.Commands.Count>1_000_000-_outlineWork)throw new PdfParseException("limit");
                    _outlineWork+=subpath.Commands.Count;
                }
                outline=PdfSubpath.GetBoundingRectangle(path);
            }
            _outlines.Add((font,code),outline);
        }
        var drawable=!recoveryResources.Contains(resource.Data) || outline is { Width: > 0,Height: > 0 };
        _glyphs.Add(new(resource.Data, code, known ? original! : unicode, known,
            matrix.Transform(new PdfPoint(0, 0)), matrix.Transform(new PdfPoint(1, 0)), matrix.Transform(new PdfPoint(0, 1)),drawable,outline is {} box?matrix.Transform(box):null));
    }
    // Geometry/paint is checked by PdfPathEngine and VisibilityState before this
    // observer runs. Unsupported secondary content still refuses explicitly.
    protected override void RenderXObjectImage(XObjectContentRecord image) => throw new PdfParseException("P01");
    protected override void RenderInlineImage(InlineImage image) => throw new PdfParseException("P01");
    public override void BeginMarkedContent(NameToken name, NameToken? propertyName, DictionaryToken? properties) => throw new PdfParseException("P01");
    public override void EndMarkedContent() => throw new PdfParseException("P01");
    public override void ModifyClippingIntersect(FillingRule rule) => throw new PdfParseException("P01");
    protected override void ClipToRectangle(PdfRectangle rectangle, FillingRule rule) => throw new PdfParseException("P01");
    public override void PaintShading(NameToken name) => throw new PdfParseException("P01");
    public override void BeginSubpath() { }
    public override PdfPoint? CloseSubpath() => null;
    public override void StrokePath(bool close) { }
    public override void FillPath(FillingRule rule, bool close) { }
    public override void FillStrokePath(FillingRule rule, bool close) { }
    public override void MoveTo(double x, double y) { }
    public override void BezierCurveTo(double x1, double y1, double x2, double y2, double x3, double y3) { }
    public override void BezierCurveTo(double x2, double y2, double x3, double y3) { }
    public override void LineTo(double x, double y) { }
    public override void Rectangle(double x, double y, double width, double height) { }
    public override void EndPath() { }
    public override void ClosePath() { }
}
