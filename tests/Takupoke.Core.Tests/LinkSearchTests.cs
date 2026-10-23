using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class LinkSearchTests
{
    [Fact]
    public void LateSearchTermsRetainMatchesAndMatchKindPriority()
    {
        var terms = string.Join('|', Enumerable.Range(0, 334).Select(i => "あ" + i)) + "|finalmatch";
        var item = new LinkItem("fictional", "category", "架空リンク", "https://example.invalid/", "sky", true, 0, false, 0, [], terms);
        var payload = new LinksPayload("v1", "sha256-" + new string('a', 64), [new("category", "架空カテゴリ", 0, [item])]).Validated();
        Assert.True(terms.Length <= 5000);
        Assert.Single(payload.Search("finalmatch", new HashSet<string>()));
        var exact = LinkSearch.Score(terms, "finalmatch");
        var prefix = LinkSearch.Score(terms, "final");
        var substring = LinkSearch.Score(terms, "match");
        Assert.True(substring >= 0); Assert.True(exact > prefix); Assert.True(prefix > substring);
        Assert.Equal(-1, LinkSearch.Score(terms, "absent"));
        Assert.True(LinkSearch.Score("needle|needless", "needle") > LinkSearch.Score("another|needle", "needle"));
    }
    [Theory]
    [InlineData("くうこう", "kuko")]
    [InlineData("くうこう", "kuukou")]
    [InlineData("こおり", "kori")]
    public void LongVowelVariantsKeepTheirVowel(string terms, string query) => Assert.True(LinkSearch.Score(terms, query) >= 0);
    [Fact]
    public void LongUVowelNeverBecomesO() => Assert.Equal(-1, LinkSearch.Score("くうこう", "koko"));

    [Theory]
    [InlineData("ｼｺｸ", "しこく")]
    [InlineData("ＳＨＩＫＯＫＵ", "sikoku")]
    [InlineData("A・B C", "abc")]
    public void NormalizesKanaLatinAndPunctuation(string source, string expected) => Assert.Equal(expected, LinkSearch.Normalize(source));
    [Theory]
    [InlineData("しこく", "shikoku")]
    [InlineData("しこく", "sikoku")]
    [InlineData("きっぷ", "kippu")]
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
