using Microsoft.Windows.AppNotifications;
using Takupoke.Win.Platform;
using Xunit;

namespace Takupoke.Platform.Tests;

public sealed class WindowsNotificationsTests : IDisposable
{
    public WindowsNotificationsTests() => AppNotificationManager.Reset();
    public void Dispose() => AppNotificationManager.Reset();
    [Fact]
    public async Task RealDeliverySuccessRemainsInStatusWhileTheOsSettingIsReadAgain()
    {
        using var sink = new WindowsNotifications(); sink.Initialize();
        Assert.DoesNotContain("送信しました", sink.Status);
        Assert.True(await sink.ShowAsync("changes", "fictional-fingerprint", 1, default));
        Assert.Contains("Windows通知を送信しました。", sink.Status);
        Assert.Contains("有効です。", sink.Status);
        Assert.True(await sink.ContainsAsync("changes", "fictional-fingerprint", default));
        AppNotificationManager.Default.Setting = AppNotificationSetting.DisabledForApplication;
        Assert.Contains("このアプリの通知が無効", sink.Status);
        Assert.Contains("Windows通知を送信しました。", sink.Status);
        Assert.False(await sink.IsAllowedAsync(default));
        await sink.ClearKindAsync("changes", default);
        Assert.False(await sink.ContainsAsync("changes", "fictional-fingerprint", default));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeliveryRejectionOrExceptionIsDisplayedAndSuccessReplacesIt(bool exception)
    {
        using var sink = new WindowsNotifications(); sink.Initialize();
        AppNotificationManager.Default.ShowFails = exception;
        AppNotificationManager.Default.Accepts = false;
        Assert.False(await sink.ShowAsync("exam", "fictional-fingerprint", 1, default));
        Assert.Contains("通知を送信できませんでした。", sink.Status);
        AppNotificationManager.Default.ShowFails = false; AppNotificationManager.Default.Accepts = true;
        Assert.True(await sink.ShowAsync("exam", "fictional-fingerprint", 1, default));
        Assert.DoesNotContain("送信できませんでした", sink.Status);
        Assert.Contains("送信しました", sink.Status);
    }
    [Fact]
    public async Task UnsupportedAndFailedRegistrationStayVisibleAndCannotDeliver()
    {
        AppNotificationManager.Supported = false;
        using (var sink = new WindowsNotifications())
        { sink.Initialize(); Assert.Contains("利用できません", sink.Status); Assert.False(await sink.ShowAsync("exam", "x", 1, default)); }
        AppNotificationManager.Supported = true; AppNotificationManager.Default.RegistrationFails = true;
        using var failed = new WindowsNotifications(); failed.Initialize();
        Assert.Contains("登録できませんでした", failed.Status);
        Assert.False(await failed.ShowAsync("exam", "x", 1, default));
    }
    [Fact]
    public async Task SettingReadFailureAndCancellationKeepTheirOwnFailureBehavior()
    {
        using var sink = new WindowsNotifications(); sink.Initialize();
        AppNotificationManager.Default.SettingFails = true;
        Assert.Contains("設定を確認できません", sink.Status); Assert.False(await sink.IsAllowedAsync(default));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sink.ShowAsync("exam", "x", 1, cancellation.Token));
        Assert.Empty(AppNotificationManager.Default.Delivered);
    }
}
