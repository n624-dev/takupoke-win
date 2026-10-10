using System.Xml.Linq;
using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Testing;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class ChangeRowReaderTests
{
    private static readonly XNamespace Ns = FictionalChangeWorkbook.Namespace;

    [Fact]
    public void IdenticalWeekdayTailGroupsPhysicalRowsButStillRequiresFullExplicitConsent()
    {
        var bytes = FictionalChangeWorkbook.WithWeekdayTail();
        var preview = XlsxChangeReader.ReadForPreview(bytes, 2032);
        var groups = ChangeReviewGroup.Create(preview.ReviewRows, preview.Warnings);
        Assert.Equal(3, groups.Count);
        Assert.Equal([3, 4], groups.Take(2).Select(group => group.Rows.Single().Row));
        Assert.Equal(Enumerable.Range(200, 101), groups[2].RowIds);
        Assert.Equal("200〜300行目", groups[2].RangeLabel);
        Assert.Equal("change-skip-group-200-300", groups[2].AutomationId);
        Assert.Equal(preview.ReviewRows, groups.SelectMany(group => group.Rows));
        Reject(ChangeErrorCode.WeekdayOnly, bytes, [3, 4]);
        Reject(ChangeErrorCode.WeekdayOnly, bytes, new[] { 3, 4 }.Concat(Enumerable.Range(200, 100)).ToArray());
        var changes = XlsxChangeReader.Parse(bytes, 2032,
            skippingRows: new[] { 3, 4 }.Concat(groups[2].RowIds).ToHashSet());
        Assert.Equal(["架空科目A", "架空科目C"], changes.Select(change => change.AfterSubject));
        Assert.Equal(["2032-07-10", "2032-07-12"], changes.Select(change => change.ChangeDate));
    }

    [Fact]
    public void GroupingPreservesGapsDifferentWeekdaysAndPopulatedRows()
    {
        ChangeReviewRow Row(int id, string weekday, string other = "") => new(id,
            [new("曜日", weekday), new("架空の他の欄", other)]);
        var rows = new[] { Row(10, "土"), Row(11, "土"), Row(13, "土"), Row(14, "日"),
            Row(15, "日", "架空値"), Row(16, "日"), Row(17, "日") };
        var warnings = rows.Select(row => new ChangeParseException(ChangeErrorCode.WeekdayOnly, row.Row)).ToArray();
        var groups = ChangeReviewGroup.Create(rows, warnings);
        Assert.Equal(5, groups.Count);
        Assert.Equal(rows, groups.SelectMany(group => group.Rows));
        Assert.Equal([10, 11], groups[0].RowIds);
        Assert.Equal([16, 17], groups[4].RowIds);
        Assert.Equal(rows.Length, ChangeReviewGroup.Create(rows, rows.Select(row =>
            new ChangeParseException(ChangeErrorCode.WeekdayMismatch, row.Row)).ToArray()).Count);
        Assert.Equal("200〜201行目、203行目、300行目", ChangeReviewGroup.DescribeRanges([300, 200, 201, 203, 203]));
        Assert.Equal("", ChangeReviewGroup.DescribeRanges([]));
        Assert.Equal(2, ChangeReviewGroup.Create([Row(int.MaxValue, "土"), Row(int.MinValue, "土")],
            [new(ChangeErrorCode.WeekdayOnly, int.MaxValue), new(ChangeErrorCode.WeekdayOnly, int.MinValue)]).Count);
    }

    [Fact]
    public void ExplicitSelectionPreservesOriginalFieldsAndExcludesWholeRows()
    {
        var bytes = FictionalChangeWorkbook.Create(sheet =>
        {
            Cell(sheet, "A3").Element(Ns + "is")!.Element(Ns + "t")!.Value = "1～2";
            Cell(sheet, "B3").Element(Ns + "is")!.Element(Ns + "t")!.Value = "ZZ,YY";
        });
        var preview = XlsxChangeReader.ReadForPreview(bytes, 2032);
        Assert.Equal([3, 4], preview.ReviewRows.Select(row => row.Row));
        Assert.Equal("学 年", preview.ReviewRows[0].Fields[0].Name);
        Assert.Equal("1～2", preview.ReviewRows[0].Fields[0].Value);
        Assert.Equal([ChangeErrorCode.WeekdayMismatch, ChangeErrorCode.WeekdayOnly],
            preview.Warnings.Select(warning => warning.Code));
        Reject(ChangeErrorCode.WeekdayOnly, bytes, [3]);
        Reject(ChangeErrorCode.WeekdayMismatch, bytes, [4]);
        Reject(ChangeErrorCode.Unsupported, bytes, [2, 3, 4]);
        Reject(ChangeErrorCode.Unsupported, bytes, [3, 4, 99]);
        var changes = XlsxChangeReader.Parse(bytes, 2032, skippingRows: new HashSet<int> { 3, 4 });
        Assert.Equal(["2032-07-10", "2032-07-12"], changes.Select(change => change.ChangeDate));
        Assert.Equal(["1", "3"], changes.Select(change => change.Period));
        Assert.Equal(["架空科目A", "架空科目C"], changes.Select(change => change.AfterSubject));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("火")]
    public void WeekdayOnlyFormulaIsInspectedWithoutEvaluationOrAutomaticSkipping(string? cache)
    {
        var bytes = FictionalChangeWorkbook.Create(sheet =>
        {
            var cell = Cell(sheet, "D4");
            cell.RemoveNodes(); cell.SetAttributeValue("t", "str");
            cell.Add(new XElement(Ns + "f", "TEXT(C4,\"aaa\")"));
            if (cache is not null) cell.Add(new XElement(Ns + "v", cache));
        });
        Assert.Equal(ChangeErrorCode.WeekdayOnly,
            XlsxChangeReader.ReadForPreview(bytes, 2032).Warnings[1].Code);
        Reject(ChangeErrorCode.WeekdayMismatch, bytes, []);
        Assert.Equal(2, XlsxChangeReader.Parse(bytes, 2032,
            skippingRows: new HashSet<int> { 3, 4 }).Count);
    }

    [Theory]
    [InlineData("C3", "2032/2/30", ChangeErrorCode.Date)]
    [InlineData("A3", "", ChangeErrorCode.Year)]
    [InlineData("B3", "", ChangeErrorCode.Classes)]
    public void ExcludedPopulatedRowsStillRequireValidDateYearAndClasses(string cell, string value,
        ChangeErrorCode code)
    {
        var bytes = FictionalChangeWorkbook.Create(sheet =>
            Cell(sheet, cell).Element(Ns + "is")!.Element(Ns + "t")!.Value = value);
        Reject(code, bytes, [3, 4]);
    }

    [Fact]
    public void ExclusionCannotHideStructureOrMissingCacheOrAcceptAnEmptyDocument()
    {
        var merged = FictionalChangeWorkbook.Create(sheet => sheet.Root!.Add(new XElement(Ns + "mergeCells",
            new XElement(Ns + "mergeCell", new XAttribute("ref", "A3:B3")))));
        Reject(ChangeErrorCode.MergedCells, merged, [3, 4]);
        var formula = FictionalChangeWorkbook.Create(sheet => Cell(sheet, "E3")
            .AddFirst(new XElement(Ns + "f", "1+1")));
        Reject(ChangeErrorCode.Formula, formula, [3, 4]);
        var extra = FictionalChangeWorkbook.Create(sheet => Cell(sheet, "D4").Parent!.Add(
            new XElement(Ns + "c", new XAttribute("r", "H4"), new XAttribute("t", "inlineStr"),
                new XElement(Ns + "is", new XElement(Ns + "t", "架空の見出し外値")))));
        Reject(ChangeErrorCode.Headers, extra, [3, 4]);
        var cache = FictionalChangeWorkbook.Create(sheet =>
        {
            var cell = Cell(sheet, "D3"); cell.RemoveNodes(); cell.SetAttributeValue("t", "str");
            cell.Add(new XElement(Ns + "f", "TEXT(C3,\"aaa\")"));
        });
        Reject(ChangeErrorCode.Unsupported, cache, [3, 4]);
        var empty = FictionalChangeWorkbook.Create(sheet =>
            sheet.Descendants(Ns + "row").Where(row => (string?)row.Attribute("r") is "2" or "5")
                .ToArray().ToList().ForEach(row => row.Remove()));
        Reject(ChangeErrorCode.Empty, empty, [3, 4]);
    }

    [Fact]
    public void ExcludedClassCannotSupplyTheRemainingAllRow()
    {
        var bytes = FictionalChangeWorkbook.Create(sheet =>
        {
            Cell(sheet, "B2").Element(Ns + "is")!.Element(Ns + "t")!.Value = "YY";
            Cell(sheet, "B5").Element(Ns + "is")!.Element(Ns + "t")!.Value = "全";
        });
        Assert.Equal(["1_YY", "1_YY"], XlsxChangeReader.Parse(bytes, 2032,
            skippingRows: new HashSet<int> { 3, 4 }).Select(change => change.ClassName));
    }

    private static XElement Cell(XDocument sheet, string reference) => sheet.Descendants(Ns + "c")
        .Single(cell => (string?)cell.Attribute("r") == reference);

    private static void Reject(ChangeErrorCode code, byte[] bytes, int[] rows) =>
        Assert.Equal(code, Assert.Throws<ChangeParseException>(() =>
            XlsxChangeReader.Parse(bytes, 2032, skippingRows: rows.ToHashSet())).Code);
}
