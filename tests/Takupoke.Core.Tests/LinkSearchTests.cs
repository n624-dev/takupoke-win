using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class LinkSearchTests
{
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
