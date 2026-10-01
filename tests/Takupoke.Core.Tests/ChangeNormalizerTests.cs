using System.Text.Json;
using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class ChangeNormalizerTests
{
    public static IEnumerable<object[]> GoldenCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "normalization.json")));
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray()) yield return [item.GetProperty("id").GetString()!, item.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void MatchesSharedIosAndReferenceFixtures(string id, string input)
    {
        Assert.NotEmpty(id);
        using var document = JsonDocument.Parse(input);
        var fixture = document.RootElement;
        var rows = fixture.GetProperty("sheetRows").EnumerateArray().Select(row => (IReadOnlyList<string>)row.EnumerateArray().Select(cell => cell.GetString()!).ToArray()).ToArray();
        var year = fixture.TryGetProperty("defaultYear", out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt32() : (int?)null;
        if (fixture.GetProperty("iosDisposition").GetString() != "compare")
        {
            Assert.Throws<ChangeParseException>(() => ChangeNormalizer.Parse(rows, year));
            return;
        }
        var actual = ChangeNormalizer.Parse(rows, year);
        var expected = fixture.GetProperty("expectedRecords").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < actual.Count; i++)
        {
            string Get(string key) => expected[i].GetProperty(key).GetString()!;
            var cls = Get("class_name");
            if (fixture.TryGetProperty("iosClassAliases", out var aliases) && aliases.TryGetProperty(cls, out var alias)) cls = alias.GetString()!;
            var canonical = Get("canonical_text");
            if (cls != Get("class_name")) canonical = canonical.Replace(" | " + Get("class_name") + " | ", " | " + cls + " | ");
            Assert.Equal(new(Get("change_date"), cls, Get("period"), Get("before_subject"), Get("after_subject"), Get("teacher"), Get("room"), Get("note"), Get("raw_text"), canonical), actual[i]);
        }
    }
    [Theory]
    [InlineData("1/10", 2032, "2033-01-10")]
    [InlineData("3/31", 2032, "2033-03-31")]
    [InlineData("4/1", 2032, "2032-04-01")]
    [InlineData("2032年1月10日", 2032, "2032-01-10")]
    public void MissingYearsUseSchoolYear(string input, int year, string expected) => Assert.Equal(expected, ChangeNormalizer.Date(input, year));
    [Theory]
    [InlineData("2033/2/29")]
    [InlineData("2032/4/31")]
    [InlineData("未定")]
    public void RejectsUnconfirmedDates(string value) => Assert.Throws<ChangeParseException>(() => ChangeNormalizer.Date(value, 2032));
    [Fact]
    public void AliasColumnsUseHeaderOrderEvenWhenFirstIsEmpty()
    {
        IReadOnlyList<IReadOnlyList<string>> rows = [
            new[] { "学 年", "学科・クラス", "月日", "旧科目", "変更前", "変更後" },
            new[] { "1", "ZZ", "4/5", "", "架空科目A", "架空科目B" }
        ];
        Assert.Equal("", Assert.Single(ChangeNormalizer.Parse(rows, 2032)).BeforeSubject);
    }
    [Fact]
    public void UnknownAllRejectsEntireResult()
    {
        IReadOnlyList<IReadOnlyList<string>> rows = [new[] { "学 年", "学科・クラス", "月日" }, new[] { "1", "全", "4/5" }];
        Assert.Equal(ChangeErrorCode.UnknownAll, Assert.Throws<ChangeParseException>(() => ChangeNormalizer.Parse(rows, 2032)).Code);
    }
}
