using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class IosParityTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private static JsonNode Reference => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "ios-parity.json")))!;
    public static IEnumerable<object[]> ScheduleCases() => Reference["schedule"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [Theory]
    [MemberData(nameof(ScheduleCases))]
    public void ScheduleMatchesOriginalSwiftWithReviewedClockCorrections(string id)
    {
        var row = Reference["schedule"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!;
        var data = row["input"]!.Deserialize<ScheduleData>(Options)!;
        // ExportParity.swift constructs this fictional model with April 5 as
        // its dedicated clock day; it is not a parsed, sorted legacy cache.
        // Supply that original input contract explicitly after schema expansion.
        data = data with { Specials = data.Specials!.Select(special =>
        {
            if (special.Kind != MaterialKind.ExamReturn) return special;
            Assert.Equal(new[] { "2032-04-05", "2032-04-06" }, special.CoveredDates);
            return special with { DedicatedTimeDate = "2032-04-05" };
        }).ToArray() };
        Assert.True(SchoolDate.TryParse(row["day"]!.GetValue<string>(), out var day));
        var className = row["className"]!.GetValue<string>();
        var engine = new TimetableEngine(data, row["includesChanges"]!.GetValue<bool>());
        string[] classes = [className];
        var days = engine.DisplayedDays(day.Monday(), classes);
        var bounds = engine.ReachableWeeks(day, classes);
        var actual = JsonSerializer.SerializeToNode(new
        {
            blocks = TimetableEngine.Positioned(engine.Blocks(day, className)).Select(position => new
            {
                start = position.Block.StartPeriod, end = position.Block.EndPeriod, lane = position.Lane,
                kind = position.Block.Content switch { NormalContent => "normal", ChangeContent => "change", SpecialContent special => JsonNamingPolicy.CamelCase.ConvertName(special.Kind.ToString()), _ => throw new InvalidOperationException() },
                subject = position.Block.Content.Names.Subject,
                time = engine.CardTime(day, className, position.Block)?.Display
            }),
            lower = bounds.Lower.Iso(), upper = bounds.Upper.Iso(), days = days.Select(value => value.Iso()),
            missingCount = engine.MissingMessages(day, className).Count,
            commonTimes = Enumerable.Range(1, 8).Select(period => engine.CommonPeriodTime(period, days, classes)?.Display),
            changeTimes = data.Changes!.Select(change => engine.ChangeTimes(change).Select(time => time?.Display))
        }, Options);
        var expected = ScheduleExpectation(row);
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{id}\nExpected: {expected}\nC#: {actual}");
    }

    private static JsonNode ScheduleExpectation(JsonNode row)
    {
        var expected = row["expected"]!.DeepClone();
        // The immutable Swift fixture deliberately retains the old bug from
        // iOS #46. Only these reviewed cases have two corrected card clocks;
        // every other field is compared with the original fixture, except for
        // the four missing-source cases explicitly listed below. No production
        // result constructs these independent expectations.
        string[] corrected = ["tag-empty-changes-false-special-3", "tag-授業なし-changes-false-special-3",
            "tag-補講日-changes-false-special-3", "tag-行事（授業なし）-changes-false-special-3",
            "tag-行事（授業あり）-changes-false-special-3", "tag-行事-changes-false-special-3",
            "tag-行事（時間割変更）-changes-false-special-3", "tag-テスト-changes-false-special-3",
            "tag-テスト返却-changes-false-special-3", "tag-行事メモ-changes-false-special-3", "tag-曜日振替-changes-false-special-3"];
        if (corrected.Contains(row["id"]!.GetValue<string>()))
        {
            var blocks = expected["blocks"]!.AsArray();
            Assert.Equal(2, blocks.Count);
            Assert.Equal("exam", blocks[0]!["kind"]!.GetValue<string>());
            Assert.Equal("examReturn", blocks[1]!["kind"]!.GetValue<string>());
            Assert.Null(blocks[0]!["time"]); Assert.Null(blocks[1]!["time"]);
            blocks[0]!["time"] = "09:10～09:50";
            blocks[1]!["time"] = "09:10～10:00";
        }
        // iOS #90 / Windows cross-check: the immutable old implementation
        // confirmed a change clock even while its own event required a missing
        // special PDF. Keep the original fixture unchanged and assert exactly
        // which old values these independently tested cases correct.
        string[] missingSources = ["tag-テスト返却-changes-true-special-1", "tag-テスト返却-changes-false-special-1",
            "tag-テスト-changes-true-special-2", "tag-テスト-changes-false-special-2"];
        if (missingSources.Contains(row["id"]!.GetValue<string>()))
        {
            var returnsMissing = row["id"]!.GetValue<string>().StartsWith("tag-テスト返却-", StringComparison.Ordinal);
            var input = row["input"]!.Deserialize<ScheduleData>(Options)!;
            var missingKind = returnsMissing ? MaterialKind.ExamReturn : MaterialKind.Exam;
            Assert.DoesNotContain(input.Specials!, source => source.Kind == missingKind);
            Assert.Contains(input.Events!, item => item.Date == "2032-04-05" && item.Tag == (returnsMissing ? "テスト返却" : "テスト"));
            var oldClocks = returnsMissing ? new[] { "09:10～09:50", "09:30～09:50", "09:10～09:30" }
                : new[] { "09:10～10:00", "09:40～10:00", "09:10～09:30" };
            var changeTimes = expected["changeTimes"]!.AsArray();
            Assert.Equal(4, changeTimes.Count);
            for (var index = 0; index < oldClocks.Length; index++)
            {
                Assert.Equal(oldClocks[index], changeTimes[index]![0]!.GetValue<string>());
                changeTimes[index]![0] = null;
            }
            if (row["includesChanges"]!.GetValue<bool>())
            {
                var block = Assert.Single(expected["blocks"]!.AsArray())!;
                Assert.Equal("change", block["kind"]!.GetValue<string>());
                Assert.Equal(oldClocks[0], block["time"]!.GetValue<string>());
                block["time"] = null;
            }
        }
        return expected;
    }

    [Fact]
    public void XlsxNormalizationMatchesOriginalSwift()
    {
        foreach (var item in Reference["changeNormalizer"]!.AsArray())
        {
            var rows = item!["rows"]!.Deserialize<string[][]>(Options)!;
            JsonNode? actual;
            try { actual = JsonSerializer.SerializeToNode(new { records = ChangeNormalizer.Parse(rows, item["defaultYear"]!.GetValue<int>()).Select(c => new { c.ChangeDate, c.ClassName, c.Period, c.BeforeSubject, c.AfterSubject, c.Teacher, c.Room, c.Note, c.RawText, c.CanonicalText }) }, Options); }
            catch (ChangeParseException error) { actual = JsonSerializer.SerializeToNode(new { error = JsonNamingPolicy.CamelCase.ConvertName(error.Code.ToString()), row = error.Row }, Options); }
            Assert.True(JsonNode.DeepEquals(item["expected"], actual), $"{item["id"]}: Swift {item["expected"]}, C# {actual}");
        }
    }

    [Fact]
    public void TextSearchClassesAndColorsMatchOriginalSwift()
    {
        var reference = Reference;
        Assert.Equal("bbd2bd7b7606e2135279d4b90bb61fd4e80fe501", reference["iosCommit"]!.GetValue<string>());
        foreach (var row in reference["text"]!.AsArray())
        {
            var source = row!["source"]!.GetValue<string>();
            Assert.Equal(row["continuous"]!.GetValue<string>(), DisplayText.Continuous(source));
            Assert.Equal(row["kana"]!.GetValue<string>(), DisplayText.FullWidthKana(source));
            Assert.Equal(row["compact"]!.GetValue<string>(), DisplayText.HalfWidthKana(source));
        }
        foreach (var row in reference["search"]!.AsArray())
        {
            var query = row!["query"]!.GetValue<string>();
            Assert.Equal(row["normalize"]!.GetValue<string>(), LinkSearch.Normalize(query));
            Assert.Equal(row["romaji"]!.GetValue<string>(), LinkSearch.Romaji(query));
            Assert.Equal(row["score"]!.GetValue<int>(), LinkSearch.Score(row["terms"]!.GetValue<string>(), query));
        }
        Assert.Equal(reference["classes"]!.AsArray().Select(row => row!.GetValue<string>()), ClassSelection.Candidates.Order(StringComparer.Ordinal));
        Assert.Equal(reference["colors"]!.AsArray().Select(row => row!["key"]!.GetValue<string>()), UserPreferences.MainColors);
        foreach (var row in reference["colors"]!.AsArray()) Assert.Equal(row!["label"]!.GetValue<string>(), UserPreferences.MainColorLabel(row["key"]!.GetValue<string>()));
    }
}
