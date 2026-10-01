using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Takupoke.Win.Platform;

public sealed class DesktopIntegration : IDisposable
{
    private const uint TrayMessage = 0x8001;
    private readonly nint _window;
    private readonly SubclassProc _callback;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool _tray;
    public event Action<bool>? LockedChanged;
    public event Action? Resumed, Suspended, OpenRequested, ExitRequested;
    public bool TrayAvailable => _tray;
    public DesktopIntegration(nint window)
    {
        _window = window; _callback = WindowProc;
        if (!SetWindowSubclass(window, _callback, 1, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!WTSRegisterSessionNotification(window, 0))
        { RemoveWindowSubclass(window, _callback, 1); throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }
    public void SetTray(bool enabled)
    {
        if (_tray == enabled) return;
        var data = IconData();
        if (enabled)
        {
            if (!ShellNotifyIcon(0, ref data)) throw new InvalidOperationException("通知領域にアイコンを登録できませんでした。");
            _tray = true; data.Version = 4; ShellNotifyIcon(4, ref data);
        }
        else { ShellNotifyIcon(2, ref data); _tray = false; }
    }
    public static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルを確認できません。");
            var command = "\"" + executable + "\" --background";
            if (command.Length > 260) throw new InvalidOperationException("自動起動に対応するパス長を超えています。");
            key.SetValue("TakupokeWin", command, RegistryValueKind.String);
        }
        else key.DeleteValue("TakupokeWin", false);
    }
    private NotifyIconData IconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 1, Flags = 1 | 2 | 4 | 128,
        CallbackMessage = TrayMessage, Icon = LoadIcon(0, (nint)32512), Tip = "たくポケ Win — 開く / 完全に終了", Info = "", InfoTitle = ""
    };
    // Delegate lifetime is rooted by this instance; callbacks stay on the owning UI thread.
    private nint WindowProc(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        try
        {
            if (message == 0x02b1 && wparam is 7 or 8) LockedChanged?.Invoke(wparam == 7);
            else if (message == 0x0218 && wparam is 7 or 18) Resumed?.Invoke();
            else if (message == 0x0218 && wparam == 4) Suspended?.Invoke();
            else if (message == _taskbarCreated && _tray) { _tray = false; SetTray(true); }
            else if (message == TrayMessage)
            {
                var notification = (uint)lparam & 0xffff;
                if (notification is 0x0400 or 0x0401 or 0x0203) OpenRequested?.Invoke();
                else if (notification == 0x007b) ShowMenu();
                return 0;
            }
        }
        catch { /* Managed exceptions must not cross the native callback boundary. */ }
        return DefSubclassProc(window, message, wparam, lparam);
    }
    private void ShowMenu()
    {
        var menu = CreatePopupMenu(); if (menu == 0) return;
        try
        {
            AppendMenu(menu, 0, 1, "たくポケ Winを開く"); AppendMenu(menu, 0, 2, "完全に終了");
            GetCursorPos(out var point); SetForegroundWindow(_window);
            var chosen = TrackPopupMenu(menu, 0x0100 | 0x0002, point.X, point.Y, 0, _window, 0);
            PostMessage(_window, 0, 0, 0);
            if (chosen == 1) OpenRequested?.Invoke(); else if (chosen == 2) ExitRequested?.Invoke();
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose()
    { SetTray(false); WTSUnRegisterSessionNotification(_window); RemoveWindowSubclass(_window, _callback, 1); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id, Flags, CallbackMessage; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProc(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(nint window);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern nint LoadIcon(nint instance, nint resource);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rectangle);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
}
