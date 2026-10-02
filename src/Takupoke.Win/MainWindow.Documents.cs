using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Takupoke.Core;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private Window? _browserWindow;
    private WebView2? _browser;
    private ContentDialog? _pdfDialog;
    private Image? _pdfImage;
    private async Task OpenBrowser(Uri uri, string title)
    {
        if (_model.OfflineTest || _model.Locked) return;
        var epoch = _model.PrivateEpoch;
        CloseBrowser();
        var browser = new WebView2(); var window = new Window { Title = title + " — たくポケ Win" };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(8) };
        controls.Children.Add(Button("戻る", () => { if (browser.CanGoBack) browser.GoBack(); return Task.CompletedTask; }));
        controls.Children.Add(Button("進む", () => { if (browser.CanGoForward) browser.GoForward(); return Task.CompletedTask; }));
        controls.Children.Add(Button("再読み込み", () => { browser.Reload(); return Task.CompletedTask; }));
        controls.Children.Add(Button("外部ブラウザで開く", async () => { if (browser.Source is { } current && current.Scheme == "https") await Windows.System.Launcher.LaunchUriAsync(current); }));
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(controls); Grid.SetRow(browser, 1); root.Children.Add(browser);
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
            browser.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
            browser.CoreWebView2.NavigationStarting += (_, args) =>
            { if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) || destination.Scheme != "https") args.Cancel = true; };
            browser.CoreWebView2.NewWindowRequested += (_, args) =>
            { args.Handled = true; if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) && destination.Scheme == "https") browser.Source = destination; };
            browser.Source = uri;
        }
        catch { CloseBrowser(); await Message("アプリ内ブラウザを開けません", "WebView2の実行環境を確認してください。設定から外部ブラウザを選ぶこともできます。"); }
    }
    private void CloseBrowser()
    {
        var browser = _browser; var window = _browserWindow; _browser = null; _browserWindow = null;
        browser?.Close(); window?.Close();
    }
    private async Task ShowPdf(MaterialKind kind, bool accepted)
    {
        var epoch = _model.PrivateEpoch;
        var bytes = await _model.ReadPdfAsync(kind, accepted);
        using var source = new InMemoryRandomAccessStream();
        try
        {
            using (var writer = new DataWriter(source)) { writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        source.Seek(0);
        var document = await PdfDocument.LoadFromStreamAsync(source);
        if (epoch != _model.PrivateEpoch || _model.Locked) return;
        if (document.PageCount is < 1 or > 12) { await Message("PDFを表示できません", "ページ数が対応範囲外です。"); return; }
        var image = new Image(); AutomationProperties.SetName(image, "保存した資料のPDFページ。読み取った内容は解析結果から確認できます。");
        _pdfImage = image;
        var label = Text(""); var pageNumber = 0u; var zoom = new Slider { Header = "表示幅", Minimum = 300, Maximum = 1600, Value = 700 };
        var rendering = false; var pending = false;
        async Task RenderPage()
        {
            if (epoch != _model.PrivateEpoch || _model.Locked) return;
            pending = true; if (rendering) return; rendering = true;
            try
            {
                while (pending && epoch == _model.PrivateEpoch && !_model.Locked)
                {
                pending = false; var requestedPage = pageNumber; var requestedWidth = (uint)zoom.Value;
                using var page = document.GetPage(requestedPage); using var rendered = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions { DestinationWidth = requestedWidth });
                rendered.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(rendered);
                if (epoch != _model.PrivateEpoch || _model.Locked) return;
                if (requestedPage != pageNumber || requestedWidth != (uint)zoom.Value) { pending = true; continue; }
                image.Source = bitmap;
                label.Text = $"{requestedPage + 1} / {document.PageCount}ページ";
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
        finally { image.Source = null; _pdfImage = null; _pdfDialog = null; _dialogOpen = false; Render(); }
    }
}
