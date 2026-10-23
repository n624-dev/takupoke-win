using System.Collections.Concurrent;
using Takupoke.Core;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Notifications;
using Takupoke.Win.ViewModels;

// Link the real application model and selection handler. Only WinUI dispatch,
// file identity, browser launch and the OS toast boundary are replaced here.
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        public bool HasThreadAccess => true;
        public DispatcherQueueTimer CreateTimer() => new();
        public bool TryEnqueue(Action action) { _queue.Enqueue(action); return true; }
        public void Drain() { while (_queue.TryDequeue(out var action)) action(); }
    }
    public sealed class DispatcherQueueTimer
    {
        public TimeSpan Interval { get; set; }
        public event Action<DispatcherQueueTimer, object>? Tick;
        public void Start() { }
        public void Stop() { }
        public void Fire() => Tick?.Invoke(this, new());
    }
}
namespace Microsoft.UI.Xaml.Controls
{
    public sealed class ToggleMenuFlyoutItem
    {
        public bool IsChecked { get; set; }
        public event EventHandler? Click;
        public void Change(bool value) { IsChecked = value; Click?.Invoke(this, EventArgs.Empty); }
    }
    public sealed class ToggleSwitch
    {
        public bool IsLoaded { get; set; }
        public bool IsOn { get; set; }
        public event EventHandler? Toggled;
        public void Change(bool value) { IsOn = value; Toggled?.Invoke(this, EventArgs.Empty); }
    }
    public sealed class ComboBox
    {
        public bool IsLoaded { get; set; }
        public int SelectedIndex { get; set; }
        public string SelectedClass { get; set; } = "";
        public event EventHandler? SelectionChanged;
        public void Change() => SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
namespace Takupoke.Win
{
    internal static class Program
    {
        public static Action<Uri>? ProtocolCallback { get; set; }
        public static void DrainCallbacks() { }
    }
    public sealed partial class MainWindow(AppViewModel model)
    {
        private readonly AppViewModel _model = model;
        public void BindToggle(Microsoft.UI.Xaml.Controls.ToggleSwitch control, Func<UserPreferences, bool> current,
            Func<UserPreferences, bool, UserPreferences> update) => BindPreferenceToggle(control, current, update);
        public void EditSchoolYear(string value) => SetSchoolYearDraft(value);
        public string? SchoolYearDraft => _schoolYearDraft;
        public Task SaveSchoolYear(string value) => SaveSchoolYearAsync(value);
        public void Bind<T>(Microsoft.UI.Xaml.Controls.ComboBox control, T initial,
            Func<(bool Valid, T Value)> selected, Func<UserPreferences, T, UserPreferences> update)
            => BindPreferenceSelection(control, initial, selected, update);
    }
}
namespace Takupoke.Win.Platform
{
    public sealed class BrowserAuthenticator : IDisposable
    {
        public BrowserAuthenticator(OidcClient oidc, Action<Uri>? openBrowser = null) { _ = oidc; _ = openBrowser; }
        public event Action<string>? ProgressChanged { add { } remove { } }
        public static void RegisterProtocol() { }
        public void HandleCallback(Uri uri) { }
        public void Cancel() { }
        public Task<string> AuthenticateAsync(CancellationToken token) => throw new InvalidOperationException("No authentication in lifecycle fixtures.");
        public void Dispose() { }
    }
    public sealed class WindowsNotifications : INotificationSink, IDisposable
    {
        public static WindowsNotifications? Latest { get; private set; }
        public static HashSet<(string Kind, string Fingerprint)> Delivered { get; } = [];
        public WindowsNotifications() => Latest = this;
        public string Status => "架空のOS通知";
        public event Action? Activated { add { } remove { } }
        public int Clears { get; private set; }
        public int Shows { get; private set; }
        public Func<CancellationToken, Task>? BeforeAllowed { get; set; }
        public Action? AfterAccepted { get; set; }
        public void Initialize() { }
        public async Task<bool> IsAllowedAsync(CancellationToken token)
        { if (BeforeAllowed is not null) await BeforeAllowed(token); token.ThrowIfCancellationRequested(); return true; }
        public Task<bool> ContainsAsync(string kind, string fingerprint, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(Delivered.Contains((kind, fingerprint))); }
        public Task<bool> ShowAsync(string kind, string fingerprint, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Shows++; Delivered.Add((kind, fingerprint)); AfterAccepted?.Invoke(); return Task.FromResult(true); }
        public Task ClearAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Clears++; Delivered.Clear(); return Task.CompletedTask; }
        public Task ClearKindAsync(string kind, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Delivered.RemoveWhere(item => item.Kind == kind); return Task.CompletedTask; }
        public void Dispose() { }
    }
}
