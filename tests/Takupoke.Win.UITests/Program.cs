using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.UITests;

internal static class Program
{
    private static Process? _process;
    private static AutomationElement? _window;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static int _checks;
    private static string _lastStep = "start";
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") != "1")
                throw new InvalidOperationException("An executable and isolated offline test data root are required.");
            SeedAsync(args[1]).GetAwaiter().GetResult();
            Start(args[0]);
            Wait(() => Find("page-home") is not null, "home heading");
            Navigate("links");
            Wait(() => Find("link-fake-study") is not null && Find("link-fake-second") is not null, "saved links are displayed");
            Require(Find("link-fake-study")!.Current.BoundingRectangle.Top < Find("link-fake-second")!.Current.BoundingRectangle.Top, "Link order follows the API array rather than sort-order metadata.");
            SetSearch("存在しない架空検索語");
            Wait(() => Find("link-fake-study") is null, "search excludes nonmatching links");
            SetSearch("かくうがくしゅう");
            Wait(() => Find("link-fake-study") is not null, "kana search finds saved link");
            Navigate("timetable"); Navigate("settings");
            var preferences = Path.Combine(args[1], "preferences.json");
            var bodyColor = TextColor("page-settings");
            foreach (var color in new[] { "green", "yellow", "orange", "red", "pink", "blue", "default", "purple" })
            {
                SelectMainColor(color, preferences);
                Require(TextColor("page-settings") == bodyColor, "Changing the main color must not recolor page text.");
            }
            Invoke(WaitElement("material-details-Timetable"));
            Wait(() => Find("page-material-Timetable") is not null, "normal material detail screen");
            Invoke(WaitElement("analysis-Timetable"));
            Wait(() => Find("page-analysis-Timetable") is not null && Find("analysis-weekday") is not null, "normal analysis and independent weekday filter");
            Invoke(ByName("資料の詳細に戻る")); Invoke(WaitElement("back-settings"));
            Invoke(WaitElement("material-details-Exam"));
            Wait(() => Find("page-material-Exam") is not null, "material detail screen");
            Invoke(WaitElement("back-settings"));
            Invoke(ByName("使い方")); Wait(() => Find("page-help") is not null, "purpose-based help");
            Invoke(ByName("時間割を見る")); Invoke(ByName("閉じる")); Invoke(WaitElement("back-settings"));
            Invoke(ByName("初期設定をもう一度表示"));
            Wait(() => Find("page-setup") is not null, "guided setup");
            Invoke(WaitElement("setup-next")); Wait(() => Find("setup-events") is not null, "setup material and event step");
            Invoke(WaitElement("setup-next")); Wait(() => Find("setup-class") is not null, "setup class step");
            Invoke(WaitElement("setup-later")); Navigate("settings");
            Invoke(ByName("利用規約"));
            Invoke(ByName("閉じる"));
            Invoke(ByName("プライバシーポリシー"));
            Invoke(ByName("閉じる"));
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "閉じる"), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))) is null, "product document closes before opening the picker");
            var selectedPdf = Path.Combine(args[1], "fictional-selection.pdf");
            File.WriteAllText(selectedPdf, "%PDF-1.7\n% Entirely synthetic malformed PDF for selection persistence.\n", Encoding.ASCII);
            PickMaterial(selectedPdf);
            Wait(() => Find("material-summary-Exam")?.Current.Name.Contains("fictional-selection.pdf", StringComparison.Ordinal) == true, "picker selection is saved even when parsing fails");
            PickMaterial(null);
            Wait(() => Find("material-summary-Exam")?.Current.Name.Contains("fictional-selection.pdf", StringComparison.Ordinal) == true, "canceling the picker preserves the previous selection");
            Invoke(ByName("クラスを選択"));
            var homeroom = WaitElement("class-1_1"); Toggle(homeroom);
            var department = WaitElement("class-1_CN"); Toggle(department);
            Require(Checked(homeroom) && Checked(department), "Year one allows a homeroom and department together.");
            var upper = WaitElement("class-3_IT"); Toggle(upper);
            Require(Checked(upper) && !Checked(homeroom) && !Checked(department), "Selecting another class removes incompatible selections.");
            Invoke(ByName("保存"));
            Wait(() => SavedClass(preferences) == "3_IT", "class preference is persisted");
            Navigate("timetable");
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "クラス：3-IT")) is not null, "selected class appears on timetable");
            AutomationElement? lesson = null;
            Wait(() => (lesson = Find("架空科目甲")) is not null && !lesson.Current.IsOffscreen && lesson.Current.IsEnabled, "lesson is visible after navigation");
            PointerClick(lesson!);
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "架空科目甲（正式名称）")) is not null, "lesson details show the saved full subject name");
            Invoke(ByName("閉じる"));
            Navigate("home");
            var refresh = WaitElement("refresh-home");
            refresh.SetFocus();
            Require(refresh.Current.HasKeyboardFocus, "Home refresh supports keyboard focus.");
            Invoke(refresh);
            Wait(() => Find("refresh-home")?.Current.IsEnabled == true, "offline refresh completes");
            Stop();
            Start(args[0]); Navigate("timetable");
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "クラス：3-IT")) is not null, "saved class survives app restart");
            Wait(() => Find("架空科目甲") is not null, "accepted timetable remains after source is unavailable");
            Navigate("settings");
            Require(SavedMainColor(preferences) == "purple", "Main color survives app restart.");
            Require(TextColor("page-settings") == bodyColor, "Restart retains theme text color.");
            Wait(() => Find("material-summary-Exam")?.Current.Name.Contains("fictional-selection.pdf", StringComparison.Ordinal) == true, "file selected through the native picker survives restart");
            Console.WriteLine($"Passed {_checks} Windows UI checks: navigation, class constraints, saved lessons and details, kana search, persistence, seven accent colors and OS default without recoloring text, focus and offline refresh.");
            return 0;
        }
        catch (Exception error)
        {
            // Only synthetic labels appear in this test. Do not capture screenshots or application data.
            Console.Error.WriteLine("Windows UI check failed: " + error.GetType().Name + " — " + error.Message + " (step: " + _lastStep + ", passed: " + _checks + ")");
            if (args.Length == 2 && Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1"
                && Path.GetFullPath(args[1]) == Path.GetFullPath(Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT") ?? ""))
            {
                var diagnostic = Path.Combine(args[1], "ui-error.txt");
                if (File.Exists(diagnostic)) foreach (var line in File.ReadLines(diagnostic).Take(30)) Console.Error.WriteLine(line);
            }
            return 1;
        }
        finally { Stop(); }
    }
    private static async Task SeedAsync(string root)
    {
        if (Path.GetFullPath(root) != Path.GetFullPath(Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT") ?? ""))
            throw new InvalidOperationException("The seed root must match the isolated app root.");
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector());
        var lease = await store.BeginAsync(); var now = DateTimeOffset.UtcNow;
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7\n% Entirely synthetic accepted-store UI fixture.\n");
        var source = new SourceRecord(Guid.NewGuid().ToString("N"), MaterialKind.Timetable, Path.Combine(root, "fake-unavailable-original.pdf"), "fake-identity", "fake-timetable.pdf",
            NotificationDiff.Digest(bytes), bytes.Length, now, now, now);
        await store.SaveOriginalAsync(lease, source, bytes);
        var lessons = Enumerable.Range(1, 5).Select(day => new NormalLesson("3_IT", day, 1,
            new("架空科目甲", "架空教員甲", "架空教室甲", "架空科目甲（正式名称）"), "完全に架空の授業", 1)).ToArray();
        await store.SaveAnalysisAsync(lease, new(source.Id, source.Kind, PdfScheduleParser.TimetableVersion, source.Digest, source.OriginalName, now, lease.Period.SchoolYear,
            Timetable: new(lease.Period.SchoolYear, lease.Period.Half == 1 ? "前期" : "後期", lessons)));
        var link = new LinkItem("fake-study", "fake-category", "架空学習リンク", "https://example.invalid/", "blue", true, 1, true, 1, [], "架空学習リンク|かくうがくしゅうりんく|kakuugakushuurinku");
        await store.WriteAsync(lease, "api.links", new SavedLinks(new("v1", "sha256-" + new string('a', 64), [new("fake-category", "架空カテゴリ", 1, [link, link with { Id = "fake-second", Label = "架空の別リンク", SortOrder = 0, SearchTerms = "別リンク" }])]),
            "\"fake-etag\"", now, new string('A', 43)));
    }
    private static void Start(string executable)
    {
        _process = Process.Start(new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false }) ?? throw new InvalidOperationException("App did not start.");
        Wait(() =>
        {
            _process.Refresh();
            if (_process.HasExited) throw new InvalidOperationException($"App exited before its window was available (0x{_process.ExitCode:X8}).");
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
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extra);
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
