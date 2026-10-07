using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Takupoke.Core;

public enum ChangeErrorCode
{
    InvalidArchive, Limit, InvalidXml, MissingSheet, Unsupported, Headers, Date, Year, Classes,
    UnknownAll, Empty, Cancelled, Storage, Formula, FormulaCache, WeekdayMismatch, MergedCells, DateSystem, CellType
}
public sealed class ChangeParseException(ChangeErrorCode code, int? row = null) : Exception(MessageFor(code, row))
{
    public ChangeErrorCode Code { get; } = code;
    public int? Row { get; } = row;
    public string? PrintedWeekday { get; init; }
    public string? CalculatedWeekday { get; init; }
    public bool CanCorrectWeekday => Code == ChangeErrorCode.WeekdayMismatch && PrintedWeekday is not null && ChangeNormalizer.KnownWeekday(PrintedWeekday) && CalculatedWeekday is not null;
    public bool PermitsPreview => Code is ChangeErrorCode.FormulaCache or ChangeErrorCode.WeekdayMismatch;
    private static string MessageFor(ChangeErrorCode code, int? row) => (row is null ? "" : $"{row}行目：") + (code switch
    {
        ChangeErrorCode.MissingSheet => "「時間割変更」シートが見つからないか、重複しています。",
        ChangeErrorCode.Headers => "必要な見出しがないか、見出しが重複しています。",
        ChangeErrorCode.Date => "日付を確定できません。補完する学校年度を確認してください。",
        ChangeErrorCode.Year => "学年の指定を読み取れません。",
        ChangeErrorCode.Classes => "クラスの指定を読み取れません。",
        ChangeErrorCode.UnknownAll => "「全」の対象クラスをファイル内の記載から確定できません。",
        ChangeErrorCode.Empty => "時間割変更の行がありません。",
        ChangeErrorCode.Formula => "見出し、または曜日以外の列に数式があります。",
        ChangeErrorCode.FormulaCache => "曜日の計算結果が保存されていません。",
        ChangeErrorCode.WeekdayMismatch => "曜日と月日が一致しないか、曜日の表記を確認できません。",
        ChangeErrorCode.MergedCells => "見出しや表の行に結合セルがあります。",
        ChangeErrorCode.DateSystem => "1904年起点の日付を使用するXLSXは未対応です。",
        ChangeErrorCode.CellType => "表に未対応のセル形式やExcelのエラー値があります。",
        ChangeErrorCode.Limit => "ファイルが解析可能なサイズ・行数・件数の上限を超えています。",
        ChangeErrorCode.Cancelled => "解析を中止しました。",
        ChangeErrorCode.Storage => "解析結果を保存できません。",
        ChangeErrorCode.InvalidArchive => "XLSXの構造を読み取れません。",
        ChangeErrorCode.InvalidXml => "XLSX内の表の構造が不正です。",
        _ => "このXLSXには未対応の構造や外部参照が含まれています。"
    }) + " 前回の正常な解析結果は保持しています。";
}

