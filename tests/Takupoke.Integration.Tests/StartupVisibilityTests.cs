using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class StartupVisibilityTests
{
    [Fact]
    public async Task ActualInitializationAndLoadedMethodsPrepareBackgroundVisibilityBeforeWaitingForNetwork()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var window = File.ReadAllText(Path.Combine(fixture, "startup-window.txt"));
        var model = File.ReadAllText(Path.Combine(fixture, "startup-model.txt"));
        var loaded = window[window.IndexOf("    private async void Loaded(", StringComparison.Ordinal)..window.IndexOf("    private void OfferInitialSetup(", StringComparison.Ordinal)];
        var show = window[window.IndexOf("    private void ShowWindow(", StringComparison.Ordinal)..window.IndexOf("    private void Navigation_SelectionChanged(", StringComparison.Ordinal)];
        var initialize = model[model.IndexOf("    public async Task InitializeAsync(", StringComparison.Ordinal)..model.IndexOf("    private void InitializePlatform(", StringComparison.Ordinal)];
        await CSharpMethodProbe.AssertRunAsync(Host.Replace("WINDOW_METHODS", loaded + show).Replace("MODEL_METHODS", initialize));
    }

    private const string Host = """
using System;
using System.Threading;
using System.Threading.Tasks;
class Program
{
    public static bool StartInBackground;
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static async Task Main()
    {
        foreach (var scenario in new[] {
            (Background:true, Tray:true, Setup:true, Before:false, During:false, Failure:false),
            (Background:false, Tray:true, Setup:true, Before:false, During:false, Failure:false),
            (Background:true, Tray:false, Setup:true, Before:false, During:false, Failure:false),
            (Background:true, Tray:true, Setup:false, Before:false, During:false, Failure:false),
            (Background:true, Tray:true, Setup:true, Before:true, During:false, Failure:false),
            (Background:true, Tray:true, Setup:true, Before:false, During:true, Failure:false),
            (Background:true, Tray:true, Setup:true, Before:false, During:false, Failure:true) })
        {
            StartInBackground = scenario.Background;
            var model = new Model(scenario.Tray, scenario.Setup, scenario.Failure);
            var window = new MainWindow(model); window.Start();
            await model.LocalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (scenario.Before) window.ShowRequested();
            model.LocalRelease.TrySetResult();
            await model.NetworkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                Require(model.LocalReady, "Local data must be ready before network checks");
                Require(window.TrayAvailable == scenario.Tray, "Tray preparation must finish before slow network checks");
                var hidden = scenario.Background && scenario.Tray && scenario.Setup && !scenario.Before;
                Require(window.AppWindow.Visible == !hidden, "Background visibility must be settled while network is still pending");
                if (scenario.During) { window.ShowRequested(); Require(window.AppWindow.Visible, "An explicit show during network must remain visible"); }
            }
            finally { model.NetworkRelease.TrySetResult(); await window.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            Require(window.AppWindow.Visible == (!scenario.Background || !scenario.Tray || !scenario.Setup || scenario.Before || scenario.During),
                "Network success/failure must not hide a later explicit show request");
        }
        Console.WriteLine("Actual startup methods: seven background/foreground/tray/setup/network scenarios passed.");
    }
}
class Preferences { public bool KeepInTray; public bool SetupCompleted; public bool AutoStart; }
class FakeTimer { public void Start() {} }
class PreferenceBoundary(Model model) { public async Task<Preferences> LoadAsync(CancellationToken token) { model.LocalStarted.TrySetResult(); await model.LocalRelease.Task; return model.Preferences; } }
class Model
{
    public bool OfflineTest => false;
    public bool LocalReady, PreferencesReady;
    public bool Locked => false;
    public Preferences Preferences;
    private PreferenceBoundary _preferences;
    private FakeTimer _timer = new();
    public TaskCompletionSource LocalStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), LocalRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource NetworkStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), NetworkRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _failure;
    public Model(bool tray, bool setup, bool failure) { Preferences = new() { KeepInTray = tray, SetupCompleted = setup }; _preferences = new(this); _failure = failure; }
    public Task SetLockedAsync(bool locked) => Task.CompletedTask;
    public Task ResumeAutomaticRefreshAsync(bool force) => Task.CompletedTask;
    public void Cancel() {}
    private async Task RunAsync(Func<CancellationToken,Task> action) { await action(default); }
    private void InitializePlatform() {}
    private Task ReloadAsync(CancellationToken token) { LocalReady = true; return Task.CompletedTask; }
    private async Task RefreshAutomaticallyAsync(bool force) { NetworkStarted.TrySetResult(); await NetworkRelease.Task; if (_failure) Cancel(); }
MODEL_METHODS
}
class RoutedEventArgs:EventArgs {}
class WindowState { public bool Visible=true; public void Show() => Visible=true; public void Hide() => Visible=false; }
class DesktopIntegration
{
    public DesktopIntegration(nint handle) {}
    public bool TrayAvailable;
    public event Action<bool>? LockedChanged;
    public event Action? Resumed, Suspended, OpenRequested, ExitRequested;
    public static bool IsInputDesktopAccessible() => true;
    public static Task SetAutoStartAsync(bool enabled) => Task.CompletedTask;
    public void SetTray(bool enabled) => TrayAvailable=enabled;
}
namespace WinRT.Interop { static class WindowNative { public static nint GetWindowHandle(object window) => 0; } }
class MainWindow(Model model)
{
    private Model _model=model;
    private bool _ready, _exitRequested, _foregroundRequested;
    private DesktopIntegration? _desktop;
    public WindowState AppWindow=new();
    public TaskCompletionSource Completed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool TrayAvailable => _desktop?.TrayAvailable == true;
    public void Start() => Loaded(this,new());
    public void ShowRequested() => ShowWindow();
    private void Activate() {}
    private void Close() {}
    private Task Message(string title,string message) => Task.CompletedTask;
    private void OfferInitialSetup() => Completed.TrySetResult();
WINDOW_METHODS
}
""";
}
