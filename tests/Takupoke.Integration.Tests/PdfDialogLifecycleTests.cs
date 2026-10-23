using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PdfDialogLifecycleTests
{
    [Fact]
    public async Task ActualPdfDialogAndButtonMethodsKeepModalOwnershipDuringReadsAndFailures()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var documents = File.ReadAllText(Path.Combine(fixture, "document-methods.txt"));
        var window = File.ReadAllText(Path.Combine(fixture, "startup-window.txt"));
        var pdf = documents[documents.IndexOf("    private async Task ShowPdf(", StringComparison.Ordinal)..documents.LastIndexOf('}')];
        var dialog = window[window.IndexOf("    private async Task<ContentDialogResult> Dialog(", StringComparison.Ordinal)..window.IndexOf("    private async Task SelectMaterial(", StringComparison.Ordinal)];
        var button = window[window.IndexOf("    private Button Button(", StringComparison.Ordinal)..window.IndexOf("    private void Add(", StringComparison.Ordinal)];
        var owner = string.Join('\n', window.Split('\n').Where(line => line.TrimStart().StartsWith("private ", StringComparison.Ordinal) && (line.Contains(" _dialogOpen") || line.Contains(" _dialogOwner"))));
        await CSharpMethodProbe.AssertRunAsync(Host.Replace("PDF_METHOD", pdf).Replace("DIALOG_METHOD", dialog).Replace("BUTTON_METHOD", button).Replace("OWNER_FIELD", owner));
    }

    private const string Host = """
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
class Program
{
    static List<string> failures=new();
    static void Check(bool condition,string message){if(!condition)failures.Add(message);}
    static async Task Main()
    {
        Reset();var host=new MainWindow();var first=host.ClickPdf();var second=host.ClickPdf();
        Check(host.Model.Reads.Count==1,"PDF reads must be exclusive from entry");
        host.Model.Reads[0].SetResult(new byte[]{1,2,3});await ContentDialog.Started.Task;
        if(host.Model.Reads.Count>1)host.Model.Reads[1].SetResult(new byte[]{4,5,6});
        try{await second;}catch{failures.Add("Second PDF click leaked through the actual Button error handler");}
        Check(host.OwnsPdf && host.DialogOpen && ContentDialog.Active!=null,"Second completion must not erase the first PDF owner");
        ContentDialog.Active?.Hide();await first;Check(!host.OwnsPdf&&!host.DialogOpen,"Manual close releases the PDF owner");
        if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));

        Reset();host=new();var pending=host.Pdf();var other=host.OtherDialog();
        Check(other.IsCompletedSuccessfully && ContentDialog.ShowCalls==0,"Other modal must be refused while PDF bytes are pending");
        // The old implementation incorrectly opens the other dialog; close it to finish the failed control.
        ContentDialog.Active?.Hide();await other;host.Model.Reads[0].SetResult(new byte[]{1});
        await ContentDialog.Started.Task;ContentDialog.Active?.Hide();await pending;

        foreach(var reason in new[]{"epoch","lock"})
        {
            Reset();host=new();pending=host.Pdf();var bytes=new byte[]{1,2,3};
            if(reason=="epoch")host.Model.PrivateEpoch++;else host.Model.Locked=true;
            host.Model.Reads[0].SetResult(bytes);await pending;
            Check(ContentDialog.ShowCalls==0&&!host.DialogOpen&&!host.OwnsPdf,"Stale PDF must not display: "+reason);
            Check(Array.TrueForAll(bytes,x=>x==0),"Owned plaintext bytes must be zeroed: "+reason);
        }
        foreach(var phase in new[]{"read","load","render"})
        {
            Reset();host=new();PdfDocument.FailAt=phase;pending=host.Pdf();
            if(phase=="read")host.Model.Reads[0].SetException(new IOException("fictional read"));else host.Model.Reads[0].SetResult(new byte[]{1});
            try{await pending;failures.Add("Expected failure at "+phase);}catch(IOException){}
            Check(!host.DialogOpen&&!host.OwnsPdf,"Failure must release its own state: "+phase);
            ContentDialog.AutoClose=true;await host.OtherDialog();Check(ContentDialog.ShowCalls==1,"Failure must allow a later modal: "+phase);
        }
        Reset();host=new();host.Model.Locked=true;await host.Pdf();Check(host.Model.Reads.Count==0,"Locked entry must not read private PDF bytes");
        Reset();host=new();var dialog=host.OtherDialog();await ContentDialog.Started.Task;await host.Pdf();
        Check(host.Model.Reads.Count==0&&host.DialogOpen&&ContentDialog.Active!=null,"Existing modal must retain ownership when PDF is requested");
        ContentDialog.Active?.Hide();await dialog;Check(!host.DialogOpen,"Ordinary dialog releases its owner");
        Reset();host=new();PdfDocument.Pages=13;ContentDialog.AutoClose=true;pending=host.Pdf();host.Model.Reads[0].SetResult(new byte[]{1});await pending;
        Check(ContentDialog.ShowCalls==1&&!host.DialogOpen,"Invalid page count must report and release without nesting a modal");
        if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));
        Console.WriteLine("Actual PDF/Dialog/Button methods: duplicate entry, competing modal, close, read/load/render failure, lock, epoch and page limit passed.");
    }
    static void Reset(){ContentDialog.Reset();PdfDocument.FailAt="";PdfDocument.Pages=1;}
}
class Model {public bool Locked;public int PrivateEpoch;public List<TaskCompletionSource<byte[]>> Reads=new();public Task<byte[]> ReadPdfAsync(MaterialKind k,bool accepted){var t=new TaskCompletionSource<byte[]>();Reads.Add(t);return t.Task;} }
enum MaterialKind {Normal}
class UIElement {}
class Control:UIElement {}
class Image:UIElement {public object? Source;}
class TextBlock:UIElement {public string Text="";}
class Button:Control {public object? Content;public HorizontalAlignment HorizontalAlignment;public Thickness? Padding;public int MinHeight;public CornerRadius? CornerRadius;public event Func<object?,EventArgs,Task>? Click;public Task ClickAsync()=>Click?.Invoke(this,EventArgs.Empty)??Task.CompletedTask;}
enum HorizontalAlignment {Left}
class Thickness {public Thickness(params int[] v){} }
class CornerRadius {public CornerRadius(int v){} }
class Slider:UIElement {public string Header="";public int Minimum,Maximum;public double Value;public event EventHandler? ValueChanged;}
class StackPanel:UIElement {public Orientation Orientation;public int Spacing;public List<UIElement> Children=new();}
enum Orientation {Horizontal}
class ScrollViewer:UIElement {public object? Content;public int MaxHeight;public ScrollBarVisibility HorizontalScrollBarVisibility,VerticalScrollBarVisibility;public ScrollMode HorizontalScrollMode;}
enum ScrollBarVisibility {Auto,Disabled}
enum ScrollMode {Disabled}
class Border:UIElement {public UIElement? Child;public Thickness? Padding;}
class Root {public object XamlRoot=new();}
class ContentDialog
{
    public object? XamlRoot,Content;public string Title="",CloseButtonText="",PrimaryButtonText="";public ContentDialogButton DefaultButton;
    public static ContentDialog? Active;public static TaskCompletionSource Started=new();public static int ShowCalls;public static bool AutoClose;
    private TaskCompletionSource<ContentDialogResult> done=new();
    public Task<ContentDialogResult> ShowAsync(){ShowCalls++;if(Active!=null)throw new InvalidOperationException("One modal per window");Active=this;Started.TrySetResult();if(AutoClose)Hide();return done.Task;}
    public void Hide(){if(ReferenceEquals(Active,this))Active=null;done.TrySetResult(ContentDialogResult.None);}
    public static void Reset(){if(Active!=null)throw new Exception("Leaked dialog");Started=new();ShowCalls=0;AutoClose=false;}
}
enum ContentDialogButton {Primary}
enum ContentDialogResult {None,Primary}
static class AutomationProperties {public static void SetName(object o,string v){}public static void SetAutomationId(object o,string v){} }
class InMemoryRandomAccessStream:IDisposable {public void Seek(int n){}public void Dispose(){} }
class DataWriter:IDisposable {public DataWriter(object s){}public void WriteBytes(byte[] b){}public Task StoreAsync()=>Task.CompletedTask;public void DetachStream(){}public void Dispose(){} }
class BitmapImage {public Task SetSourceAsync(object o)=>Task.CompletedTask;}
class PdfDocument {public static string FailAt="";public static uint Pages=1;public uint PageCount=>Pages;public static Task<PdfDocument> LoadFromStreamAsync(object stream){if(FailAt=="load")throw new IOException("fictional load");return Task.FromResult(new PdfDocument());}public PdfPage GetPage(uint n)=>new();}
class PdfPage:IDisposable {public Task RenderToStreamAsync(object stream,PdfPageRenderOptions options){if(PdfDocument.FailAt=="render")throw new IOException("fictional render");return Task.CompletedTask;}public void Dispose(){} }
class PdfPageRenderOptions {public uint DestinationWidth;}
class MainWindow
{
    private Model _model=new();public Model Model=>_model;private Root RootGrid=new();private ContentDialog? _pdfDialog,_activeDialog;private Image? _pdfImage;
OWNER_FIELD
    public bool DialogOpen=>_dialogOpen;public bool OwnsPdf=>_pdfDialog!=null&&_pdfImage!=null;
    public Task Pdf()=>ShowPdf(MaterialKind.Normal,true);public Task ClickPdf()=>Button("PDF",Pdf).ClickAsync();public Task OtherDialog()=>Dialog("Fictional",Text("fictional"));
    private TextBlock Text(string text)=>new(){Text=text};private UIElement Panel(params UIElement[] items)=>new();private void Render(){}private void RegisterPopupTree(UIElement item){}
PDF_METHOD
DIALOG_METHOD
BUTTON_METHOD
}
""";
}
