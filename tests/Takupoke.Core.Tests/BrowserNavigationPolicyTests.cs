using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class BrowserNavigationPolicyTests
{
    [Theory]
    [InlineData("http://localhost:3000/", true)]
    [InlineData("http://127.0.0.1:1234/", true)]
    [InlineData("http://[::1]:1234/", true)]
    [InlineData("http://[::ffff:127.0.0.1]/", true)]
    [InlineData("http://192.168.10.2/", true)]
    [InlineData("http://10.1.2.3/", true)]
    [InlineData("http://172.31.1.2/", true)]
    [InlineData("http://[fd12::1]/", true)]
    [InlineData("http://invented.local/", true)]
    [InlineData("http://invented.home.arpa/", true)]
    [InlineData("http://invented-host/", false)]
    [InlineData("http://ai/", false)]
    [InlineData("https://example.invalid/", true)]
    [InlineData("https://192.168.1.1/", true)]
    [InlineData("http://example.invalid/", false)]
    [InlineData("http://172.32.1.2/", false)]
    [InlineData("http://0.0.0.0/", false)]
    [InlineData("http://8.8.8.8/", false)]
    [InlineData("http://user:secret@localhost/", false)]
    [InlineData("https://user:secret@example.invalid/", false)]
    [InlineData("http://localhost.example.invalid/", false)]
    [InlineData("http://localhost/with space", false)]
    [InlineData("file:///tmp/fictional.html", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,fictional", false)]
    [InlineData("about:blank", false)]
    public void DownloadedLinksOnlyAcceptHttpsOrLocalHttp(string value, bool accepted)
    {
        Assert.Equal(accepted, BrowserNavigationPolicy.IsWebUrl(value));
        Assert.Equal(accepted, LinksPayload.ValidUrl(value));
    }
    [Fact]
    public void RemotePagesCannotRedirectIntoLocalNetworkAndLocalPagesStayOnTheirHost()
    {
        var remote = new Uri("https://example.invalid/"); var local = new Uri("http://127.0.0.1:1234/");
        Assert.True(BrowserNavigationPolicy.MayNavigate(local, null, local.AbsoluteUri));
        Assert.True(BrowserNavigationPolicy.MayNavigate(local, local, "https://127.0.0.1:4567/"));
        Assert.True(BrowserNavigationPolicy.MayNavigate(local, local, remote.AbsoluteUri));
        Assert.False(BrowserNavigationPolicy.MayNavigate(remote, remote, local.AbsoluteUri));
        Assert.False(BrowserNavigationPolicy.MayNavigate(local, remote, local.AbsoluteUri));
        Assert.False(BrowserNavigationPolicy.MayNavigate(local, local, "http://192.168.1.2/"));
        Assert.False(BrowserNavigationPolicy.MayNavigate(local, local, "javascript:alert(1)"));
    }
    [Theory]
    [InlineData("HostNameNotResolved", "名前")]
    [InlineData("CannotConnect", "ポート")]
    [InlineData("CertificateCommonNameIsIncorrect", "名前")]
    [InlineData("CertificateExpired", "有効期限")]
    [InlineData("CertificateIsInvalid", "省略しません")]
    [InlineData("Unknown", "表示できません")]
    public void FailureMessagesDistinguishDnsConnectionsAndCertificates(string error, string fragment)
        => Assert.Contains(fragment, BrowserNavigationPolicy.NavigationFailure(error));
}
