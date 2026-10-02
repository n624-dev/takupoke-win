using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Takupoke.Win;

public static class Program
{
    private static readonly ConcurrentQueue<Uri> Pending = new();
    internal static Action<Uri>? ProtocolCallback;
    internal static Action? OpenRequested;
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (args is ["--unregister"])
        {
            try { ActivationRegistrationManager.UnregisterForProtocolActivation("jp.n624.takupoke.win", Environment.ProcessPath!); }
            catch { }
            try { Microsoft.Windows.AppNotifications.AppNotificationManager.Default.UnregisterAll(); }
            catch { }
            return;
        }
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var instance = AppInstance.FindOrRegisterForKey("takupoke-win");
        if (!instance.IsCurrent)
        {
            Task.Run(async () => await instance.RedirectActivationToAsync(activation)).GetAwaiter().GetResult();
            return;
        }
        void Activated(AppActivationArguments data)
        {
            var uri = ProtocolActivation.GetCallback(data);
            if (uri is not null)
            {
                var callback = ProtocolCallback; if (callback is null) Pending.Enqueue(uri); else callback(uri);
                OpenRequested?.Invoke();
            }
            else if (data.Kind != ExtendedActivationKind.Protocol) OpenRequested?.Invoke();
        }
        instance.Activated += (_, data) => Activated(data);
        Activated(activation);
        Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
    internal static void DrainCallbacks() { while (Pending.TryDequeue(out var uri)) ProtocolCallback?.Invoke(uri); }
}
