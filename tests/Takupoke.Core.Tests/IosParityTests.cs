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
    public void ScheduleMatchesOriginalSwift(string id)
    {
        var row = Reference["schedule"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!;
        var data = row["input"]!.Deserialize<ScheduleData>(Options)!;
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
        Assert.True(JsonNode.DeepEquals(row["expected"], actual), $"{id}\nSwift: {row["expected"]}\nC#: {actual}");
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
