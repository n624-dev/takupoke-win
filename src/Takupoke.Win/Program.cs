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
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
        {
            MessageBoxW(IntPtr.Zero, "たくポケにはWindows 11 24H2（build 26100）以降が必要です。", "たくポケ", 0x10);
            return;
        }
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
            RedirectActivationTo(instance, activation);
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
    private static void RedirectActivationTo(AppInstance instance, AppActivationArguments activation)
    {
        // The startup STA must dispatch COM calls while the SDK marshals the
        // activation to the running instance. Blocking on Task.GetResult can
        // deadlock the callback process even when the first window is usable.
        using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
        var redirect = Task.Run(async () =>
        {
            try { await instance.RedirectActivationToAsync(activation); }
            finally { completed.Set(); }
        });
        var result = CoWaitForMultipleObjects(0, uint.MaxValue, 1,
            [completed.SafeWaitHandle.DangerousGetHandle()], out _);
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(result);
        // The event is signalled on both success and failure; preserve SDK
        // failures instead of reporting a successful protocol redirection.
        redirect.GetAwaiter().GetResult();
    }
    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(uint flags, uint milliseconds,
        uint handleCount, nint[] handles, out uint index);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
    internal static void DrainCallbacks() { while (Pending.TryDequeue(out var uri)) ProtocolCallback?.Invoke(uri); }
}
