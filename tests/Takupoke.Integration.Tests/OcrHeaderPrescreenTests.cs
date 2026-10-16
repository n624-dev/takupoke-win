using System.Collections;
using System.Reflection;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;
namespace Takupoke.Integration.Tests;

public sealed class OcrHeaderPrescreenTests
{
    private static readonly string[] Syntax = ["月曜日", "火曜", "金", "2034年9月12日", "9月12日", "2034/9/12", "9-12", "1-1", "5_ES", "AI_2", "2年",
        "２０３４年９月１２日", " ＡＩ＿１ ", "前期", "後期", "令和16年度", "2034年度", "試験返却時間割", "定期試験時間割", "通常時間割", "1時限目",
        "1・2時限連続", "7〜8時限連続", "8:30~9:20", "8:30〜9:20", "8:30～9:20", "科目：架空", "教員:仮想", "教室：虚構", "架空備考8:30~9:20のみ", "注意令和16年度のみ"];
    private static PdfPageLayout Fixture(int bodyRows = 2000)
    {
        var glyphs = new List<PdfGlyph>(); int order = 0, line = 0;
        foreach (var text in Syntax)
        {
            for (int i = 0; i < text.Length; i++) glyphs.Add(new(text[i].ToString(), 180 + i * 5, 12 + line * 6, 4, 3, line, order++));
            line++;
        }
        // Body rows are generated independently, with stable IDs/order and no
        // dates/classes/roles/header syntax. Their atoms remain foreign inputs.
        for (int row = 0; row < bodyRows; row++, line++)
            foreach (var (text, i) in new[] { "架", "空", "値", "甲" }.Select((t, i) => (t, i)))
                glyphs.Add(new(text, 20 + i * 5, 250 + row * 2.2, 4, 1.5, line, order++));
        var rules = Enumerable.Range(0, 67).Select(i => new PdfRule(450, 10 + i * 50, 490, 10 + i * 50)).ToArray();
        return new(500, 4800, glyphs, rules);
    }
    private static (string[] Labels, long Work) Labels(PdfPageLayout page, bool baseline = false)
    {
        if (baseline) return OcrHeaderGoldenSnapshots.For(page);
        var assembly = typeof(RecoveryDocumentBuilder).Assembly;
        var builder = assembly.GetType("Takupoke.Infrastructure.Recovery.RecoveryDocumentBuilder")!;
        var pageType = assembly.GetType("Takupoke.Infrastructure.Parsing.PdfPageLayout")!;
        object input = page;
        var glyphs = ((IEnumerable)pageType.GetProperty("Glyphs")!.GetValue(input)!).Cast<object>().ToArray();
        var atomType = builder.GetNestedType("Atom", BindingFlags.NonPublic)!;
        var atomList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(atomType))!;
        for (int i = 0; i < glyphs.Length; i++) atomList.Add(Activator.CreateInstance(atomType, $"p1s{i}", 1, glyphs[i])!);
        var pageList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pageType))!; pageList.Add(input);
        var workType = builder.GetNestedType("Work", BindingFlags.NonPublic)!;
        var work = Activator.CreateInstance(workType, CancellationToken.None)!;
        var method = builder.GetMethod("OcrLineLabels", BindingFlags.Static | BindingFlags.NonPublic)!;
        var labels = ((IEnumerable)method.Invoke(null, [atomList, pageList, new HashSet<int> { 1 }, work])!).Cast<object>().Select(label =>
        {
            var t = label.GetType(); var ids = ((IEnumerable)t.GetProperty("Ids")!.GetValue(label)!).Cast<string>();
            return t.GetProperty("Value")!.GetValue(label) + "|" + string.Join(',', ids) + "|"
                + JsonSerializer.Serialize(t.GetProperty("Box")!.GetValue(label)) + "|" + t.GetProperty("WholeOcrRow")!.GetValue(label);
        }).ToArray();
        return (labels, (long)workType.GetField("_comparisons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(work)!);
    }
    [Fact]
    public void FullSizedIndependentRowsKeepExactLabelEvidenceAndReduceMeasuredWork()
    {
        var page = Fixture(); var before = Labels(page, baseline: true); var after = Labels(page);
        Assert.NotEmpty(before.Labels); Assert.Equal(before.Labels, after.Labels);
        Assert.True(before.Work > 16_000_000); Assert.True(after.Work < 500_000);
        Assert.True(after.Work < before.Work / 20);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "deterministic-work-count.json"),
            JsonSerializer.Serialize(new { rows = 2000 + Syntax.Length, atoms = page.Glyphs.Count, rules = page.Lines.Count,
                before = before.Work, after = after.Work, exactOutputEquality = true }));
    }
    [Theory]
    [InlineData("foreignAtom")]
    [InlineData("foreignUnselectedMalformedAtom")]
    [InlineData("crossingRule")]
    [InlineData("orderGap")]
    [InlineData("partialAtomMatch")]
    [InlineData("malformedGlobalRule")]
    public void AdversarialHeadersHaveExactlyTheOldOutput(string mutation)
    {
        var page = Fixture(7); var glyphs = page.Glyphs.ToList(); var rules = page.Lines.ToList();
        if (mutation is "foreignAtom" or "foreignUnselectedMalformedAtom")
            glyphs.Add(new("偽", 182, 12, 5, 3, 999, mutation == "foreignAtom" ? 999 : -1));
        if (mutation == "crossingRule") rules.Add(new(183, 11, 183, 17));
        if (mutation == "orderGap") glyphs[1] = glyphs[1] with { SourceOrder = 999 };
        if (mutation == "partialAtomMatch") glyphs.Add(new("注記令和16年度末尾", 200, 200, 60, 3, 999, 999));
        if (mutation == "malformedGlobalRule") rules.Add(new(1, 1, 2, 3));
        page = page with { Glyphs = glyphs, Lines = rules };
        var before = Labels(page, baseline: true); var after = Labels(page);
        Assert.Equal(before.Labels, after.Labels);
        if (mutation is "foreignAtom" or "foreignUnselectedMalformedAtom" or "crossingRule" or "orderGap")
            Assert.DoesNotContain(after.Labels, s => s.StartsWith("月曜日|", StringComparison.Ordinal));
        if (mutation == "malformedGlobalRule") Assert.Empty(after.Labels);
    }
    [Fact]
    public void ExcludedUnknownSourceStillExistsAndFailsGlobalClassification()
    {
        PdfGlyph[] glyphs = [new("令和16年度", 20, 20, 90, 12), new("前期", 140, 20, 24, 12), new("時間割", 200, 20, 36, 12),
            new("月曜日", 180, 94, 36, 12, 0, 0), new("1-1", 30, 230, 18, 12), new("1", 150, 150, 6, 12),
            new("科目：", 140, 210, 36, 12), new("架空科", 200, 210, 36, 12), new("教員：", 140, 240, 36, 12), new("架空師", 200, 240, 36, 12),
            new("教室：", 140, 270, 36, 12), new("架空室", 200, 270, 36, 12), new("分類不能", 700, 340, 36, 12, 88, -1)];
        PdfRule[] rules = [new(20, 88, 420, 88), new(20, 122, 420, 122), new(20, 194, 420, 194), new(20, 290, 420, 290),
            new(20, 88, 20, 290), new(120, 88, 120, 290), new(420, 88, 420, 290)];
        var page = new PdfPageLayout(900, 600, glyphs, rules);
        var doc = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page],
            (_, box) => !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))), new HashSet<int> { 1 });
        Assert.Equal(glyphs.Length, doc.Sources.Count);
        Assert.Contains(doc.Sources, s => s.Text == "分類不能" && s.SourceOrder == -1);
        Assert.Contains("unclassifiedSource", RecoveryValidator.InputErrors(doc));
    }
    [Fact]
    public void ComparisonBudgetRemainsTwentyMillionAndCancelable()
    {
        var workType = typeof(RecoveryDocumentBuilder).GetNestedType("Work", BindingFlags.NonPublic)!;
        var work = Activator.CreateInstance(workType, CancellationToken.None)!;
        var step = workType.GetMethod("Step")!;
        step.Invoke(work, [20_000_000L]);
        var limit = Assert.IsType<InvalidDataException>(Assert.Throws<TargetInvocationException>(() => step.Invoke(work, [1L])).InnerException);
        Assert.True(limit.Data.Contains("RecoveryWorkLimitExceeded"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var canceled = Activator.CreateInstance(workType, cancellation.Token)!;
        Assert.IsType<OperationCanceledException>(Assert.Throws<TargetInvocationException>(() => step.Invoke(canceled, [1L])).InnerException);
    }
}
