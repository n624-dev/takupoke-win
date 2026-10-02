using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using WinRT;

namespace Takupoke.Win;

internal static class ProtocolActivation
{
    internal static Uri? GetCallback(AppActivationArguments activation)
    {
        if (activation.Kind == ExtendedActivationKind.Protocol)
            return activation.Data.As<IProtocolActivatedEventArgs>().Uri;
        // Earlier installers passed the raw URI. App SDK reports those as Launch,
        // including when forwarding activation to the existing app instance.
        if (activation.Kind != ExtendedActivationKind.Launch) return null;
        var launch = activation.Data.As<ILaunchActivatedEventArgs>();
        var memory = CommandLineToArgvW(launch.Arguments, out var count);
        if (memory == 0) return null;
        try
        {
            if (count != 2) return null;
            var argument = Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, IntPtr.Size));
            return argument is { Length: <= 16000 } && Uri.TryCreate(argument, UriKind.Absolute, out var uri)
                && uri.Scheme == "jp.n624.takupoke.win" ? uri : null;
        }
        finally { LocalFree(memory); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
