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
    private static void Start(string executable)
    {
        _process = Process.Start(new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false }) ?? throw new InvalidOperationException("App did not start.");
        Wait(() =>
        {
            _process.Refresh();
            if (_process.HasExited) throw new InvalidOperationException($"App exited before its window was available (0x{_process.ExitCode:X8}).");
            if (_process.MainWindowHandle == 0) return false;
            if (!SystemParametersInfo(0x0030, 0, out var work, 0)) throw new InvalidOperationException("The test desktop work area could not be queried.");
            if (!SetWindowPos(_process.MainWindowHandle, 0, work.Left, work.Top, Math.Min(1150, work.Right - work.Left), Math.Min(820, work.Bottom - work.Top), 0x0044))
                throw new InvalidOperationException("The isolated app could not be positioned on the test desktop.");
            _window = AutomationElement.FromHandle(_process.MainWindowHandle); return _window is not null;
        }, "WinUI window");
        Wait(() => Find("page-home") is not null && Find("refresh-home")?.Current.IsEnabled == true, "app initialization");
    }
    private static void Stop()
    {
        if (_process is null) return;
        try { if (!_process.HasExited) { _process.Kill(true); _process.WaitForExit(5000); } }
        finally { _process.Dispose(); _process = null; _window = null; }
    }
    private static System.Windows.Rect VisiblePageBounds()
    {
        var bounds = System.Windows.Rect.Empty;
        Wait(() => TryMeasuredPageBounds(out bounds), "The arranged page viewport is available through the isolated layout probe");
        return bounds;
    }
    private static bool TryMeasuredPageBounds(out System.Windows.Rect bounds)
    {
        bounds = System.Windows.Rect.Empty;
        // The outer ScrollViewer peer includes unclipped content in its UIA
        // height. The fake-data app probe reports the arranged viewport in
        // logical pixels; convert that actual size to physical UIA pixels.
        if (Find("timetable-grid-scroller") is not { } table || Find("page-scroller") is not { } page) return false;
        var geometry = table.Current.Name;
        if (!geometry.StartsWith("Synthetic layout: ", StringComparison.Ordinal)) return false;
        var measuredPage = System.Text.RegularExpressions.Regex.Match(geometry, @"; page=([^,;]+),([^;]+);");
        var measuredScale = System.Text.RegularExpressions.Regex.Match(geometry, @"; scale=([^;]+)$");
        bool Number(string text, out double value) => double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value > 0;
        if (!measuredPage.Success || !measuredScale.Success
            || !Number(measuredPage.Groups[1].Value, out var width)
            || !Number(measuredPage.Groups[2].Value, out var height)
            || !Number(measuredScale.Groups[1].Value, out var scale)) return false;
        var origin = page.Current.BoundingRectangle;
        if (origin.IsEmpty) return false;
        bounds = System.Windows.Rect.Intersect(new System.Windows.Rect(origin.Left, origin.Top, width * scale, height * scale),
            _window!.Current.BoundingRectangle);
        return !bounds.IsEmpty;
    }
    private static AutomationElement? Find(string id) => _window?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
    private static AutomationElement WaitElement(string id) { AutomationElement? result = null; Wait(() => (result = Find(id)) is not null, id); return result!; }
    private static AutomationElement ByName(string name, ControlType? role = null)
    {
        AutomationElement? result = null;
        Wait(() => (result = _window!.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, role ?? ControlType.Button)))) is not null, name);
        return result!;
    }
    private static void Navigate(string page)
    {
        if (Find("page-" + page) is not null) return;
        var item = WaitElement("nav-" + page);
        if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
        {
            var selection = (SelectionItemPattern)pattern;
            if (selection.Current.IsSelected && page != "home")
            { Navigate("home"); item = WaitElement("nav-" + page); selection = (SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern); }
            selection.Select();
        }
        else Invoke(item);
        Wait(() => Find("page-" + page) is not null, page + " page");
    }
    private static void SetSearch(string query)
    {
        // The minute refresh can rebuild controls between consecutive inputs.
        var search = WaitElement("link-search");
        var edit = search.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)) ?? search;
        ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(query);
    }
    private static int TextColor(string id)
    {
        var text = (TextPattern)WaitElement(id).GetCurrentPattern(TextPattern.Pattern);
        return text.DocumentRange.GetAttributeValue(TextPattern.ForegroundColorAttribute) is int color
            ? color : throw new InvalidOperationException("The text provider did not report its rendered foreground color.");
    }
    private static void SelectMainColor(string color, string preferences)
    {
        Wait(() =>
        {
            if (SavedMainColor(preferences) == color && Find("main-color")?.Current.IsEnabled == true) return true;
            var combo = Find("main-color");
            if (combo?.Current.IsEnabled != true) return false;
            if (combo.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
            var expansion = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            try
            {
                if (expansion.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) expansion.Expand();
                var option = _window!.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, UserPreferences.MainColorLabel(color)), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)));
                if (option?.Current.IsEnabled != true) return false;
                ((SelectionItemPattern)option.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                return false; // Saving and rendering are asynchronous; verify persisted state on the next pass.
            }
            catch (ElementNotEnabledException) { return false; }
        }, "main color " + color + " is selectable and saved");
    }

    private static string? SavedMainColor(string path)
    {
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.TryGetProperty("mainColor", out var color) ? color.GetString() : "default";
    }
    private static void PickMaterial(string? path)
    {
        var dialogs = new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window), new PropertyCondition(AutomationElement.ClassNameProperty, "#32770"));
        // The desktop picker is an owned window beneath the app in the UIA tree.
        // Restrict discovery to this app rather than unrelated desktop dialogs.
        var existing = _window!.FindAll(TreeScope.Descendants, dialogs).Cast<AutomationElement>().Select(e => e.Current.NativeWindowHandle).ToHashSet();
        AutomationElement? select = null;
        Wait(() => (select = Find("select-material-Exam"))?.Current.IsEnabled == true, "material selection is ready");
        if (select!.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
        Invoke(select);
        AutomationElement? dialog = null;
        Wait(() => (dialog = _window!.FindAll(TreeScope.Descendants, dialogs).Cast<AutomationElement>().FirstOrDefault(e => !existing.Contains(e.Current.NativeWindowHandle))) is not null, "native file picker opens");
        if (path is not null)
        {
            var edit = dialog!.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
            if (edit is null) throw new InvalidOperationException("Native file-name field was not found.");
            ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
        }
        var button = dialog!.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button), new PropertyCondition(AutomationElement.AutomationIdProperty, path is null ? "2" : "1")));
        if (button is null) throw new InvalidOperationException("Native file picker action was not found.");
        Invoke(button);
        Wait(() => { try { return !dialog.Current.IsEnabled || dialog.Current.NativeWindowHandle == 0; } catch (ElementNotAvailableException) { return true; } }, "native file picker closes");
    }
    private static void PointerClick(AutomationElement element)
    {
        _lastStep = "pointer click on lesson text opens details";
        if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
        element.SetFocus();
        if (!element.TryGetClickablePoint(out var point))
        {
            var bounds = element.Current.BoundingRectangle;
            if (element.Current.IsOffscreen || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("The synthetic lesson is outside the visible viewport.");
            point = new System.Windows.Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        }
        if (!SetCursorPos((int)point.X, (int)point.Y)) throw new InvalidOperationException("The test pointer could not be positioned.");
        MouseEvent(0x0002, 0, 0, 0, 0); MouseEvent(0x0004, 0, 0, 0, 0);
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SystemParametersInfo(uint action, uint parameter, out NativeRect value, uint update);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extra);
    private static void Invoke(string id)
    {
        // Find and invoke inside the retry boundary: a periodic render can
        // replace a control even between obtaining it and reading its ID.
        Wait(() =>
        {
            var current = Find(id);
            if (current?.Current.IsEnabled != true || !current.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)) return false;
            try { ((InvokePattern)pattern).Invoke(); return true; }
            catch (ElementNotEnabledException) { return false; }
        }, "invoke " + id);
    }
    private static void Invoke(AutomationElement element)
    {
        var id = element.Current.AutomationId; var name = element.Current.Name; var role = element.Current.ControlType;
        Wait(() =>
        {
            var current = id.Length > 0 ? Find(id) : _window!.FindFirst(TreeScope.Descendants,
                new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, role)));
            if (current?.Current.IsEnabled != true || !current.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)) return false;
            try { ((InvokePattern)pattern).Invoke(); return true; }
            catch (ElementNotEnabledException) { return false; }
        }, "invoke " + name);
    }
    private static void Toggle(AutomationElement element) => ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
    private static bool Checked(AutomationElement element) => ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState == ToggleState.On;
    private static string? SavedClass(string path)
    {
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var classes = document.RootElement.GetProperty("selectedClasses");
        return classes.GetArrayLength() == 1 ? classes[0].GetString() : null;
    }
    private static void Require(bool success, string label) { if (!success) throw new InvalidOperationException(label); _checks++; }
    private static void Wait(Func<bool> condition, string label)
    {
        _lastStep = label;
        var deadline = DateTime.UtcNow + Timeout;
        do
        {
            try { if (condition()) { _checks++; return; } }
            catch (ElementNotAvailableException) { }
            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);
        throw new TimeoutException(label);
    }
}
