using System.Reflection;
using System.Collections;
using Takupoke.Infrastructure.Parsing;
using Xunit;
namespace Takupoke.Integration.Tests;

// Independently generated fictional geometry; no retained OCR/corpus inputs.
public sealed class ExamCompositeOcrHeaderTests
{
    private static (PdfPageLayout Page, PdfGlyph[] Periods) Fixture(string first = "1年AI_1", string second = "2年AI_2")
    {
        var glyphs = new List<PdfGlyph>(); var periods = new List<PdfGlyph>(); var rules = new List<PdfRule>();
        int order = 0, line = 0;
        for (int column = 0; column < 2; column++)
        {
            double left = 100 + column * 360, right = left + 360;
            var text = column == 0 ? first : second;
            for (int i = 0; i < text.Length; i++)
                glyphs.Add(new(text[i].ToString(), left + 18 + i * 12 + (i > 1 ? 160 : 0), 68, 8, 14, line, order++));
            line++;
            rules.Add(new(left, 58, right, 58)); rules.Add(new(left, 92, right, 92));
            rules.Add(new(left, 58, left, 120)); rules.Add(new(right, 58, right, 120));
            rules.Add(new(left, 120, right, 120));
            for (int period = 0; period < 6; period++)
            {
                var glyph = new PdfGlyph((period + 1).ToString(), left + period * 60 + 25, 100, 8, 12, line++, order++);
                glyphs.Add(glyph); periods.Add(glyph);
                if (period > 0) rules.Add(new(left + period * 60, 92, left + period * 60, 120));
            }
        }
        return (new(900, 600, glyphs, rules.Distinct().ToArray()), periods.ToArray());
    }
    private static string[] Classes(PdfPageLayout page, PdfGlyph[] periods, bool ocr = true)
    {
        var method = typeof(PdfScheduleParser).GetMethod("ExamClasses", BindingFlags.Static | BindingFlags.NonPublic);
        if (method is null)
        {
            // Baseline RED invokes the unchanged production Runs boundary and
            // applies the exact pre-change ExamPage grade-selection expression.
            var headerY = periods[0].Cy;
            var candidates = page.Glyphs.Where(g => g.Cy < headerY - 3 && g.Cy > headerY - page.Height / 10).ToArray();
            var runsMethod = typeof(PdfScheduleParser).GetMethod("Runs", BindingFlags.Static | BindingFlags.NonPublic)!;
            var runs = ((IEnumerable)runsMethod.Invoke(null, [page, candidates, ocr])!).Cast<object>();
            var labels = runs.Select(r => (Text: (string)r.GetType().GetProperty("Text")!.GetValue(r)!,
                    Cx: (double)r.GetType().GetProperty("Cx")!.GetValue(r)!))
                .Where(r => System.Text.RegularExpressions.Regex.IsMatch(r.Text, "^[12]年$")).OrderBy(r => r.Cx).ToArray();
            if (labels.Length != 2) throw new PdfParseException("P14", 6);
            return labels.Select(r => "AI_" + r.Text[..1]).ToArray();
        }
        try { return (string[])method.Invoke(null, [page, periods, 6, ocr, CancellationToken.None])!; }
        catch (TargetInvocationException e) when (e.InnerException is PdfParseException) { throw e.InnerException; }
    }
    [Fact]
    public void CompleteConsistentHeadersOwnTheEntirePhysicalSixPeriodBand()
    {
        var (page, periods) = Fixture(); var snapshot = page.Glyphs.ToArray();
        Assert.Equal(new[] { "AI_1", "AI_2" }, Classes(page, periods));
        Assert.Equal(snapshot, page.Glyphs); // Raw text and original geometry untouched.
    }
    [Theory]
    [InlineData("1年AI_2", "2年AI_2")]
    [InlineData("1年AI_1", "1年AI_1")]
    [InlineData("1年AI_3", "2年AI_2")]
    [InlineData("1年AI_1x", "2年AI_2")]
    public void MismatchedDuplicateOrIncompleteCompositeRefuses(string first, string second)
    {
        var (page, periods) = Fixture(first, second);
        Assert.Equal("P14", Assert.Throws<PdfParseException>(() => Classes(page, periods)).Stage);
    }
    [Theory]
    [InlineData("crossingRule")]
    [InlineData("foreignOverlap")]
    [InlineData("orderGap")]
    [InlineData("differentY")]
    [InlineData("differentSourceLine")]
    [InlineData("missingCanonicalMetadata")]
    [InlineData("allCanonicalMetadataMissing")]
    [InlineData("allCanonicalOtherRow")]
    [InlineData("allCanonicalDifferentRows")]
    [InlineData("bothCanonicalMetadataMissing")]
    [InlineData("bothCanonicalOtherRows")]
    [InlineData("bothCanonicalDifferentRows")]
    [InlineData("missingSide")]
    [InlineData("bandGap")]
    [InlineData("wrongBandOwner")]
    [InlineData("periodForeignOverlap")]
    [InlineData("periodOrderGap")]
    [InlineData("duplicatePrefix")]
    public void TextCannotOverrideSourceOrPhysicalOwnership(string mutation)
    {
        var (page, periods) = Fixture(); var glyphs = page.Glyphs.ToList(); var rules = page.Lines.ToList();
        if (mutation == "crossingRule") rules.Add(new(200, 58, 200, 92));
        if (mutation == "foreignOverlap") glyphs.Add(new("偽", 280, 68, 10, 14, 999, 999));
        if (mutation == "orderGap") glyphs[2] = glyphs[2] with { SourceOrder = 999 };
        if (mutation == "differentY") glyphs[2] = glyphs[2] with { Y = 45 };
        if (mutation == "differentSourceLine") glyphs[2] = glyphs[2] with { SourceLine = 999 };
        if (mutation == "missingCanonicalMetadata") glyphs[2] = glyphs[2] with { SourceLine = null, SourceOrder = null };
        if (mutation == "allCanonicalMetadataMissing")
            for (int i = 2; i < 6; i++) glyphs[i] = glyphs[i] with { SourceLine = null, SourceOrder = null };
        if (mutation == "allCanonicalOtherRow")
            for (int i = 2; i < 6; i++) glyphs[i] = glyphs[i] with { SourceLine = 998 };
        if (mutation == "allCanonicalDifferentRows")
            for (int i = 2; i < 6; i++) glyphs[i] = glyphs[i] with { SourceLine = 990 + i };
        if (mutation.StartsWith("bothCanonical", StringComparison.Ordinal))
            foreach (int i in new[] { 2, 3, 4, 5, 14, 15, 16, 17 })
                glyphs[i] = glyphs[i] with
                {
                    SourceLine = mutation == "bothCanonicalMetadataMissing" ? null : mutation == "bothCanonicalOtherRows" ? 990 + i / 12 : 990 + i,
                    SourceOrder = mutation == "bothCanonicalMetadataMissing" ? null : glyphs[i].SourceOrder
                };
        if (mutation == "missingSide") rules.RemoveAll(r => r.Vertical && r.X1 == 100);
        if (mutation == "bandGap") rules.RemoveAll(r => r.Vertical && r.X1 == 160);
        if (mutation == "wrongBandOwner") rules.Add(new(100, 90, 460, 90));
        if (mutation == "periodForeignOverlap") glyphs.Add(periods[0] with { Text = "偽", SourceLine = 999, SourceOrder = 999 });
        if (mutation == "periodOrderGap") { var i = glyphs.IndexOf(periods[0]); glyphs[i] = periods[0] = periods[0] with { SourceOrder = -1 }; }
        if (mutation == "duplicatePrefix") glyphs.AddRange(new[] { new PdfGlyph("1", 150, 84, 8, 5, 998, 998), new PdfGlyph("年", 158, 84, 8, 5, 998, 999) });
        page = page with { Glyphs = glyphs, Lines = rules };
        Assert.Equal("P14", Assert.Throws<PdfParseException>(() => Classes(page, periods)).Stage);
    }
    [Fact]
    public void VectorLegacyGradeHeadersRetainGapAndPageMapping()
    {
        var (page, periods) = Fixture("1年", "2年");
        page = page with { Glyphs = page.Glyphs.Select(g => g with { SourceLine = null, SourceOrder = null }).ToArray() };
        Assert.Equal(new[] { "AI_1", "AI_2" }, Classes(page, periods, ocr: false));
    }
    [Fact]
    public void OcrLegacyGradeOnlyRemainsSupportedWhenWholeOwnerHasNoSuffixInk()
    {
        var (page, periods) = Fixture("1年", "2年");
        Assert.Equal(new[] { "AI_1", "AI_2" }, Classes(page, periods));
    }
    [Fact]
    public void VectorCompositeDoesNotEnableNewGrammar()
    {
        var (page, periods) = Fixture();
        // Old vector spacing can expose the two grade prefixes; the new proof
        // is strictly disabled and preserves that legacy behavior.
        Assert.Equal(new[] { "AI_1", "AI_2" }, Classes(page, periods, ocr: false));
    }
}
