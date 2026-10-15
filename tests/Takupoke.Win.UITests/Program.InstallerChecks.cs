using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Microsoft.Win32;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Materials;
using Takupoke.Win.Platform;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static int CheckInstallerDisplay(string installer, string caption)
    {
        var info = new ProcessStartInfo(Path.GetFullPath(installer)) { UseShellExecute = false };
        info.ArgumentList.Add("/LANG=japanese"); info.ArgumentList.Add("/SP-"); info.ArgumentList.Add("/NORESTART");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Installer did not start.");
        try
        {
            AutomationElement? wizard = null;
            Wait(() =>
            {
                process.Refresh();
                var owners = DescendantProcesses((uint)process.Id);
                EnumWindows((window, _) =>
                {
                    GetWindowThreadProcessId(window, out var owner);
                    if (!owners.Contains(owner) || !IsWindowVisible(window)) return true;
                    var candidate = AutomationElement.FromHandle(window);
                    if (!candidate.Current.Name.Contains(caption, StringComparison.Ordinal)) return true;
                    wizard = candidate; return false;
                }, 0);
                return wizard is not null;
            }, "installer caption identifies " + caption);
            // Inno's static labels may be exposed as a different UIA role, or
            // appear after the outer window. Inspect the visible native controls
            // as well, rather than assuming every label is a UIA Text element.
            Wait(() =>
            {
                var text = string.Join("\n", wizard!.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                    .Cast<AutomationElement>().Where(element => !element.Current.IsOffscreen).Select(element => element.Current.Name));
                var handle = (nint)wizard.Current.NativeWindowHandle;
                EnumChildWindows(handle, (child, _) =>
                {
                    if (IsWindowVisible(child))
                    {
                        var buffer = new System.Text.StringBuilder(GetWindowTextLength(child) + 1);
                        GetWindowText(child, buffer, buffer.Capacity); text += "\n" + buffer;
                    }
                    return true;
                }, 0);
                return text.Contains("現在のバージョン", StringComparison.Ordinal)
                    && text.Contains("インストールするバージョン", StringComparison.Ordinal);
            }, "upgrade wizard shows installed and new version labels");
            Console.WriteLine("Verified installer display: " + caption + " and installed/new version labels."); return 0;
        }
        finally
        {
            // No installation step was invoked; terminate only this isolated wizard.
            if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); }
        }
    }
    private static HashSet<uint> DescendantProcesses(uint root)
    {
        var owners = new HashSet<uint> { root }; var parents = new List<(uint Id, uint Parent)>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == (nint)(-1)) throw new InvalidOperationException("Installer process tree could not be inspected.");
        try
        {
            var entry = new ProcessEntry { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snapshot, ref entry)) do { parents.Add((entry.Id, entry.ParentId)); } while (Process32Next(snapshot, ref entry));
            while (true)
            { var previous = owners.Count; foreach (var pair in parents) if (owners.Contains(pair.Parent)) owners.Add(pair.Id); if (previous == owners.Count) break; }
            return owners;
        }
        finally { CloseHandle(snapshot); }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id; public nuint DefaultHeap; public uint ModuleId, Threads, ParentId; public int Priority; public uint Flags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    private delegate bool EnumWindow(nint window, nint parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowTextLength(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(nint window, System.Text.StringBuilder text, int count);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint process);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
