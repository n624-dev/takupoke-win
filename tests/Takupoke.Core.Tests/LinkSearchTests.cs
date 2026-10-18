using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class LinkSearchTests
{
    [Fact]
    public void SyllabusChoiceOnlyChangesTheKnownTargetAndUsesCalendarYear()
    {
        var link = new LinkItem("fictional", "fictional", "高専Webシラバス", "https://l.n624.jp/gSBBt", "blue", true, 1, false, 0, [], "fictional");
        foreach (var department in SyllabusLinkPolicy.Departments)
            Assert.Equal($"https://l.n624.jp/KCWgd?department_id={department.Id}&year=2033", SyllabusLinkPolicy.Resolve(link, department.Id, 2033));
        Assert.Equal(link.Href, SyllabusLinkPolicy.Resolve(link, null, 2033));
        Assert.Equal(link.Href, SyllabusLinkPolicy.Resolve(link, "unknown", 2033));
        var other = link with { Href = "https://example.invalid/gSBBt" };
        Assert.Equal(other.Href, SyllabusLinkPolicy.Resolve(other, "49", 2033));
        Assert.Equal(link.Href, SyllabusLinkPolicy.Resolve(link with { Label = "架空リンク" }, "49", 2033));
        Assert.Equal("49", (new UserPreferences { SyllabusDepartmentId = "49", DefaultSchoolYear = "2032" }).Validated().SyllabusDepartmentId);
        Assert.Throws<InvalidDataException>(() => (new UserPreferences { SyllabusDepartmentId = "unknown" }).Validated());
    }
    [Theory]
    [InlineData("ｼｺｸ", "しこく")]
    [InlineData("ＳＨＩＫＯＫＵ", "sikoku")]
    [InlineData("A・B C", "abc")]
    public void NormalizesKanaLatinAndPunctuation(string source, string expected) => Assert.Equal(expected, LinkSearch.Normalize(source));
    [Theory]
    [InlineData("しこく", "shikoku")]
    [InlineData("しこく", "sikoku")]
    [InlineData("きっぷ", "kippu")]
    [InlineData("スー", "su")]
    [InlineData("ちゅうがく", "chugaku")]
    [InlineData("ABC", "abc")]
    public void MatchesRomajiQueries(string source, string query) => Assert.True(LinkSearch.Score(source, query) >= 0);
    [Theory]
    [InlineData("https://example.invalid/fake", true)]
    [InlineData("jrshikoku:fake", true)]
    [InlineData("http://example.invalid/fake", false)]
    [InlineData("https://fake@example.invalid/fake", false)]
    [InlineData("javascript:fake", false)]
    [InlineData("https://example.invalid/fake path", false)]
    public void AcceptsOnlySupportedLinkTargets(string value, bool expected) => Assert.Equal(expected, LinksPayload.ValidUrl(value));
}
