using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Takupoke.Infrastructure.Notifications;

namespace Takupoke.Win.Platform;

public sealed class WindowsNotifications : INotificationSink, IDisposable
{
    private bool _registered;
    public event Action? Activated;
    private string _status = "通知の利用状態を確認していません。";
    public string Status
    {
        get
        {
            try { return _registered ? "Windowsの通知設定：" + AppNotificationManager.Default.Setting : _status; }
            catch { return "Windowsの通知設定を確認できません。"; }
        }
        private set => _status = value;
    }
    public void Initialize()
    {
        try
        {
            if (!AppNotificationManager.IsSupported()) { Status = "この環境ではWindowsアプリ通知を利用できません。"; return; }
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register(); _registered = true;
            Status = "Windowsの通知設定：" + AppNotificationManager.Default.Setting;
        }
        catch { Status = "Windows通知を登録できませんでした。通常ユーザー権限と実行環境を確認してください。"; }
    }
    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => Activated?.Invoke();
    private static string Tag(string fingerprint) => fingerprint[..Math.Min(16, fingerprint.Length)];
    public Task<bool> IsAllowedAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { return Task.FromResult(_registered && AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled); }
        catch { return Task.FromResult(false); }
    }
    public async Task<bool> ContainsAsync(string kind, string fingerprint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!_registered) return false;
        try { return (await AppNotificationManager.Default.GetAllAsync()).Any(n => n.Group == kind && n.Tag == Tag(fingerprint)); }
        catch { return false; }
    }
    public Task<bool> ShowAsync(string kind, string fingerprint, int count, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!_registered) return Task.FromResult(false);
        try
        {
            var label = kind switch { "changes" => "時間割変更", "exam" => "試験時間割PDFの更新", _ => "試験返却時間割PDFの更新" };
            var notice = new AppNotificationBuilder().AddText("たくポケ Win").AddText(label + "：" + count + "件。詳細はアプリで確認してください。")
                .AddArgument("destination", "timetable").BuildNotification();
            notice.Tag = Tag(fingerprint); notice.Group = kind;
            AppNotificationManager.Default.Show(notice);
            return Task.FromResult(notice.Id != 0);
        }
        catch { Status = "通知を送信できませんでした。Windowsの通知設定を確認してください。"; return Task.FromResult(false); }
    }
    public async Task ClearAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (_registered) await AppNotificationManager.Default.RemoveAllAsync(); }
    public async Task ClearKindAsync(string kind, CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (_registered) await AppNotificationManager.Default.RemoveByGroupAsync(kind); }
    public void Dispose()
    { if (_registered) { AppNotificationManager.Default.NotificationInvoked -= OnInvoked; AppNotificationManager.Default.Unregister(); _registered = false; } }
}
