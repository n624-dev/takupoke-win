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
    [Theory]
    [InlineData("inline")]
    [InlineData("shared")]
    [InlineData("rich")]
    public void WorkbookKindIsIndependentOfRemarksAndEscapesDecodeOnlyOnce(string form)
    {
        foreach (var kind in new[] { "休講", "補講", "変更", "" })
        foreach (var note in new[] { "", "架空備考A", "休講" })
        {
            var row = Assert.Single(XlsxChangeReader.Parse(ContentWorkbook(kind, note, form), 2032));
            Assert.Equal(kind, row.ChangeKind);
            Assert.Equal(note.Length == 0 ? kind : note, row.Note);
            Assert.Equal(kind == "休講", row.IsCancellation);
            Assert.Equal(kind == "補講", row.IsMakeup);
            Assert.Equal(kind is "休講" or "補講" ? kind : "変更", row.KindLabel);
            var engine = new TimetableEngine(new(Changes: [row]));
            var block = Assert.Single(engine.Blocks(new(2032, 4, 5), "1_ZZ"));
            Assert.Equal(kind != "休講", engine.IsInProgress(new(2032, 4, 5), "1_ZZ", block, DateTimeOffset.Parse("2032-04-05T09:00:00+09:00")));
        }
        foreach (var (encoded, expected) in new[] { ("休講_x000D_", "休講"), ("補講_x000A_", "補講"), ("休講_x000D__x000A_", "休講") })
        {
            var row = Assert.Single(XlsxChangeReader.Parse(ContentWorkbook(encoded, "架空備考A", form,
                subject: "架空科目_x0041_", teacher: "架空教員_x0042_", room: "架空室_x005F_x0043_"), 2032));
            Assert.Equal(expected, row.KindLabel);
            Assert.Equal("架空科目A", row.IsCancellation ? row.BeforeSubject : row.AfterSubject);
            Assert.Equal("架空教員B", row.Teacher);
            Assert.Equal("架空室_x0043_", row.Room);
        }
        foreach (var literal in new[] { "_x000Z_", "_X000D_" })
            Assert.Equal(literal, Assert.Single(XlsxChangeReader.Parse(ContentWorkbook("変更", "", form, subject: literal), 2032)).AfterSubject);
        Assert.Equal("😀", Assert.Single(XlsxChangeReader.Parse(ContentWorkbook("変更", "", form, subject: "_xD83D__xDE00_"), 2032)).AfterSubject);
    }

    [Fact]
    public void WorkbookMakeupWithRemarksKeepsPriorityOverFollowingCancellation()
    {
        var rows = XlsxChangeReader.Parse(ContentWorkbook("補講", "架空備考A", edit: sheet =>
        {
            XNamespace ns = Ns;
            var second = new XElement(sheet.Descendants(ns + "row").Last());
            second.SetAttributeValue("r", 3);
            foreach (var cell in second.Elements(ns + "c")) cell.SetAttributeValue("r", ((string)cell.Attribute("r")!)[..^1] + "3");
            second.Descendants(ns + "t").ElementAt(4).Value = "休講";
            second.Descendants(ns + "t").ElementAt(5).Value = "架空休講科目B";
            sheet.Descendants(ns + "sheetData").Single().Add(second);
        }), 2032);
        Assert.Equal("架空科目A", TimetableEngine.EffectiveChange(rows)!.AfterSubject);
    }

    internal static byte[] ContentWorkbook(string kind, string note, string form = "inline",
        string subject = "架空科目A", string teacher = "架空教員A", string room = "架空教室A", Action<XDocument>? edit = null) => Workbook(mutate: entries =>
    {
        XNamespace ns = Ns;
        var values = new[] { new[] { "学 年", "学科・クラス", "月日", "時限", "変更内容", "科目", "教員", "教室", "備考" },
            new[] { "1", "ZZ", "2032/4/5", "1", kind, subject, teacher, room, note } };
        var strings = new List<string>();
        XElement Cell(int row, int col, string value)
        {
            var reference = $"{(char)('A' + col)}{row}";
            if (form == "shared")
            {
                strings.Add(value);
                return new(ns + "c", new XAttribute("r", reference), new XAttribute("t", "s"), new XElement(ns + "v", strings.Count - 1));
            }
            var text = new XElement(ns + "t", value);
            return new(ns + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"),
                new XElement(ns + "is", form == "rich" ? new XElement(ns + "r", text) : text));
        }
        var sheet = new XDocument(new XElement(ns + "worksheet", new XElement(ns + "sheetData",
            values.Select((cells, row) => new XElement(ns + "row", new XAttribute("r", row + 1), cells.Select((v, col) => Cell(row + 1, col, v)))))));
        edit?.Invoke(sheet);
        entries["xl/worksheets/sheet1.xml"] = sheet.ToString();
        if (form == "shared")
        {
            entries["xl/sharedStrings.xml"] = new XElement(ns + "sst", strings.Select(v => new XElement(ns + "si", new XElement(ns + "t", v)))).ToString();
            var types = XDocument.Parse(entries["[Content_Types].xml"]);
            types.Root!.Add(new XElement(types.Root.Name.Namespace + "Override", new XAttribute("PartName", "/xl/sharedStrings.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml")));
            entries["[Content_Types].xml"] = types.ToString();
            var rels = XDocument.Parse(entries["xl/_rels/workbook.xml.rels"]);
            rels.Root!.Add(new XElement(rels.Root.Name.Namespace + "Relationship", new XAttribute("Id", "shared"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings"), new XAttribute("Target", "sharedStrings.xml")));
            entries["xl/_rels/workbook.xml.rels"] = rels.ToString();
        }
    });

}
