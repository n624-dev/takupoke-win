using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;

namespace Takupoke.Win.UITests;

internal static class Program
{
    private static Process? _process;
    private static AutomationElement? _window;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static int _checks;
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") != "1")
                throw new InvalidOperationException("An executable and isolated offline test data root are required.");
            Start(args[0]);
            Wait(() => Find("page-home") is not null, "home heading");
            Navigate("links"); Navigate("timetable"); Navigate("settings");
            Invoke(ByName("クラスを選択"));
            var homeroom = WaitElement("class-1_1"); Toggle(homeroom);
            var department = WaitElement("class-1_CN"); Toggle(department);
            Require(Checked(homeroom) && Checked(department), "Year one allows a homeroom and department together.");
            var upper = WaitElement("class-3_IT"); Toggle(upper);
            Require(Checked(upper) && !Checked(homeroom) && !Checked(department), "Selecting another class removes incompatible selections.");
            Invoke(ByName("保存"));
            var preferences = Path.Combine(args[1], "preferences.json");
            Wait(() => SavedClass(preferences) == "3_IT", "class preference is persisted");
            Navigate("timetable");
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "クラス：3-IT")) is not null, "selected class appears on timetable");
            Navigate("home");
            var refresh = WaitElement("refresh-home");
            refresh.SetFocus();
            Require(refresh.Current.HasKeyboardFocus, "Home refresh supports keyboard focus.");
            Invoke(refresh);
            Wait(() => Find("refresh-home")?.Current.IsEnabled == true, "offline refresh completes");
            Stop();
            Start(args[0]); Navigate("timetable");
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "クラス：3-IT")) is not null, "saved class survives app restart");
            Console.WriteLine($"Passed {_checks} Windows UI checks: navigation, class constraints, persistence, focus and offline refresh.");
            return 0;
        }
        catch (Exception error)
        {
            // Only synthetic labels appear in this test. Do not capture screenshots or application data.
            Console.Error.WriteLine("Windows UI check failed: " + error.GetType().Name + " — " + error.Message);
            return 1;
        }
        finally { Stop(); }
    }
    private static void Start(string executable)
    {
        _process = Process.Start(new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false }) ?? throw new InvalidOperationException("App did not start.");
        Wait(() =>
        {
            _process.Refresh();
            if (_process.HasExited) throw new InvalidOperationException("App exited before its window was available.");
            if (_process.MainWindowHandle == 0) return false;
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
    private static AutomationElement? Find(string id) => _window?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
    private static AutomationElement WaitElement(string id) { AutomationElement? result = null; Wait(() => (result = Find(id)) is not null, id); return result!; }
    private static AutomationElement ByName(string name)
    {
        AutomationElement? result = null;
        Wait(() => (result = _window!.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))) is not null, name);
        return result!;
    }
    private static void Navigate(string page)
    {
        var item = WaitElement("nav-" + page);
        if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)) ((SelectionItemPattern)pattern).Select();
        else Invoke(item);
        Wait(() => Find("page-" + page) is not null, page + " page");
    }
    private static void Invoke(AutomationElement element) => ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
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
