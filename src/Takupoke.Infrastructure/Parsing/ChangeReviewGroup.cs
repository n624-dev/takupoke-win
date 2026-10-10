using Takupoke.Core;

namespace Takupoke.Infrastructure.Parsing;

// Display grouping only. The parser still validates and authorizes physical row IDs.
public sealed record ChangeReviewGroup(IReadOnlyList<ChangeReviewRow> Rows)
{
    public int[] RowIds => Rows.Select(row => row.Row).ToArray();
    public string RangeLabel => DescribeRanges(RowIds);
    public string AutomationId => Rows.Count == 1 ? $"change-skip-row-{Rows[0].Row}"
        : $"change-skip-group-{Rows[0].Row}-{Rows[^1].Row}";

    public static IReadOnlyList<ChangeReviewGroup> Create(IReadOnlyList<ChangeReviewRow> rows,
        IReadOnlyList<ChangeParseException> warnings)
    {
        var placeholders = warnings.Where(warning => warning.Code == ChangeErrorCode.WeekdayOnly)
            .Select(warning => warning.Row).ToHashSet();
        var groups = new List<List<ChangeReviewRow>>();
        bool Placeholder(ChangeReviewRow row) => placeholders.Contains(row.Row)
            && row.Fields.Count(field => ChangeNormalizer.Token(field.Name) is "曜日" or "曜") == 1
            && row.Fields.All(field => ChangeNormalizer.Token(field.Name) is "曜日" or "曜"
                || ChangeNormalizer.Text(field.Value).Length == 0);
        foreach (var row in rows)
        {
            var previous = groups.Count > 0 ? groups[^1][^1] : null;
            if (previous is not null && previous.Row < int.MaxValue && row.Row == previous.Row + 1
                && Placeholder(previous) && Placeholder(row) && previous.Fields.SequenceEqual(row.Fields))
                groups[^1].Add(row);
            else groups.Add([row]);
        }
        return groups.Select(group => new ChangeReviewGroup(group.ToArray())).ToArray();
    }

    public static string DescribeRanges(IEnumerable<int> rows)
    {
        var sorted = rows.Distinct().Order().ToArray();
        if (sorted.Length == 0) return "";
        var ranges = new List<string>();
        var start = sorted[0];
        var end = start;
        void AddRange() => ranges.Add(start == end ? $"{start}行目" : $"{start}〜{end}行目");
        foreach (var row in sorted.Skip(1))
        {
            if (end < int.MaxValue && row == end + 1) end = row;
            else { AddRange(); start = row; end = row; }
        }
        AddRange();
        return string.Join("、", ranges);
    }
}
