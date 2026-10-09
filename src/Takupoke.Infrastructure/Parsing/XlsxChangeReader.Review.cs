using Takupoke.Core;

namespace Takupoke.Infrastructure.Parsing;

public sealed record ChangeReviewField(string Name, string Value);
public sealed record ChangeReviewRow(int Row, IReadOnlyList<ChangeReviewField> Fields);
public sealed record ChangeTable(IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<ChangeParseException> Warnings, IReadOnlyList<ChangeReviewRow> ReviewRows)
{
    public IReadOnlyList<IReadOnlyList<string>> Excluding(IReadOnlySet<int> excluded) =>
        Rows.Select((row, index) => excluded.Contains(index + 1)
            ? (IReadOnlyList<string>)Array.Empty<string>() : row).ToArray();

    public IReadOnlyList<IReadOnlyList<string>> WithoutWeekdayOnlyRows() =>
        Excluding(Warnings.Where(warning => warning.Code == ChangeErrorCode.WeekdayOnly)
            .Select(warning => warning.Row!.Value).ToHashSet());
}

public static partial class XlsxChangeReader
{
    private static void CollectWeekdayWarnings(IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<(int Row, int Column, bool HasCache)> formulas, int header, string[] headers,
        int dateColumn, int year, List<ChangeParseException> warnings, CancellationToken token)
    {
        var columns = Enumerable.Range(0, headers.Length)
            .Where(column => headers[column] is "曜日" or "曜").ToArray();
        if (columns.Length > 1) throw new ChangeParseException(ChangeErrorCode.Headers, header);
        if (columns.Length == 0) return;
        var column = columns[0];
        var caches = formulas.Where(formula => formula.Row > header && formula.Column == column)
            .ToDictionary(formula => formula.Row, formula => formula.HasCache);
        for (var number = header + 1; number <= rows.Count; number++)
        {
            token.ThrowIfCancellationRequested();
            var row = rows[number - 1];
            if (row.Skip(headers.Length).Any(value => ChangeNormalizer.Text(value).Length > 0))
                throw new ChangeParseException(ChangeErrorCode.Headers, number);
            var printed = column < row.Count ? row[column] : "";
            if (!caches.ContainsKey(number) && string.IsNullOrWhiteSpace(printed)) continue;
            if (row.Select((value, index) => index == column || ChangeNormalizer.Text(value).Length == 0)
                .All(empty => empty))
            {
                warnings.Add(new(ChangeErrorCode.WeekdayOnly, number));
                continue;
            }
            if (dateColumn >= row.Count) throw new ChangeParseException(ChangeErrorCode.Date, number);
            string date;
            try { date = ChangeNormalizer.Date(row[dateColumn], year); }
            catch (ChangeParseException) { throw new ChangeParseException(ChangeErrorCode.Date, number); }
            if (caches.TryGetValue(number, out var cached) && !cached)
                warnings.Add(new(ChangeErrorCode.FormulaCache, number));
            else if (!ChangeNormalizer.WeekdayMatches(printed, date))
                warnings.Add(new(ChangeErrorCode.WeekdayMismatch, number));
        }
    }

    private static IReadOnlyList<ChangeReviewRow> ReviewRows(IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<string> headers, IReadOnlyList<ChangeParseException> warnings) => warnings
        .Where(warning => warning.Code is ChangeErrorCode.WeekdayMismatch or ChangeErrorCode.WeekdayOnly)
        .Select(warning => new ChangeReviewRow(warning.Row!.Value,
            headers.Select((name, column) => new ChangeReviewField(name,
                column < rows[warning.Row.Value - 1].Count ? rows[warning.Row.Value - 1][column] : ""))
            .ToArray())).ToArray();
}
