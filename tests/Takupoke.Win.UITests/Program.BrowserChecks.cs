using System.Windows.Automation;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static void CheckBrowser(string executable, string root)
    {
        Console.WriteLine("Browser fixture stage: create certificates");
        using var certificates = new BrowserFixtureCertificates();
        Console.WriteLine("Browser fixture stage: start owned HTTP/TLS servers");
        using var http = new BrowserFixtureServer();
        using var https = new BrowserFixtureServer(certificates.Trusted);
        using var mismatched = new BrowserFixtureServer(certificates.Mismatched);
        using var untrusted = new BrowserFixtureServer(certificates.Untrusted);
        SeedAsync(root).GetAwaiter().GetResult();
        SeedBrowserLinksAsync(root, [http.Address, new UriBuilder(http.Address) { Host = "localhost" }.Uri,
            https.Address, mismatched.Address, untrusted.Address]).GetAwaiter().GetResult();
        Console.WriteLine("Browser fixture stage: launch app with fictional local links");
        Start(executable); Navigate("links");
        AutomationElement[] Pages() => AutomationElement.RootElement.FindAll(TreeScope.Children,
            new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, _process!.Id),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)))
            .Cast<AutomationElement>().Where(e => e.Current.Name.StartsWith("架空ブラウザ", StringComparison.Ordinal)).ToArray();
        AutomationElement Open(int number)
        {
            Console.WriteLine("Browser fixture stage: open real link " + number);
            Invoke("link-browser-" + number);
            AutomationElement? page = null; Wait(() => (page = Pages().SingleOrDefault()) is not null, "The real loopback browser window opens"); return page!;
        }
        AutomationElement? Text(AutomationElement page, string name) => page.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name));
        void Close(AutomationElement page) => ((WindowPattern)page.GetCurrentPattern(WindowPattern.Pattern)).Close();
        var first = Open(0);
        Wait(() => Text(first, "架空ローカルページ到達") is not null, "Actual HTTP loopback response appears inside WebView2");
        foreach (var (label, heading) in new[] { ("架空新規リンク", "架空新規ページ到達"), ("架空空白から開く", "架空空白からの遷移到達") })
        {
            Console.WriteLine("Browser fixture stage: popup " + label);
            var button = Text(first, label); Require(button is not null, "The served popup control is present");
            Invoke(button!);
            AutomationElement? child = null;
            Wait(() => (child = Pages().FirstOrDefault(p => !p.GetRuntimeId().SequenceEqual(first.GetRuntimeId()) && Text(p, heading) is not null)) is not null,
                "The real popup retains its new-window context and reaches the served page");
            Close(child!); Wait(() => Pages().Length == 1, "Closing the popup retains its opener");
        }
        Invoke(Text(first, "架空外部HTTP拒否")!);
        Wait(() => Text(first, "BlockedNavigation") is not null, "Disallowed external HTTP navigation reports a reason");
        Close(first); Wait(() => Pages().Length == 0, "The first local browser closes");
        var named = Open(1); Wait(() => Text(named, "架空ローカルページ到達") is not null, "Localhost HTTP reaches the same real test server"); Close(named);
        Wait(() => Pages().Length == 0, "The localhost browser closes");
        var trusted = Open(2); Wait(() => Text(trusted, "架空ローカルページ到達") is not null, "A valid trusted HTTPS certificate renders normally"); Close(trusted);
        Wait(() => Pages().Length == 0, "The valid HTTPS browser closes");
        foreach (var index in new[] { 3, 4 })
        {
            var invalid = Open(index);
            Wait(() => invalid.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "browser-error-code"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))?.Current.Name.Contains("Certificate", StringComparison.Ordinal) == true,
                "An invalid HTTPS certificate is rejected with its actual WebView2 error code");
            Require(Text(invalid, "架空ローカルページ到達") is null, "Certificate errors never bypass verification or render the protected page");
            Close(invalid); Wait(() => Pages().Length == 0, "The rejected HTTPS browser closes");
        }
        Require(http.Requests > 0 && https.Requests > 0 && mismatched.Requests == 0 && untrusted.Requests == 0,
            "HTTP and trusted HTTPS reach the server; invalid TLS stops before any HTTP request");
        Console.WriteLine($"Passed {_checks} real WebView2 browser checks: HTTP, localhost, trusted TLS, rejected TLS and new-window contexts.");
    }
    private static async Task SeedBrowserLinksAsync(string root, Uri[] addresses)
    {
        await using var school = new SchoolDataStore(root, new WindowsDpapiProtector()); var lease = await school.BeginAsync();
        var links = addresses.Select((uri, i) => new LinkItem("browser-" + i, "fictional-browser", "架空ブラウザ" + i,
            uri.AbsoluteUri, "blue", true, i, false, 0, [], "架空ブラウザ")).ToArray();
        var payload = new LinksPayload("v1", "sha256-" + new string('b', 64), [new("fictional-browser", "架空ブラウザ", 0, links)]).Validated();
        await school.WriteAsync(lease, "api.links", new SavedLinks(payload, "\"fictional-browser\"", DateTimeOffset.UtcNow, new string('B', 43)));
        var preferences = new PreferencesStore(root);
        await preferences.SaveAsync((await preferences.LoadAsync()) with { OpeningMode = LinkOpeningMode.InApp, SetupCompleted = true });
    }
}
