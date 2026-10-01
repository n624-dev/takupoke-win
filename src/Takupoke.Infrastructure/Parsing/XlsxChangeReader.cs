using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Takupoke.Core;

namespace Takupoke.Infrastructure.Parsing;

public sealed record ChangeTable(IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<ChangeParseException> Warnings);

public static class XlsxChangeReader
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string DocumentRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static IReadOnlyList<ScheduleChange> Parse(byte[] data, int schoolYear, CancellationToken cancellationToken = default)
    {
        var table = ReadForPreview(data, schoolYear, cancellationToken);
        if (table.Warnings.FirstOrDefault() is { } warning) throw warning;
        return ChangeNormalizer.Parse(table.Rows, schoolYear, cancellationToken);
    }
    public static ChangeTable ReadForPreview(byte[] data, int schoolYear, CancellationToken cancellationToken = default)
    {
        try { return Read(data, schoolYear, cancellationToken); }
        catch (ChangeParseException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch { throw new ChangeParseException(ChangeErrorCode.InvalidArchive); }
    }
    private static ChangeTable Read(byte[] bytes, int schoolYear, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var zip = new BoundedZip(bytes);
        if (zip.Contains("xl/vbaProject.bin")) throw new ChangeParseException(ChangeErrorCode.Unsupported);
        XElement Xml(string name, string root, string ns) => BoundedXml.Read(zip.Read(name, BoundedXml.MaximumBytes, token), root, ns, token);
        XNamespace spreadsheet = SpreadsheetNamespace;
        var workbook = Xml("xl/workbook.xml", "workbook", SpreadsheetNamespace);
        var date1904 = (string?)workbook.Element(spreadsheet + "workbookPr")?.Attribute("date1904");
        if (date1904 is not null and not "0" and not "false") throw new ChangeParseException(ChangeErrorCode.DateSystem);
        if (workbook.Element(spreadsheet + "externalReferences") is not null) throw new ChangeParseException(ChangeErrorCode.Unsupported);
        var sheets = workbook.Element(spreadsheet + "sheets")?.Elements(spreadsheet + "sheet").Where(s => (string?)s.Attribute("name") == "時間割変更").ToArray() ?? [];
        if (sheets.Length != 1 || sheets[0].Attribute(XName.Get("id", DocumentRelationshipNamespace)) is not { } id) throw new ChangeParseException(ChangeErrorCode.MissingSheet);
        var relationships = Xml("xl/_rels/workbook.xml.rels", "Relationships", RelationshipNamespace)
            .Elements(XName.Get("Relationship", RelationshipNamespace)).Where(r => (string?)r.Attribute("Id") == id.Value).ToArray();
        if (relationships.Length != 1) throw new ChangeParseException(ChangeErrorCode.Unsupported);
        var relationship = relationships[0];
        var target = (string?)relationship.Attribute("Target");
        if ((string?)relationship.Attribute("Type") != DocumentRelationshipNamespace + "/worksheet"
            || ((string?)relationship.Attribute("TargetMode") ?? "Internal") != "Internal" || target is null
            || target.IndexOfAny([':', '%', '\\', '?', '#']) >= 0) throw new ChangeParseException(ChangeErrorCode.Unsupported);
        var sheetPath = target.StartsWith('/') ? target[1..] : "xl/" + target;
        if (sheetPath.Split('/').Any(p => p is "." or "..")) throw new ChangeParseException(ChangeErrorCode.InvalidArchive);
        _ = Xml(sheetPath, "worksheet", SpreadsheetNamespace);
        if (zip.Contains("xl/sharedStrings.xml")) _ = Xml("xl/sharedStrings.xml", "sst", SpreadsheetNamespace);

        // The SDK identifies document parts and supplies typed rows/cells only after bounded structural checks.
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false, new OpenSettings { AutoSave = false, MaxCharactersInPart = BoundedXml.MaximumBytes });
        var part = document.WorkbookPart ?? throw new ChangeParseException(ChangeErrorCode.InvalidXml);
        var sdkSheets = part.Workbook.GetFirstChild<Sheets>()?.Elements<Sheet>().Where(s => s.Name?.Value == "時間割変更").ToArray() ?? [];
        if (sdkSheets.Length != 1 || sdkSheets[0].Id?.Value != id.Value || part.GetPartById(id.Value) is not WorksheetPart worksheetPart)
            throw new ChangeParseException(ChangeErrorCode.MissingSheet);
        var strings = part.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(RichText).ToArray() ?? [];
        if (strings.Length > 50_000) throw new ChangeParseException(ChangeErrorCode.Limit);
        var sheet = worksheetPart.Worksheet;
        var sheetData = sheet.GetFirstChild<SheetData>() ?? throw new ChangeParseException(ChangeErrorCode.InvalidXml);
        var rows = new List<IReadOnlyList<string>>();
        var formulas = new List<(int Row, int Column, bool HasCache)>();
        var cellCount = 0;
        long textBytes = 0;
        foreach (var row in sheetData.Elements<Row>())
        {
            token.ThrowIfCancellationRequested();
            var number = (int)(row.RowIndex?.Value ?? 0);
            if (number <= rows.Count || number > ChangeNormalizer.MaximumRows) throw new ChangeParseException(ChangeErrorCode.Limit);
            var cells = new List<string>();
            var seen = new HashSet<int>();
            foreach (var cell in row.Elements<Cell>())
            {
                if (++cellCount > 100_000 || cell.CellReference?.Value is not { } reference) throw new ChangeParseException(ChangeErrorCode.Limit);
                var position = Coordinate(reference);
                if (position.Row != number || !seen.Add(position.Column)) throw new ChangeParseException(ChangeErrorCode.InvalidXml);
                var formula = cell.GetFirstChild<CellFormula>() is not null;
                var type = cell.GetAttributes().FirstOrDefault(a => a.LocalName == "t" && a.NamespaceUri == "").Value;
                if (string.IsNullOrEmpty(type)) type = "n";
                var value = cell.GetFirstChild<CellValue>()?.Text ?? "";
                if (formula) formulas.Add((number, position.Column, value.Length > 0 && new[] { "n", "str", "s", "b", "d" }.Contains(type)));
                string decoded;
                switch (type)
                {
                    case "inlineStr": decoded = cell.GetFirstChild<InlineString>() is { } inline ? RichText(inline) : ""; break;
                    case "s":
                        if (!int.TryParse(value, out var index) || index < 0 || index >= strings.Length) throw new ChangeParseException(ChangeErrorCode.InvalidXml, number);
                        decoded = strings[index]; break;
                    case "b":
                        if (value is not "0" and not "1") throw new ChangeParseException(ChangeErrorCode.InvalidXml, number);
                        decoded = value == "1" ? "TRUE" : "FALSE"; break;
                    case "n": case "str": case "d": decoded = value; break;
                    default:
                        if (!formula) throw new ChangeParseException(ChangeErrorCode.CellType, number);
                        decoded = ""; break;
                }
                textBytes += Encoding.UTF8.GetByteCount(decoded);
                if (textBytes > ChangeNormalizer.MaximumTextBytes || Encoding.UTF8.GetByteCount(decoded) > 4096) throw new ChangeParseException(ChangeErrorCode.Limit);
                while (cells.Count <= position.Column) cells.Add("");
                cells[position.Column] = decoded;
            }
            while (rows.Count < number - 1) rows.Add([]);
            rows.Add(cells);
        }
        var headerRows = rows.Select(r => (IReadOnlyList<string>)r.ToArray()).ToArray();
        foreach (var formula in formulas) ((string[])headerRows[formula.Row - 1])[formula.Column] = "";
        var header = ChangeNormalizer.HeaderIndex(headerRows) + 1;
        var headers = rows[header - 1].Select(ChangeNormalizer.Token).ToArray();
        var dateColumn = Array.IndexOf(headers, "月日");
        var warnings = new List<ChangeParseException>();
        foreach (var formula in formulas)
        {
            token.ThrowIfCancellationRequested();
            if (formula.Row < header) { ((List<string>)rows[formula.Row - 1])[formula.Column] = ""; continue; }
            if (formula.Row == header || formula.Column >= headers.Length || headers[formula.Column] is not "曜日" and not "曜") throw new ChangeParseException(ChangeErrorCode.Formula, formula.Row);
            if (dateColumn >= rows[formula.Row - 1].Count) throw new ChangeParseException(ChangeErrorCode.Date, formula.Row);
            string date;
            try { date = ChangeNormalizer.Date(rows[formula.Row - 1][dateColumn], schoolYear); }
            catch { throw new ChangeParseException(ChangeErrorCode.Date, formula.Row); }
            if (!formula.HasCache) warnings.Add(new(ChangeErrorCode.FormulaCache, formula.Row));
            else if (!ChangeNormalizer.WeekdayMatches(rows[formula.Row - 1][formula.Column], date)) warnings.Add(new(ChangeErrorCode.WeekdayMismatch, formula.Row));
        }
        foreach (var merge in sheet.GetFirstChild<MergeCells>()?.Elements<MergeCell>() ?? [])
        {
            var bounds = merge.Reference?.Value?.Split(':') ?? [];
            if (bounds.Length is < 1 or > 2) throw new ChangeParseException(ChangeErrorCode.InvalidXml);
            var end = Coordinate(bounds[^1]);
            if (end.Row >= header) throw new ChangeParseException(ChangeErrorCode.MergedCells, end.Row);
        }
        return new(rows, warnings);
    }
    private static string RichText(DocumentFormat.OpenXml.OpenXmlElement node)
    {
        var value = string.Concat(node.ChildElements.Select(child => child is Text text ? text.Text
            : child is Run run ? string.Concat(run.Elements<Text>().Select(t => t.Text)) : ""));
        if (Encoding.UTF8.GetByteCount(value) > 4096) throw new ChangeParseException(ChangeErrorCode.Limit);
        return value;
    }
    private static (int Row, int Column) Coordinate(string value)
    {
        var match = Regex.Match(value, "^([A-Z]{1,3})([1-9][0-9]{0,6})$");
        if (!match.Success) throw new ChangeParseException(ChangeErrorCode.InvalidXml);
        var column = 0;
        foreach (var letter in match.Groups[1].Value) column = column * 26 + letter - 'A' + 1;
        var row = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (column > ChangeNormalizer.MaximumColumns || row > ChangeNormalizer.MaximumRows) throw new ChangeParseException(ChangeErrorCode.Limit);
        return (row, column - 1);
    }
}