public static class ChangeNormalizer
{
    public const int MaximumRows = 10_000, MaximumColumns = 128, MaximumRecords = 20_000, MaximumTextBytes = 16 * 1024 * 1024;
    public static string Text(string value) => Regex.Replace(Regex.Replace(value.Normalize(NormalizationForm.FormKC)
        .Replace("\r\n", "\n").Replace('\r', '\n'), "[ \t\f\u000B]+", " "), "\n+", "\n").Trim();
    public static string Token(string value) => Text(value).Replace(" ", "").ToUpperInvariant();
    public static string Number(string value)
    {
        var text = Text(value);
        return Regex.IsMatch(text, @"^-?[0-9]+\.0+$") ? text.Split('.')[0] : text;
    }
    public static int EffectiveSchoolYear(string? configured, DateOnly today) =>
        int.TryParse(configured?.Trim(), out var year) && year is >= 1900 and <= 9998 ? year : today.SchoolYear();
    public static IReadOnlyList<string> Years(string value)
    {
        var text = Text(value);
        if (Token(text) == "AI") return ["AI"];
        var range = Regex.Match(text, @"^([0-9]+)\s*[～〜~-]\s*([0-9]+)$");
        if (range.Success && int.TryParse(range.Groups[1].Value, out var first) && int.TryParse(range.Groups[2].Value, out var last)
            && first is >= 1 and <= 9 && last is >= 1 and <= 9)
        {
            var result = new List<string>();
            for (var n = first; ; n += first <= last ? 1 : -1)
            {
                result.Add(n.ToString(CultureInfo.InvariantCulture));
                if (n == last) return result;
            }
        }
        if (Regex.IsMatch(Token(text), "^[0-9]+$") && int.TryParse(Token(text), out var year) && year is >= 1 and <= 9) return [Token(text)];
        throw new ChangeParseException(ChangeErrorCode.Year);
    }
    public static IReadOnlyList<string> Classes(string value)
    {
        if (Token(value) == "全") return [];
        var pieces = Regex.Split(Text(value), "[,，、]").Select(Token).Where(p => p.Length > 0).ToArray();
        if (pieces.Length == 0 || pieces.Any(p => !Regex.IsMatch(p, "^[A-Z0-9]{1,12}$"))) throw new ChangeParseException(ChangeErrorCode.Classes);
        return pieces;
    }
    public static int HeaderIndex(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        for (var index = 0; index < rows.Count; index++)
            if (new[] { "学 年", "学科・クラス", "月日" }.All(h => rows[index].Select(Text).Contains(h))) return index;
        throw new ChangeParseException(ChangeErrorCode.Headers);
    }
    public static string SerialDate(string value)
    {
        if (!Regex.IsMatch(value, @"^[0-9]+(\.[0-9]+)?$")) return value;
        if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var days) || days <= 0 || days >= 2_958_466)
            throw new ChangeParseException(ChangeErrorCode.Date);
        return new DateTime(1899, 12, 30).AddDays(days).ToString("yyyy/M/d", CultureInfo.InvariantCulture);
    }
    public static string Date(string value, int? defaultYear)
    {
        var text = SerialDate(Text(value));
        foreach (var separator in new[] { '年', '月', '.', '-' }) text = text.Replace(separator, '/');
        text = Regex.Replace(text.Replace("日", ""), @"\s+", "");
        int year, month, day;
        if (Regex.IsMatch(text, "^[0-9]{4}/[0-9]{1,2}/[0-9]{1,2}$"))
        {
            var parts = text.Split('/').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            (year, month, day) = (parts[0], parts[1], parts[2]);
        }
        else if (Regex.IsMatch(text, "^[0-9]{1,2}/[0-9]{1,2}$") && defaultYear is not null)
        {
            var parts = text.Split('/').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            (year, month, day) = (defaultYear.Value + (parts[0] <= 3 ? 1 : 0), parts[0], parts[1]);
        }
        else throw new ChangeParseException(ChangeErrorCode.Date);
        if (year is < 1900 or > 9999 || !SchoolDate.TryParse($"{year:D4}-{month:D2}-{day:D2}", out var date)) throw new ChangeParseException(ChangeErrorCode.Date);
        return date.Iso();
    }
    public static bool KnownWeekday(string value) => "日月火水木金土".Any(day =>
        new[] { day.ToString(), day + "曜", day + "曜日", "(" + day + ")" }.Contains(Text(value)));
    public static string Weekday(string normalizedDate) => SchoolDate.TryParse(normalizedDate, out var day)
        ? "日月火水木金土"[(int)day.DayOfWeek].ToString() : throw new ChangeParseException(ChangeErrorCode.Date);
    public static bool WeekdayMatches(string value, string normalizedDate)
    {
        var weekday = Weekday(normalizedDate);
        return new[] { weekday, weekday + "曜", weekday + "曜日", "(" + weekday + ")" }.Contains(Text(value));
    }
    public static IReadOnlyList<ScheduleChange> Parse(IReadOnlyList<IReadOnlyList<string>> rows, int? defaultYear, CancellationToken cancellationToken = default)
    {
        if (rows.Count > MaximumRows || rows.Any(r => r.Count > MaximumColumns || r.Any(c => Encoding.UTF8.GetByteCount(c) > 4096))
            || rows.Sum(r => r.Sum(c => (long)Encoding.UTF8.GetByteCount(c))) > MaximumTextBytes) throw new ChangeParseException(ChangeErrorCode.Limit);
        if (defaultYear is not null && defaultYear is < 1900 or > 9999) throw new ChangeParseException(ChangeErrorCode.Date);
        var index = HeaderIndex(rows);
        var headers = rows[index].Select(h => Text(h) == "時限" ? h : Text(h)).ToArray();
        var nonempty = headers.Where(h => h.Length > 0).ToArray();
        if (nonempty.Distinct().Count() != nonempty.Length) throw new ChangeParseException(ChangeErrorCode.Headers, index + 1);
        var dateIndex = Array.IndexOf(headers, "月日");
        var yearIndex = Array.IndexOf(headers, "学 年");
        var classIndex = Array.IndexOf(headers, "学科・クラス");
        var table = new List<(int Row, string[] Cells, IReadOnlyList<string> Years, IReadOnlyList<string> Classes)>();
        var known = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (var i = index + 1; i < rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rows[i].Skip(headers.Length).Any(c => Text(c).Length > 0)) throw new ChangeParseException(ChangeErrorCode.Headers, i + 1);
            var cells = rows[i].Take(headers.Length).Select(Number).Concat(Enumerable.Repeat("", Math.Max(0, headers.Length - rows[i].Count))).ToArray();
            if (cells.All(string.IsNullOrEmpty)) continue;
            try
            {
                cells[dateIndex] = SerialDate(cells[dateIndex]);
                var years = Years(cells[yearIndex]);
                var classes = Classes(cells[classIndex]);
                foreach (var year in years)
                {
                    if (!known.TryGetValue(year, out var set)) known[year] = set = new(StringComparer.Ordinal);
                    set.UnionWith(classes);
                }
                table.Add((i + 1, cells, years, classes));
            }
            catch (ChangeParseException error) { throw new ChangeParseException(error.Code, i + 1); }
        }
        var output = new List<ScheduleChange>();
        long textBytes = 0;
        foreach (var row in table)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string Value(params string[] aliases)
            {
                var keys = aliases.Select(Token).ToHashSet();
                var column = Array.FindIndex(headers, h => keys.Contains(Token(h)));
                return column < 0 ? "" : row.Cells[column];
            }
            string date;
            try { date = Date(row.Cells[dateIndex], defaultYear); }
            catch (ChangeParseException error) { throw new ChangeParseException(error.Code, row.Row); }
            var period = Number(Regex.Replace(Value("時限", "校時", "時間", "限"), "(時限|限目|限)$", ""));
            var before = Value("変更前", "変更前科目", "変更前 科目", "旧科目", "変更元");
            var after = Value("変更後", "変更後科目", "変更後 科目", "新科目", "変更先");
            var teacher = Value("教員", "担当", "担当教員", "担任", "教官");
            var room = Value("教室", "場所");
            var note = Value("備考", "連絡", "メモ", "その他");
            var content = Value("変更内容", "変更種別", "種別");
            var subject = Value("科目(担当教員)", "科目（担当教員）", "科目・担当教員", "科目");
            if (note.Length == 0) note = content;
            if (before.Length == 0 && after.Length == 0 && subject.Length > 0)
            { if (Token(content) == "休講") before = subject; else after = subject; }
            var raw = string.Join(" | ", headers.Zip(row.Cells).Where(p => p.Second.Length > 0).Select(p => p.First + ":" + p.Second));
            foreach (var year in row.Years)
            {
                var classes = Token(row.Cells[classIndex]) == "全" ? known.GetValueOrDefault(year, []).Where(c => c != "AI").Order(StringComparer.Ordinal).ToArray() : row.Classes;
                if (classes.Count == 0) throw new ChangeParseException(ChangeErrorCode.UnknownAll, row.Row);
                foreach (var cls in classes)
                {
                    var name = ClassSelection.Canonical(year + "_" + cls);
                    var fields = new[] { date, name, period, before, after, teacher, room, note, raw };
                    var canonical = string.Join(" | ", fields.Select(Text).Where(f => f.Length > 0));
                    textBytes += fields.Sum(f => (long)Encoding.UTF8.GetByteCount(f)) + Encoding.UTF8.GetByteCount(canonical);
                    if (output.Count >= MaximumRecords || textBytes > MaximumTextBytes) throw new ChangeParseException(ChangeErrorCode.Limit);
                    output.Add(new(date, name, period, before, after, teacher, room, note, raw, canonical));
                }
            }
        }
        if (output.Count == 0) throw new ChangeParseException(ChangeErrorCode.Empty);
        return output;
    }
}
