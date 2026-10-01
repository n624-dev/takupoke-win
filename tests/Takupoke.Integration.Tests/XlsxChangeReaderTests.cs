using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class XlsxChangeReaderTests
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    [Fact]
    public void ReadsRealOoxmlStructureAndKeepsSourceText()
    {
        var change = Assert.Single(XlsxChangeReader.Parse(Workbook(), 2032));
        Assert.Equal("2032-04-05", change.ChangeDate);
        Assert.Equal("1_CN", change.ClassName);
        Assert.Equal("架空科目B", change.AfterSubject);
        Assert.Contains("変更後:架空科目B", change.RawText);
    }
    [Fact]
    public void ValidWeekdayFormulaUsesSavedValueWithoutEvaluation()
    {
        Assert.Single(XlsxChangeReader.Parse(Workbook(formula: true, cache: "月"), 2032));
    }
    [Theory]
    [InlineData(null, ChangeErrorCode.FormulaCache)]
    [InlineData("火", ChangeErrorCode.WeekdayMismatch)]
    public void FormulaWarningsAreAvailableForPreviewButCannotBeAccepted(string? cache, ChangeErrorCode expected)
    {
        var bytes = Workbook(formula: true, cache: cache);
        Assert.Equal(expected, Assert.Single(XlsxChangeReader.ReadForPreview(bytes, 2032).Warnings).Code);
        Assert.Equal(expected, Assert.Throws<ChangeParseException>(() => XlsxChangeReader.Parse(bytes, 2032)).Code);
    }
    [Fact]
    public void RejectsFormulaInSubjectColumn()
    {
        var bytes = Workbook(mutate: entries =>
        {
            var sheet = XDocument.Parse(entries["xl/worksheets/sheet1.xml"]);
            XNamespace ns = Ns;
            sheet.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "F2").AddFirst(new XElement(ns + "f", "\"架空科目B\""));
            entries["xl/worksheets/sheet1.xml"] = sheet.ToString();
        });
        Assert.Equal(ChangeErrorCode.Formula, Assert.Throws<ChangeParseException>(() => XlsxChangeReader.Parse(bytes, 2032)).Code);
    }
    [Theory]
    [InlineData("macro")]
    [InlineData("external")]
    [InlineData("1904")]
    [InlineData("merged")]
    [InlineData("duplicateCell")]
    [InlineData("duplicateValue")]
    [InlineData("path")]
    [InlineData("doctype")]
    public void RejectsUnsupportedOrAmbiguousStructures(string kind)
    {
        var bytes = Workbook(mutate: entries =>
        {
            XNamespace ns = Ns;
            var workbook = XDocument.Parse(entries["xl/workbook.xml"]);
            var sheet = XDocument.Parse(entries["xl/worksheets/sheet1.xml"]);
            switch (kind)
            {
                case "macro": entries["xl/vbaProject.bin"] = "fake-macro"; break;
                case "external": workbook.Root!.Add(new XElement(ns + "externalReferences")); break;
                case "1904": workbook.Root!.AddFirst(new XElement(ns + "workbookPr", new XAttribute("date1904", "1"))); break;
                case "merged": sheet.Root!.Add(new XElement(ns + "mergeCells", new XElement(ns + "mergeCell", new XAttribute("ref", "A2:B2")))); break;
                case "duplicateCell": sheet.Descendants(ns + "row").Last().Add(new XElement(sheet.Descendants(ns + "c").Last())); break;
                case "duplicateValue": sheet.Descendants(ns + "c").First().Add(new XElement(ns + "v", "fake"), new XElement(ns + "v", "fake")); break;
                case "path": entries["../fake.xml"] = "fake"; break;
                case "doctype": entries["xl/workbook.xml"] = "<!DOCTYPE workbook [<!ENTITY fake 'fake'>]>" + workbook; return;
            }
            entries["xl/workbook.xml"] = workbook.ToString();
            entries["xl/worksheets/sheet1.xml"] = sheet.ToString();
        });
        Assert.Throws<ChangeParseException>(() => XlsxChangeReader.Parse(bytes, 2032));
    }
    [Fact]
    public void CancellationStopsReading()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => XlsxChangeReader.Parse(Workbook(), 2032, cancellation.Token));
    }
    [Fact]
    public void ZipBoundsApplyBeforeReadingDocumentParts()
    {
        var bytes = Zip(new() { ["fake"] = new string('A', 1024) });
        Assert.Throws<InvalidDataException>(() => new BoundedZip(bytes, maximumEntryBytes: 512));
        using var zip = new BoundedZip(bytes);
        Assert.Throws<InvalidDataException>(() => zip.Read("fake", 512));
    }
    internal static byte[] Workbook(bool formula = false, string? cache = null, Action<Dictionary<string, string>>? mutate = null)
    {
        XNamespace ns = Ns;
        XElement Cell(string reference, string value) => new(ns + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"),
            new XElement(ns + "is", new XElement(ns + "t", value)));
        var header = new[] { "学 年", "学科・クラス", "月日", "時限", "変更前", "変更後", "曜日" };
        var values = new[] { "1", "CN", "4/5", "1", "架空科目A", "架空科目B", "月" };
        var row = new XElement(ns + "row", new XAttribute("r", "2"), values.Select((v, i) => Cell($"{(char)('A' + i)}2", v)));
        if (formula) row.Elements().Last().ReplaceWith(new XElement(ns + "c", new XAttribute("r", "G2"), new XAttribute("t", "str"),
            new XElement(ns + "f", "TEXT(C2,\"aaa\")"), cache is null ? null : new XElement(ns + "v", cache)));
        var worksheet = new XElement(ns + "worksheet", new XElement(ns + "sheetData",
            new XElement(ns + "row", new XAttribute("r", "1"), header.Select((v, i) => Cell($"{(char)('A' + i)}1", v))), row));
        var entries = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>",
            ["_rels/.rels"] = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
            ["xl/workbook.xml"] = $"<workbook xmlns=\"{Ns}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"時間割変更\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>",
            ["xl/worksheets/sheet1.xml"] = worksheet.ToString()
        };
        mutate?.Invoke(entries);
        return Zip(entries);
    }
    private static byte[] Zip(Dictionary<string, string> entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var pair in entries)
            { using var writer = new StreamWriter(zip.CreateEntry(pair.Key).Open(), new UTF8Encoding(false)); writer.Write(pair.Value); }
        return stream.ToArray();
    }
}
