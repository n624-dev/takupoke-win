using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Takupoke.Core;

namespace Takupoke.Infrastructure.Parsing;

public static partial class XlsxChangeReader
{
    public const int Version = 8;
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string DocumentRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static IReadOnlyList<ScheduleChange> Parse(byte[] data, int schoolYear,
        CancellationToken cancellationToken = default, IReadOnlySet<int>? skippingRows = null)
    {
        var table = ReadForPreview(data, schoolYear, cancellationToken);
        if (skippingRows is { Count: > 0 })
        {
            if (!skippingRows.IsSubsetOf(table.ReviewRows.Select(row => row.Row).ToHashSet()))
                throw new ChangeParseException(ChangeErrorCode.Unsupported);
            // Validate populated excluded rows too: weekday permission cannot hide other errors.
            _ = ChangeNormalizer.Parse(table.WithoutWeekdayOnlyRows(), schoolYear, cancellationToken,
                allowEmptyPreview: true);
        }
        if (table.Warnings.FirstOrDefault(warning =>
            warning.Row is null || skippingRows?.Contains(warning.Row.Value) != true) is { } warning)
            throw warning;
        return ChangeNormalizer.Parse(table.Excluding(skippingRows ?? new HashSet<int>()), schoolYear,
            cancellationToken);
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
        var sdkWorkbook = part.Workbook ?? throw new ChangeParseException(ChangeErrorCode.InvalidXml);
        var sdkSheets = sdkWorkbook.GetFirstChild<Sheets>()?.Elements<Sheet>().Where(s => s.Name?.Value == "時間割変更").ToArray() ?? [];
        if (sdkSheets.Length != 1 || sdkSheets[0].Id?.Value != id.Value || part.GetPartById(id.Value) is not WorksheetPart worksheetPart)
            throw new ChangeParseException(ChangeErrorCode.MissingSheet);
        var strings = part.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(RichText).ToArray() ?? [];
        if (strings.Length > 50_000) throw new ChangeParseException(ChangeErrorCode.Limit);
        var sheet = worksheetPart.Worksheet ?? throw new ChangeParseException(ChangeErrorCode.InvalidXml);
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
                    case "str": decoded = DecodedString(value); break;
                    case "d": decoded = TypedDate(value, number); break;
                    case "n": decoded = value; break;
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
        }
        CollectWeekdayWarnings(rows, formulas, header, headers, dateColumn, schoolYear, warnings, token);
        foreach (var merge in sheet.GetFirstChild<MergeCells>()?.Elements<MergeCell>() ?? [])
        {
            var bounds = merge.Reference?.Value?.Split(':') ?? [];
            if (bounds.Length is < 1 or > 2) throw new ChangeParseException(ChangeErrorCode.InvalidXml);
            var end = Coordinate(bounds[^1]);
            if (end.Row >= header) throw new ChangeParseException(ChangeErrorCode.MergedCells, end.Row);
        }
        return new(rows, warnings, ReviewRows(rows, rows[header - 1], warnings));
    }
    private static string RichText(DocumentFormat.OpenXml.OpenXmlElement node)
    {
        var value = string.Concat(node.ChildElements.Select(child => child is Text text ? DecodedString(text.Text)
            : child is Run run ? string.Concat(run.Elements<Text>().Select(t => DecodedString(t.Text))) : ""));
        if (Encoding.UTF8.GetByteCount(value) > 4096) throw new ChangeParseException(ChangeErrorCode.Limit);
        return value;
    }
    private static string DecodedString(string source)
    {
        var result = new StringBuilder();
        for (var i = 0; i < source.Length;)
        {
            if (i + 6 < source.Length && source[i] == '_' && source[i + 1] == 'x' && source[i + 6] == '_'
                && source.AsSpan(i + 2, 4).ToArray().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')
                && ushort.TryParse(source.AsSpan(i + 2, 4), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var unit))
            { result.Append((char)unit); i += 7; }
            else result.Append(source[i++]);
        }
        var text = result.ToString();
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[++i])) throw new ChangeParseException(ChangeErrorCode.InvalidXml);
            }
            else if (char.IsLowSurrogate(text[i])) throw new ChangeParseException(ChangeErrorCode.InvalidXml);
        }
        return text;
    }
    private static string TypedDate(string value, int row)
    {
        // A spreadsheet date is its written calendar day. Validate the entire
        // typed ISO value, including its clock/offset, without timezone shifts.
        var match = Regex.Match(value, @"\A([0-9]{4}-[0-9]{2}-[0-9]{2})(?:T([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.[0-9]{1,9})?(?:Z|([+-])([0-9]{2}):([0-9]{2}))?)?\z");
        if (!match.Success) throw new ChangeParseException(ChangeErrorCode.Date, row);
        if (match.Groups[2].Success && (int.Parse(match.Groups[2].Value) > 23
            || int.Parse(match.Groups[3].Value) > 59 || int.Parse(match.Groups[4].Value) > 59))
            throw new ChangeParseException(ChangeErrorCode.Date, row);
        if (match.Groups[5].Success && (int.Parse(match.Groups[6].Value) > 14
            || int.Parse(match.Groups[7].Value) > 59
            || int.Parse(match.Groups[6].Value) == 14 && int.Parse(match.Groups[7].Value) != 0))
            throw new ChangeParseException(ChangeErrorCode.Date, row);
        try { return ChangeNormalizer.Date(match.Groups[1].Value, null); }
        catch (ChangeParseException error) { throw new ChangeParseException(error.Code, row); }
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
