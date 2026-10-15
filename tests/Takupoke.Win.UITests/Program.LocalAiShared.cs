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
    private static void DisplayMenuDiagnostic(bool previous, string preferences)
    {
        var remaining = _window!.FindFirst(TreeScope.Descendants,
            new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "時間割変更を反映"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)));
        GetWindowThreadProcessId(GetForegroundWindow(), out var owner);
        Console.Error.WriteLine($"Display-menu diagnostic: initial={previous}; saved={SavedIncludesChanges(preferences)}; menuExists={remaining is not null}; checked={(remaining is null ? "absent" : Checked(remaining).ToString())}; focused={remaining?.Current.HasKeyboardFocus}; foregroundOwner={owner}; appOwner={_window.Current.ProcessId}");
    }
    private static void EnterFocusedMenuOption(string name, string label)
    {
        // UIA can report item focus while another HWND receives keyboard input.
        // Deliver exactly one Enter only after both native foreground ownership
        // and the actual menu item's keyboard focus agree. Persistence and menu
        // identity assertions remain unchanged; no programmatic toggle is used.
        Wait(() =>
        {
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var owner);
            if (owner != _window!.Current.ProcessId)
            {
                if (!SetForegroundWindow(_window.Current.NativeWindowHandle)) return false;
            }
            var item = ByName(name, ControlType.MenuItem); item.SetFocus();
            GetWindowThreadProcessId(GetForegroundWindow(), out owner);
            return owner == _window.Current.ProcessId && item.Current.HasKeyboardFocus;
        }, label);
        var keys = new[] {
            new NativeInput { Type = 1, Data = new NativeInputData { Keyboard = new NativeKeyboardInput { VirtualKey = 0x0D } } },
            new NativeInput { Type = 1, Data = new NativeInputData { Keyboard = new NativeKeyboardInput { VirtualKey = 0x0D, Flags = 2 } } }
        };
        Require(SendInput((uint)keys.Length, keys, System.Runtime.InteropServices.Marshal.SizeOf<NativeInput>()) == keys.Length,
            "The OS accepted one complete Enter key press and release.");
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeKeyboardInput { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nuint ExtraInfo; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeMouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct NativeInputData {
        [System.Runtime.InteropServices.FieldOffset(0)] public NativeKeyboardInput Keyboard;
        [System.Runtime.InteropServices.FieldOffset(0)] public NativeMouseInput Mouse;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeInput { public uint Type; public NativeInputData Data; }
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [System.Runtime.InteropServices.In] NativeInput[] inputs, int size);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
    private static async Task<bool> SavedAuthenticationRevisionsAsync(string root)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector()); var lease = await store.BeginAsync();
        var expected = new string('B', 43);
        return (await store.ReadAsync<SavedLinks>(lease, "api.links"))?.Revision == expected
            && (await store.ReadAsync<SavedMapping>(lease, "api.mapping"))?.Revision == expected
            && (await store.ReadAsync<SavedTimes>(lease, "api.times"))?.Revision == expected;
    }
    private static async Task<SourceRecord?> ReadPickerSelectionAsync(string root)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector()); var lease = await store.BeginAsync();
        return await store.ReadAsync<SourceRecord>(lease, "selection.Exam");
    }
    private static void ShowPickerSummary()
    {
        var summaryVisible = false;
        var scrollable = false;
        Wait(() =>
        {
            if (_process is null || _process.HasExited) throw new InvalidOperationException("The app exited before showing the picker summary.");
            summaryVisible = Visible("material-summary-Exam");
            if (summaryVisible) return true;
            var page = Find("page-scroller");
            if (page is null || !page.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern)) return false;
            scrollable = ((ScrollPattern)pattern).Current.VerticallyScrollable;
            return true;
        }, "Inspect picker summary visibility and page scrolling");
        if (summaryVisible || !scrollable) return;
        for (var step = 0; step <= 10; step++)
        {
            Wait(() =>
            {
                if (_process is null || _process.HasExited) throw new InvalidOperationException("The app exited while showing the picker summary.");
                summaryVisible = Visible("material-summary-Exam");
                if (summaryVisible) return true;
                // Parsing can rebuild the page between scroll steps. Reacquire
                // both providers inside Wait so only a stale operation retries.
                var page = Find("page-scroller");
                if (page is null || !page.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern)) return false;
                var scrolling = (ScrollPattern)pattern;
                scrolling.SetScrollPercent(ScrollPattern.NoScroll, step * 10);
                return true;
            }, "Scroll the current page to the picker summary at " + step * 10 + "%");
            if (summaryVisible) return;
            System.Threading.Thread.Sleep(75);
        }
    }
    private static void SetSearch(string query, Func<bool> resultsMatch, string label)
    {
        // The minute refresh can rebuild controls between consecutive inputs.
        Wait(() =>
        {
            var search = Find("link-search");
            if (search is null) return false;
            var edit = search.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)) ?? search;
            // A rebuilding AutoSuggestBox can be present before its edit
            // provider supports input. Retry the same input/results check.
            if (!edit.Current.IsEnabled || !edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)) return false;
            var value = (ValuePattern)pattern;
            if (value.Current.Value != query) { value.SetValue(query); return false; }
            // Observe input and filtering together; a page rebuild between
            // separate waits must not certify input on a detached control.
            return resultsMatch();
        }, label);
    }
}
