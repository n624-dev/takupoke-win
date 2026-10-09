using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Takupoke.Testing;

// Created independently for these tests; no school file or copied school fields.
public static class FictionalChangeWorkbook
{
    public static readonly XNamespace Namespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static byte[] Create(Action<XDocument>? edit = null)
    {
        var values = new[]
        {
            new[] { "学 年", "学科・クラス", "月日", "曜日", "時限", "変更内容", "科目(担当教員)" },
            new[] { "1", "ZZ", "2032/7/10", "土", "1", "補講", "架空科目A" },
            new[] { "1", "ZZ", "2032/7/11", "月", "2", "補講", "架空除外科目B" },
            new[] { "", "", "", "火" },
            new[] { "1", "ZZ", "2032/7/12", "月", "3", "補講", "架空科目C" },
        };
        var ns = Namespace;
        var sheet = new XDocument(new XElement(ns + "worksheet", new XElement(ns + "sheetData",
            values.Select((cells, row) => new XElement(ns + "row", new XAttribute("r", row + 1),
                cells.Select((value, column) => value.Length == 0 ? null : new XElement(ns + "c",
                    new XAttribute("r", $"{(char)('A' + column)}{row + 1}"),
                    new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value)))))))));
        edit?.Invoke(sheet);
        const string relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string documentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var parts = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] =
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>",
            ["_rels/.rels"] = $"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"book\" " +
                $"Type=\"{documentRelationships}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
            ["xl/workbook.xml"] = $"<workbook xmlns=\"{ns}\" xmlns:q=\"{documentRelationships}\"><sheets>" +
                "<sheet name=\"時間割変更\" sheetId=\"1\" q:id=\"sheet\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = $"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"sheet\" " +
                $"Type=\"{documentRelationships}/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>",
            ["xl/worksheets/sheet1.xml"] = sheet.ToString(),
        };
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var part in parts)
            {
                using var writer = new StreamWriter(archive.CreateEntry(part.Key).Open(), new UTF8Encoding(false));
                writer.Write(part.Value);
            }
        return output.ToArray();
    }

    public static byte[] Valid() => Create(sheet =>
    {
        sheet.Descendants(Namespace + "row").Where(row => (string?)row.Attribute("r") is "3" or "4")
            .ToArray().ToList().ForEach(row => row.Remove());
    });
}
