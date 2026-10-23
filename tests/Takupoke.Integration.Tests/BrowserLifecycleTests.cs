using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class BrowserLifecycleTests
{
    [Fact]
    public async Task ActualBrowserMethodsOnlyCloseTheirOwnWindowAcrossInitializationRaces()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "document-methods.txt"));
        var methods = source[source.IndexOf("    private Window? _browserWindow;", StringComparison.Ordinal)..source.IndexOf("    private async Task ShowPdf(", StringComparison.Ordinal)];
        await CSharpMethodProbe.AssertRunAsync(Host.Replace("BROWSER_METHODS", methods));
    }

    private const string Host = """
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
class Program
{
    static List<string> failures = new();
    static void Check(bool value, string message) { if (!value) failures.Add(message); }
    static async Task Main()
    {
        // First completion and failure are both obsolete after a replacement.
        foreach (var fail in new[] {false,true})
        {
            Reset(); var host = new MainWindow(); var a = host.Open(); var wa = Window.All[0]; var ba = WebView2.All[0];
            var b = host.Open(); var wb = Window.All[1];
            CoreWebView2Environment.Pending[0].SetResultOrException(fail); await a;
            Check(!wb.ClosedAlready && host.Messages==0, "Obsolete A must not close B or show an error");
            Check(ba.EnsureCalls==0 && ba.CloseCalls==1 && wa.ClosedAlready, "Closed A must not initialize again or be disposed twice");
            CoreWebView2Environment.Pending[1].SetResultOrException(false); await b;
            Check(host.Current==WebView2.All[1] && WebView2.All[1].Source?.Host=="example.invalid", "B must remain current and navigate");
            host.Close();
        }
        foreach (var reason in new[]{"manual","epoch","lock"})
        {
            Reset(); var host = new MainWindow(); var task = host.Open(); var window=Window.All[0]; var browser=WebView2.All[0];
            if(reason=="manual") window.Close(); else if(reason=="epoch") host.Model.PrivateEpoch++; else host.Model.Locked=true;
            CoreWebView2Environment.Pending[0].SetResultOrException(false); await task;
            Check(window.ClosedAlready && browser.EnsureCalls==0 && host.Current==null && host.Messages==0, "Manual close/epoch/lock must abandon only its own initialization: "+reason);
        }
        foreach(var failAt in new[]{"environment","ensure"})
        {
            Reset(); var host=new MainWindow(); var task=host.Open(); WebView2.All[0].FailEnsure=failAt=="ensure";
            CoreWebView2Environment.Pending[0].SetResultOrException(failAt=="environment"); await task;
            Check(Window.All[0].ClosedAlready && WebView2.All[0].CloseCalls==1 && host.Current==null && host.Messages==1, "Current failure must close once and report: "+failAt);
        }
        Reset(); var success=new MainWindow(); var opened=success.Open(); CoreWebView2Environment.Pending[0].SetResultOrException(false); await opened;
        var view=WebView2.All[0]; var core=view.CoreWebView2;
        Check(view.Private && !core.Settings.IsWebMessageEnabled && !core.Settings.AreHostObjectsAllowed, "Private mode and native bridge restrictions must remain enabled");
        Check(core.Navigation("http://example.invalid/") && !core.Navigation("https://example.invalid/"), "Only HTTPS navigation is permitted");
        Check(core.Download(), "Downloads remain refused");
        core.NewWindow("https://example.invalid/next"); Check(view.Source?.AbsolutePath=="/next", "HTTPS popups navigate in the same owned browser");
        core.NewWindow("file:///tmp/fixture"); Check(view.Source?.AbsolutePath=="/next", "Non-HTTPS popups remain refused"); success.Close();
        if(failures.Count>0) throw new InvalidOperationException(string.Join("\n",failures));
        Console.WriteLine("Actual browser methods: replacement, closure, epoch, lock, current errors and navigation restrictions passed.");
    }
    static void Reset() { Window.All.Clear(); WebView2.All.Clear(); CoreWebView2Environment.Pending.Clear(); }
}
static class Completion { public static void SetResultOrException(this TaskCompletionSource<CoreWebView2Environment> source,bool fail) { if(fail)source.SetException(new IOException("fictional environment failure"));else source.SetResult(new()); } }
class Model { public bool OfflineTest,Locked; public int PrivateEpoch; public string Root=Path.GetTempPath(); }
class Element {}
class ContentDialog {}
class Image {}
class Button:Element { public bool IsEnabled; }
class Window { public static List<Window> All=new(); public Window(){All.Add(this);} public string Title="";public object? Content; public event EventHandler? Closed; public bool ClosedAlready; public void Activate(){} public void Close(){if(ClosedAlready)return;ClosedAlready=true;Closed?.Invoke(this,EventArgs.Empty);} }
class StackPanel:Element { public Orientation Orientation;public int Spacing;public Thickness Padding;public List<object> Children=new(); }
enum Orientation { Horizontal }
class Thickness {public Thickness(params int[] values){} }
class Grid:Element {public List<RowDefinition> RowDefinitions=new();public List<object> Children=new();public static void SetRow(object obj,int row){} }
class RowDefinition {public GridLength? Height;}
class GridLength {public static GridLength Auto=new(0);public GridLength(int n,GridUnitType type=default){} }
enum GridUnitType { Star }
class ScrollViewer:Element {public object? Content;public ScrollBarVisibility HorizontalScrollBarVisibility,VerticalScrollBarVisibility;}
enum ScrollBarVisibility {Auto,Disabled}
class CoreWebView2Environment { public static List<TaskCompletionSource<CoreWebView2Environment>> Pending=new();public static Task<CoreWebView2Environment> CreateWithOptionsAsync(object? a,string path,object? c){var t=new TaskCompletionSource<CoreWebView2Environment>();Pending.Add(t);return t.Task;}public Options CreateCoreWebView2ControllerOptions()=>new();}
class Options {public bool IsInPrivateModeEnabled;}
class Settings {public bool IsWebMessageEnabled=true,AreHostObjectsAllowed=true;}
class Args:EventArgs {public string Uri="";public bool Cancel,Handled;}
class Core {public Settings Settings=new();public event EventHandler? HistoryChanged;public event EventHandler<Args>? DownloadStarting,NavigationStarting,NewWindowRequested;public bool Navigation(string uri){var a=new Args{Uri=uri};NavigationStarting?.Invoke(this,a);return a.Cancel;}public bool Download(){var a=new Args();DownloadStarting?.Invoke(this,a);return a.Cancel;}public void NewWindow(string uri){var a=new Args{Uri=uri};NewWindowRequested?.Invoke(this,a);if(!a.Handled)throw new Exception("Popup not handled");} }
class WebView2:Element {public static List<WebView2> All=new();public WebView2(){All.Add(this);}public int CloseCalls,EnsureCalls;public bool FailEnsure,Private;public bool CanGoBack,CanGoForward;public Uri? Source;public Core CoreWebView2=new();public void GoBack(){}public void GoForward(){}public void Reload(){}public Task EnsureCoreWebView2Async(CoreWebView2Environment e,Options o){EnsureCalls++;if(CloseCalls>0||FailEnsure)throw new IOException("fictional WebView failure");Private=o.IsInPrivateModeEnabled;return Task.CompletedTask;}public void Close(){CloseCalls++;}}
namespace Windows.System {static class Launcher {public static Task LaunchUriAsync(Uri u)=>Task.CompletedTask;}}
class MainWindow
{
    private Model _model=new(); public Model Model=>_model; public int Messages;
    public WebView2? Current=>_browser;public Task Open()=>OpenBrowser(new Uri("https://example.invalid/"),"Fictional");public void Close()=>CloseBrowser();
    private Button IconButton(string a,string b,Func<Task> c,string? d=null)=>new();private Task Message(string a,string b){Messages++;return Task.CompletedTask;}
BROWSER_METHODS
}
""";
}
