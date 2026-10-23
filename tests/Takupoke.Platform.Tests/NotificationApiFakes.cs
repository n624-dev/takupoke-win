// Only the Windows App SDK boundary is simulated. Tests compile the unchanged
// production WindowsNotifications class and execute its actual delivery methods.
namespace Microsoft.Windows.AppNotifications
{
    public enum AppNotificationSetting { Enabled, DisabledForApplication, DisabledForUser, DisabledByGroupPolicy, Unsupported }
    public sealed class AppNotificationActivatedEventArgs : EventArgs { }
    public sealed class AppNotification { public uint Id { get; set; } public string Group { get; set; } = ""; public string Tag { get; set; } = ""; }
    public sealed class AppNotificationManager
    {
        public static AppNotificationManager Default { get; private set; } = new();
        public static bool Supported { get; set; } = true;
        public static bool IsSupported() => Supported;
        public static void Reset() { Default = new(); Supported = true; }
        public bool RegistrationFails { get; set; }
        public bool ShowFails { get; set; }
        public bool Accepts { get; set; } = true;
        public bool SettingFails { get; set; }
        public bool Registered { get; private set; }
        private AppNotificationSetting _setting = AppNotificationSetting.Enabled;
        public AppNotificationSetting Setting { get => SettingFails ? throw new InvalidOperationException() : _setting; set => _setting = value; }
        public List<AppNotification> Delivered { get; } = [];
        public event Action<AppNotificationManager, AppNotificationActivatedEventArgs>? NotificationInvoked;
        public void Invoke() => NotificationInvoked?.Invoke(this, new());
        public void Register() { if (RegistrationFails) throw new InvalidOperationException(); Registered = true; }
        public void Unregister() => Registered = false;
        public void Show(AppNotification notice) { if (ShowFails) throw new InvalidOperationException(); if (Accepts) { notice.Id = (uint)Delivered.Count + 1; Delivered.Add(notice); } }
        public Task<IReadOnlyList<AppNotification>> GetAllAsync() => Task.FromResult<IReadOnlyList<AppNotification>>(Delivered.ToArray());
        public Task RemoveAllAsync() { Delivered.Clear(); return Task.CompletedTask; }
        public Task RemoveByGroupAsync(string group) { Delivered.RemoveAll(n => n.Group == group); return Task.CompletedTask; }
    }
}
namespace Microsoft.Windows.AppNotifications.Builder
{
    public sealed class AppNotificationBuilder
    {
        public AppNotificationBuilder AddText(string value) => this;
        public AppNotificationBuilder AddArgument(string key, string value) => this;
        public Microsoft.Windows.AppNotifications.AppNotification BuildNotification() => new();
    }
}
