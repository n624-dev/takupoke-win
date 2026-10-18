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
    private ContentDialog? _pdfDialog;
    private bool _pdfOpening;
    private Image? _pdfImage;
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
