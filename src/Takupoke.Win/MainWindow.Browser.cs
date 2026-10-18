using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Takupoke.Core;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private readonly Dictionary<Window, WebView2> _browserWindows = [];

    private async Task OpenBrowser(Uri uri, string title)
    {
        if (_model.Locked || _model.OfflineTest && !uri.IsLoopback) return;
        if (!BrowserNavigationPolicy.IsWebUrl(uri.AbsoluteUri)) { await Message("リンクを開けません", "許可されていない接続先です。"); return; }
        var epoch = _model.PrivateEpoch;
        CloseBrowser();
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(_model.Root, "school", "browser"), null);
            if (epoch != _model.PrivateEpoch || _model.Locked) return;
            await CreateBrowserPage(environment, uri, title, epoch, navigate: true);
        }
        catch { if (epoch == _model.PrivateEpoch && !_model.Locked) await Message("アプリ内ブラウザを開けませんでした", "WebView2 Runtimeとネットワークを確認するか、設定の「リンクの開き方」を「既定のブラウザ」に変更してください。"); }
    }

    private async Task<CoreWebView2?> CreateBrowserPage(CoreWebView2Environment environment, Uri opened, string title, long epoch, bool navigate)
    {
        var browser = new WebView2(); var window = new Window { Title = title + " — たくポケ" };
        var requestContext = opened; Uri? externalAddress = null; var allowInitialBlank = !navigate;
        var status = Text("ページを読み込んでいます⋯"); status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetAutomationId(status, "browser-status");
        var code = Text(""); code.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetAutomationId(code, "browser-error-code");
        void Failure(string message, string error) { status.Text = message; code.Text = error; }
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(16, 12, 16, 12) };
        var back = IconButton("戻る", "back", () => { if (browser.CanGoBack) browser.GoBack(); return Task.CompletedTask; });
        var forward = IconButton("進む", "forward", () => { if (browser.CanGoForward) browser.GoForward(); return Task.CompletedTask; });
        var reload = IconButton("再読み込み", "refresh", () => { browser.Reload(); return Task.CompletedTask; });
        var external = IconButton("既定のブラウザで開く", "open", async () =>
        {
            if (epoch != _model.PrivateEpoch || _model.Locked || externalAddress is null) return;
            if (_model.OfflineTest) { Failure("テスト実行中は外部ブラウザを起動しません。", "OfflineExternalLaunchBlocked"); return; }
            if (!await Windows.System.Launcher.LaunchUriAsync(externalAddress)) Failure("既定のブラウザを起動できませんでした。", "ExternalLaunchFailed");
        }, "外部ブラウザで開く");
        back.IsEnabled = forward.IsEnabled = reload.IsEnabled = external.IsEnabled = false;
        controls.Children.Add(back); controls.Children.Add(forward); controls.Children.Add(reload); controls.Children.Add(external);
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new ScrollViewer { Content = controls, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var messages = new StackPanel { Padding = new Thickness(16, 4, 16, 8), Spacing = 4 };
        messages.Children.Add(status); messages.Children.Add(code); Grid.SetRow(messages, 1); root.Children.Add(messages);
        Grid.SetRow(browser, 2); root.Children.Add(browser);
        window.Content = root; _browserWindows.Add(window, browser);
        bool Current() => _browserWindows.ContainsKey(window) && epoch == _model.PrivateEpoch && !_model.Locked;
        window.Closed += (_, _) => { _browserWindows.Remove(window); browser.Close(); };
        window.Activate();
        try
        {
            var options = environment.CreateCoreWebView2ControllerOptions(); options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options);
            if (!Current()) { browser.Close(); return null; }
            var core = browser.CoreWebView2;
            core.Settings.IsWebMessageEnabled = false; core.Settings.AreHostObjectsAllowed = false;
            reload.IsEnabled = true;
            core.HistoryChanged += (_, _) => { back.IsEnabled = browser.CanGoBack; forward.IsEnabled = browser.CanGoForward; };
            core.DownloadStarting += (_, args) => { args.Cancel = true; Failure("アプリ内ブラウザではファイルを保存できません。既定のブラウザで開いてください。", "DownloadBlocked"); };
            core.NavigationStarting += (_, args) =>
            {
                if (allowInitialBlank && args.Uri == "about:blank") { allowInitialBlank = false; return; }
                if (!Current() || !BrowserNavigationPolicy.MayNavigate(opened, requestContext, args.Uri))
                { args.Cancel = true; Failure("この移動は許可されていません。HTTPはローカル接続先だけ利用できます。別のローカル機器は一覧から明示的に開いてください。", "BlockedNavigation"); return; }
                allowInitialBlank = false; requestContext = new Uri(args.Uri); externalAddress = requestContext; external.IsEnabled = true;
                status.Text = "ページを読み込んでいます⋯"; code.Text = "";
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (!Current()) return;
                if (!args.IsSuccess)
                { if (code.Text != "BlockedNavigation" && !code.Text.StartsWith("WebView2: Certificate", StringComparison.Ordinal)) Failure(BrowserNavigationPolicy.NavigationFailure(args.WebErrorStatus.ToString()), "WebView2: " + args.WebErrorStatus); return; }
                if (core.Source == "about:blank") { status.Text = "新しいページの読み込みを待っています。"; return; }
                if (Uri.TryCreate(core.Source, UriKind.Absolute, out var current)) requestContext = current;
                if (args.HttpStatusCode >= 400) Failure("接続先がエラーを返しました。権限や接続先の状態を確認してください。", "HTTP: " + args.HttpStatusCode);
                else { status.Text = "ページを表示しました。"; code.Text = ""; }
            };
            core.ServerCertificateErrorDetected += (_, args) =>
            {
                args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
                if (Current()) Failure(BrowserNavigationPolicy.NavigationFailure(args.ErrorStatus.ToString()), "WebView2: " + args.ErrorStatus);
            };
            core.ProcessFailed += (_, args) => { if (Current()) Failure("ブラウザの処理が停止しました。ページを開き直してください。", "WebView2: " + args.ProcessFailedKind); };
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                // Offline CI never contacts a school or any external site.
                if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var resource) || resource.Scheme is not ("http" or "https")) return;
                if (!Current() || _model.OfflineTest && !resource.IsLoopback || !BrowserNavigationPolicy.MayNavigate(opened, requestContext, resource.AbsoluteUri))
                    args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked by application policy", "Content-Type: text/plain");
            };
            core.NewWindowRequested += async (_, args) =>
            {
                args.Handled = true;
                var blank = args.Uri == "about:blank";
                if (!Current() || _browserWindows.Count >= 4 || (!blank && !BrowserNavigationPolicy.MayNavigate(opened, requestContext, args.Uri)))
                { Failure("新しいページを開けませんでした。接続先の形式や、開いているページ数を確認してください。", "PopupBlocked"); return; }
                var deferral = args.GetDeferral();
                try
                {
                    var opener = requestContext;
                    var child = await CreateBrowserPage(environment, opener, title, epoch, navigate: false);
                    if (Current() && child is not null) args.NewWindow = child;
                }
                catch { if (Current()) Failure("新しいページのブラウザを初期化できませんでした。", "PopupInitializationFailed"); }
                finally { deferral.Complete(); }
            };
            if (navigate) browser.Source = opened;
            return core;
        }
        catch { if (Current()) Failure("WebView2を初期化できませんでした。Runtimeを確認し、ページを開き直してください。", "BrowserInitializationFailed"); return null; }
    }

    private void CloseBrowser()
    {
        var pages = _browserWindows.ToArray(); _browserWindows.Clear();
        foreach (var (window, browser) in pages) { browser.Close(); window.Close(); }
    }
}
