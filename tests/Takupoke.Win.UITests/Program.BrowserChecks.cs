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
        var namedAddress = new UriBuilder(http.Address) { Host = "localhost" }.Uri;
        SeedAsync(root).GetAwaiter().GetResult();
        SeedBrowserLinksAsync(root, [http.Address, namedAddress, https.Address, mismatched.Address, untrusted.Address]).GetAwaiter().GetResult();
        var reservation = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        reservation.Start();
        var debugPort = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        Console.WriteLine("Browser fixture stage: launch app with fictional local links");
        using var debugPolicy = new BrowserFixtureDebugPolicy(executable, debugPort);
        Start(executable); Navigate("links");
        AutomationElement[] Pages() => AutomationElement.RootElement.FindAll(TreeScope.Children,
            new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, _process!.Id),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)))
            .Cast<AutomationElement>().Where(e => e.Current.Name.StartsWith("架空ブラウザ", StringComparison.Ordinal)).ToArray();
        AutomationElement Open(int number)
        {
            Console.WriteLine("Browser fixture stage: open real link " + number);
            Invoke("link-browser-" + number);
            AutomationElement? page = null;
            Wait(() => (page = Pages().SingleOrDefault()) is not null, "The real loopback browser window opens");
            Wait(() => page!.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "再読み込み"))?.Current.IsEnabled == true,
                "The actual WebView2 controller is initialized");
            return page!;
        }
        AutomationElement? Text(AutomationElement page, string name) => page.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name));
        void Close(AutomationElement page) => ((WindowPattern)page.GetCurrentPattern(WindowPattern.Pattern)).Close();
        using var playwright = Microsoft.Playwright.Playwright.CreateAsync().GetAwaiter().GetResult();
        Microsoft.Playwright.IBrowser? browser = null;
        void Attach()
        {
            if (browser?.IsConnected != true)
            {
                Console.WriteLine("Browser fixture stage: attach to the actual WebView2 controller");
                browser = playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{debugPort}", new() { Timeout = 30000 }).GetAwaiter().GetResult();
                Console.WriteLine("Browser fixture stage: actual WebView2 controller attached");
            }
        }
        Microsoft.Playwright.IPage Content(Uri address, string heading)
        {
            Microsoft.Playwright.IPage? page = null;
            Wait(() =>
            {
                page = browser!.Contexts.SelectMany(context => context.Pages).SingleOrDefault(candidate => candidate.Url == address.AbsoluteUri);
                return page is not null && page.GetByRole(Microsoft.Playwright.AriaRole.Heading,
                    new() { Name = heading, Exact = true }).IsVisibleAsync().GetAwaiter().GetResult();
            }, "The actual rendered WebView2 page has its visible served heading");
            return page!;
        }
        void Click(Microsoft.Playwright.IPage page, string label) => page.GetByText(label, new() { Exact = true })
            .ClickAsync(new() { Timeout = 30000 }).GetAwaiter().GetResult();
        try
        {
            var first = Open(0);
            Wait(() => Text(first, "ページを表示しました。") is not null, "The native WebView2 navigation completes before attaching the test client");
            Attach();
            var opener = Content(http.Address, "架空ローカルページ到達");
            foreach (var (label, path, heading) in new[] { ("架空新規リンク", "target", "架空新規ページ到達"), ("架空空白から開く", "afterblank", "架空空白からの遷移到達") })
            {
                Console.WriteLine("Browser fixture stage: popup " + label);
                Click(opener, label);
                AutomationElement? child = null;
                Wait(() => (child = Pages().SingleOrDefault(page => !page.GetRuntimeId().SequenceEqual(first.GetRuntimeId()))) is not null,
                    "A distinct native popup window opens from the actual page control");
                var popup = Content(new Uri(http.Address, path), heading);
                Require(!ReferenceEquals(popup, opener) && opener.Url == http.Address.AbsoluteUri,
                    "The popup has a distinct real page and preserves its opener address");
                Require(opener.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "架空ローカルページ到達", Exact = true })
                    .IsVisibleAsync().GetAwaiter().GetResult(), "The opener's actual original content remains visible");
                Close(child!);
                Wait(() => Pages().Length == 1 && popup.IsClosed && !opener.IsClosed,
                    "Closing the actual native popup closes its page while retaining its opener");
            }
            var external = first.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "外部ブラウザで開く"));
            Require(external is not null && external.Current.IsEnabled, "The native external-browser control accepts the local HTTP destination");
            Require(external!.TryGetCurrentPattern(InvokePattern.Pattern, out var externalPattern),
                "The actual browser-window control exposes native InvokePattern");
            ((InvokePattern)externalPattern!).Invoke();
            Wait(() => Text(first, "OfflineExternalLaunchBlocked") is not null,
                "The real external-browser operation reaches the offline launch guard without starting an external process");
            Click(opener, "架空外部HTTP拒否");
            Wait(() => Text(first, "BlockedNavigation") is not null, "Disallowed external HTTP navigation reports a reason");
            Require(opener.Url == http.Address.AbsoluteUri, "A rejected destination cannot replace the rendered opener");
            Close(first); Wait(() => Pages().Length == 0 && opener.IsClosed, "The first local browser and actual page close");
            var named = Open(1); Attach(); var namedPage = Content(namedAddress, "架空ローカルページ到達"); Close(named);
            Wait(() => Pages().Length == 0 && namedPage.IsClosed, "The localhost browser and actual page close");
            var trusted = Open(2); Attach(); var trustedPage = Content(https.Address, "架空ローカルページ到達"); Close(trusted);
            Wait(() => Pages().Length == 0 && trustedPage.IsClosed, "The valid HTTPS browser and actual page close");
            foreach (var index in new[] { 3, 4 })
            {
                var invalid = Open(index); Attach();
                Wait(() => invalid.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "browser-error-code"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))?.Current.Name.Contains("Certificate", StringComparison.Ordinal) == true,
                    "An invalid HTTPS certificate is rejected with its actual WebView2 error code");
                Require(!browser!.Contexts.SelectMany(context => context.Pages).Any(page => page.GetByRole(Microsoft.Playwright.AriaRole.Heading,
                    new() { Name = "架空ローカルページ到達", Exact = true }).CountAsync().GetAwaiter().GetResult() > 0),
                    "Certificate errors never bypass verification or render the protected page");
                Close(invalid); Wait(() => Pages().Length == 0, "The rejected HTTPS browser closes");
            }
            Require(http.Requests > 0 && https.Requests > 0 && mismatched.Requests == 0 && untrusted.Requests == 0,
                "HTTP and trusted HTTPS reach the server; invalid TLS stops before any HTTP request");
            Console.WriteLine($"Passed {_checks} real WebView2 browser checks: HTTP, localhost, trusted TLS, rejected TLS and new-window contexts.");
        }
        catch
        {
            Console.Error.WriteLine($"Synthetic browser requests: http={http.Requests};https={https.Requests};mismatched={mismatched.Requests};untrusted={untrusted.Requests}");
            try
            {
                Console.Error.WriteLine($"Synthetic CDP state: connected={browser?.IsConnected == true};contexts={browser?.Contexts.Count ?? 0};pages={browser?.Contexts.Sum(context => context.Pages.Count) ?? 0}");
                foreach (var page in Pages())
                {
                    Console.Error.WriteLine("Synthetic browser window: " + page.Current.BoundingRectangle);
                    foreach (var element in page.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().Take(60))
                    {
                        var current = element.Current;
                        var name = current.Name.Replace('\r', ' ').Replace('\n', ' ');
                        if (name.Length > 300) name = name[..300];
                        Console.Error.WriteLine($"Synthetic browser element: type={current.ControlType.ProgrammaticName};id={current.AutomationId};name={name};offscreen={current.IsOffscreen};bounds={current.BoundingRectangle}");
                    }
                }
            }
            catch (Exception diagnosticError) { Console.Error.WriteLine("Synthetic browser diagnostic unavailable: " + diagnosticError.GetType().Name); }
            throw;
        }
        finally { if (browser?.IsConnected == true) browser.CloseAsync().GetAwaiter().GetResult(); }
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
