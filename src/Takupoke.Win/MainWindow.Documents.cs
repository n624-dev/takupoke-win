using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Takupoke.Core;
using Takupoke.Infrastructure.Storage;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private Window? _browserWindow;
    private WebView2? _browser;
    private ContentDialog? _pdfDialog;
    private bool _pdfOpening;
    private Image? _pdfImage;
    private async Task OpenBrowser(Uri uri, string title)
    {
        if (_model.OfflineTest || _model.Locked) return;
        var epoch = _model.PrivateEpoch;
        CloseBrowser();
        var browser = new WebView2(); var window = new Window { Title = title + " — たくポケ" };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(16, 12, 16, 12) };
        var back = IconButton("戻る", "back", () => { if (browser.CanGoBack) browser.GoBack(); return Task.CompletedTask; });
        var forward = IconButton("進む", "forward", () => { if (browser.CanGoForward) browser.GoForward(); return Task.CompletedTask; });
        var reload = IconButton("再読み込み", "refresh", () => { browser.Reload(); return Task.CompletedTask; });
        var external = IconButton("既定のブラウザで開く", "open", async () => { if (browser.Source is { } current && current.Scheme == "https") await Windows.System.Launcher.LaunchUriAsync(current); }, "外部ブラウザで開く");
        back.IsEnabled = forward.IsEnabled = reload.IsEnabled = external.IsEnabled = false;
        controls.Children.Add(back); controls.Children.Add(forward); controls.Children.Add(reload); controls.Children.Add(external);
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new ScrollViewer { Content = controls, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(browser, 1); root.Children.Add(browser);
        window.Content = root; _browserWindow = window; _browser = browser;
        window.Closed += (_, _) => { browser.Close(); if (_browser == browser) { _browser = null; _browserWindow = null; } };
        window.Activate();
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(_model.Root, "school", "browser"), null);
            var options = environment.CreateCoreWebView2ControllerOptions(); options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options);
            if (epoch != _model.PrivateEpoch || _model.Locked || _browser != browser) { browser.Close(); return; }
            browser.CoreWebView2.Settings.IsWebMessageEnabled = false; browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            reload.IsEnabled = external.IsEnabled = true;
            browser.CoreWebView2.HistoryChanged += (_, _) => { back.IsEnabled = browser.CanGoBack; forward.IsEnabled = browser.CanGoForward; };
            browser.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
            browser.CoreWebView2.NavigationStarting += (_, args) =>
            { if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) || destination.Scheme != "https") args.Cancel = true; };
            browser.CoreWebView2.NewWindowRequested += (_, args) =>
            { args.Handled = true; if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) && destination.Scheme == "https") browser.Source = destination; };
            browser.Source = uri;
        }
        catch { if (_browser != browser || epoch != _model.PrivateEpoch || _model.Locked) { browser.Close(); return; } CloseBrowser(); await Message("アプリ内ブラウザを開けませんでした", "設定の「リンクの開き方」を「既定のブラウザ」に変更して、もう一度リンクを開いてください。"); }
    }
    private void CloseBrowser()
    {
        var browser = _browser; var window = _browserWindow; _browser = null; _browserWindow = null;
        browser?.Close(); window?.Close();
    }
    private async Task ShowPdf(MaterialKind kind, bool accepted, RecoveryPreview? preview = null, RecoveryManualSession? manual = null)
    {
        // Reserve before the first await. A second click must never own or clear
        // the first viewer's privacy references.
        if (_pdfOpening || _dialogOpen) return;
        _pdfOpening = true;
        try { await ShowPdfCore(kind, accepted, preview, manual); }
        finally { _pdfOpening = false; }
    }
    private async Task ShowPdfCore(MaterialKind kind, bool accepted, RecoveryPreview? preview, RecoveryManualSession? manual)
    {
        var epoch = _model.PrivateEpoch;
        var bytes = manual is not null ? await _model.ReadManualPdfAsync(kind, manual) : preview is null ? await _model.ReadPdfAsync(kind, accepted) : await _model.ReadRecoveryPdfAsync(kind, preview);
        using var source = new InMemoryRandomAccessStream();
        try
        {
            using (var writer = new DataWriter(source)) { writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        source.Seek(0);
        var document = await PdfDocument.LoadFromStreamAsync(source);
        if (epoch != _model.PrivateEpoch || _model.Locked || _dialogOpen) return;
        if (document.PageCount is < 1 or > 12) { await Message("PDFを表示できません", "ページ数が対応範囲外です。"); return; }
        var image = new Image(); AutomationProperties.SetName(image, "保存した資料のPDFページ。読み取った内容は解析結果から確認できます。");
        _pdfImage = image;
        var label = Text(""); var pageNumber = 0u; var zoom = new Slider { Header = "表示幅", Minimum = 300, Maximum = 1600, Value = 700 };
        var rendering = false; var pending = false; var closed = false;
        async Task RenderPage()
        {
            if (closed || epoch != _model.PrivateEpoch || _model.Locked) return;
            pending = true; if (rendering) return; rendering = true;
            try
            {
                while (pending && !closed && epoch == _model.PrivateEpoch && !_model.Locked)
                {
                    pending = false; var requestedPage = pageNumber; var requestedWidth = (uint)zoom.Value;
                    try
                    {
                        using var page = document.GetPage(requestedPage); using var rendered = new InMemoryRandomAccessStream();
                        await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions { DestinationWidth = requestedWidth });
                        rendered.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(rendered);
                        if (closed || epoch != _model.PrivateEpoch || _model.Locked) return;
                        if (requestedPage != pageNumber || requestedWidth != (uint)zoom.Value) { pending = true; continue; }
                        image.Source = bitmap;
                        label.Text = $"{requestedPage + 1} / {document.PageCount}ページ";
                    }
                    catch
                    {
                        if (closed || epoch != _model.PrivateEpoch || _model.Locked) return;
                        // Failure of an obsolete request must not erase a queued
                        // request for a different page or width.
                        if (requestedPage != pageNumber || requestedWidth != (uint)zoom.Value) { pending = true; continue; }
                        pending = false;
                        image.Source = null;
                        label.Text = "このページを表示できませんでした。別のページを選ぶか、PDFを開き直してください。";
                    }
                }
            }
            finally { rendering = false; }
        }
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(Button("前のページ", async () => { if (pageNumber > 0) { pageNumber--; await RenderPage(); } })); controls.Children.Add(label);
        controls.Children.Add(Button("次のページ", async () => { if (pageNumber + 1 < document.PageCount) { pageNumber++; await RenderPage(); } }));
        zoom.ValueChanged += async (_, _) => await RenderPage();
        _dialogOpen = true;
        try
        {
            await RenderPage();
            if (epoch != _model.PrivateEpoch || _model.Locked) return;
            _pdfDialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "保存したPDF", CloseButtonText = "閉じる",
                Content = Panel(controls, zoom, new ScrollViewer { Content = image, MaxHeight = 450, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }) };
            await _pdfDialog.ShowAsync();
        }
        finally { closed = true; pending = false; image.Source = null; _pdfImage = null; _pdfDialog = null; _dialogOpen = false; Render(); }
    }
}
