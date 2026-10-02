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
            if (args is ["--check-installer", var installer, var caption] && Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                return CheckInstallerDisplay(installer, caption);
            if (args.Length != 2 || Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") != "1")
                throw new InvalidOperationException("An executable and isolated offline test data root are required.");
            // UI Automation returns physical pixels. Match the tested app's per-monitor context.
            if (SetThreadDpiAwarenessContext((nint)(-4)) == 0) throw new InvalidOperationException("Per-monitor coordinates could not be enabled for the test.");
            SeedAsync(args[1]).GetAwaiter().GetResult();
            Start(args[0]);
            Wait(() => Find("page-home") is not null, "home heading");
            Navigate("links");
            Require(WaitElement("main-navigation").FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "たくポケ Win")).Count == 0, "The navigation body does not repeat the app title.");
            Wait(() => Find("link-fake-study") is not null && Find("link-fake-second") is not null, "saved links are displayed");
            Require(Find("link-fake-study")!.Current.BoundingRectangle.Top < Find("link-fake-second")!.Current.BoundingRectangle.Top, "Link order follows the API array rather than sort-order metadata.");
            SetSearch("存在しない架空検索語");
            Wait(() => Find("link-fake-study") is null, "search excludes nonmatching links");
            SetSearch("かくうがくしゅう");
            Wait(() => Find("link-fake-study") is not null, "kana search finds saved link");
            Require(Find("link-fake-study")!.Current.Name.Contains("架空カテゴリ", StringComparison.Ordinal), "Search results include the source category.");
            SetSearch("!");
            Wait(() => Find("link-fake-study") is not null && Find("link-fake-second") is not null, "A query empty after normalization shows the complete category list");
            Navigate("timetable"); Navigate("settings");
            Require(Find("material-summary-Exam") is null && Find("fetch-events") is null, "Root settings contains destinations rather than file and event details.");
            var preferences = Path.Combine(args[1], "preferences.json");
            Wait(() => !Visible("status-bar"), "idle footer is hidden");
            var bodyColor = TextColor("page-settings");
            foreach (var color in new[] { "green", "yellow", "orange", "red", "pink", "blue", "default", "purple" })
            {
                SelectMainColor(color, preferences);
                Require(TextColor("page-settings") == bodyColor, "Changing the main color must not recolor page text.");
            }
            CheckAuthentication(args[0], args[1]);
            Invoke(WaitElement("settings-materials"));
            Wait(() => Find("page-materials") is not null, "material list is a settings child screen");
            Invoke(WaitElement("suspend-automatic-refresh"));
            Wait(() => Find("automatic-refresh-paused") is not null, "Automatic file checking can be suspended while idle");
            Invoke(WaitElement("refresh-materials"));
            Wait(() => Find("refresh-materials")?.Current.IsEnabled == true && Find("automatic-refresh-paused") is null, "Manual refresh resumes automatic checking");
            Invoke(WaitElement("material-details-Timetable"));
            Wait(() => Find("page-material-Timetable") is not null, "normal material detail screen");
            Invoke(WaitElement("analysis-Timetable"));
            Wait(() => Find("page-analysis-Timetable") is not null && Find("analysis-weekday") is not null, "normal analysis and independent weekday filter");
            Invoke(ByName("資料の詳細に戻る")); Invoke(WaitElement("back-materials"));
            Invoke(WaitElement("material-details-Exam"));
            Wait(() => Find("page-material-Exam") is not null, "material detail screen");
            Invoke(WaitElement("back-materials")); Invoke(WaitElement("back-settings"));
            Invoke(WaitElement("settings-help")); Wait(() => Find("page-help") is not null, "purpose-based help");
            Invoke(ByName("時間割を見る")); Invoke(ByName("閉じる")); Invoke(WaitElement("back-settings"));
            Invoke(WaitElement("settings-setup"));
            Wait(() => Find("page-setup") is not null, "guided setup");
            Invoke(WaitElement("setup-next")); Wait(() => Find("setup-events") is not null, "setup material and event step");
            Invoke(WaitElement("setup-next")); Wait(() => Find("setup-class") is not null, "setup class step");
            Invoke(WaitElement("setup-later")); Navigate("settings");
            Invoke(WaitElement("settings-about"));
            Wait(() => Find("page-about") is not null, "about contains the legal documents");
            Require(Find("about-source") is not null && Find("about-contact") is not null, "About provides source and contact destinations.");
            Invoke(ByName("利用規約"));
            Invoke(ByName("閉じる"));
            Invoke(ByName("プライバシーポリシー"));
            Invoke(ByName("閉じる"));
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "閉じる"), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))) is null, "product document closes before opening the picker");
            Invoke(WaitElement("back-settings")); Invoke(WaitElement("settings-materials"));
            var selectedPdf = Path.Combine(args[1], "fictional-selection.pdf");
            File.WriteAllText(selectedPdf, "%PDF-1.7\n% Entirely synthetic malformed PDF for selection persistence.\n", Encoding.ASCII);
            PickMaterial(selectedPdf);
            Wait(() => Find("material-summary-Exam")?.Current.Name.Contains("fictional-selection.pdf", StringComparison.Ordinal) == true, "picker selection is saved even when parsing fails");
            PickMaterial(null);
            Wait(() => Find("material-summary-Exam")?.Current.Name.Contains("fictional-selection.pdf", StringComparison.Ordinal) == true, "canceling the picker preserves the previous selection");
            Invoke(WaitElement("back-settings"));
            Invoke(WaitElement("settings-class"));
            var homeroom = WaitElement("class-1_1"); Toggle(homeroom);
            var department = WaitElement("class-1_CN"); Toggle(department);
            Require(Checked(homeroom) && Checked(department), "Year one allows a homeroom and department together.");
            var upper = WaitElement("class-3_IT"); Toggle(upper);
            Require(Checked(upper) && !Checked(homeroom) && !Checked(department), "Selecting another class removes incompatible selections.");
            Invoke(ByName("保存"));
            Wait(() => SavedClass(preferences) == "3_IT", "class preference is persisted");
            Navigate("timetable");
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "クラス：3-IT")) is not null, "selected class appears on timetable");
            var tableBounds = WaitElement("timetable-grid-scroller").Current.BoundingRectangle;
            var pageBounds = WaitElement("page-scroller").Current.BoundingRectangle;
            Require(tableBounds.Width >= pageBounds.Width - 64, "The desktop table uses the available content width.");
            Require(tableBounds.Height >= pageBounds.Height - 170 && tableBounds.Bottom <= pageBounds.Bottom + 4, "The timetable viewport fills the page without pushing the table below it.");
            var clockBounds = WaitElement("timetable-clock-label-1").Current.BoundingRectangle;
            Require(clockBounds.Left >= tableBounds.Left + 5 && clockBounds.Right < tableBounds.Right && clockBounds.Height > 0, "The first timetable clock has visible horizontal padding.");
            var changeList = WaitElement("timetable-change-list");
            Require(((ExpandCollapsePattern)changeList.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Current.ExpandCollapseState == ExpandCollapseState.Expanded, "Changes are initially visible.");
            var originalWindowBounds = _window!.Current.BoundingRectangle;
            Require(SetWindowPos(_process!.MainWindowHandle, 0, (int)originalWindowBounds.Left, (int)originalWindowBounds.Top,
                (int)originalWindowBounds.Width, 600, 0x0044), "The test window can be shortened for the scroll regression.");
            Wait(() => ((ScrollPattern)WaitElement("timetable-grid-scroller").GetCurrentPattern(ScrollPattern.Pattern)).Current.VerticallyScrollable,
                "Shortening the host makes the synthetic eight-period grid scrollable");
            var timetableScroller = WaitElement("timetable-grid-scroller");
            var scrolling = (ScrollPattern)timetableScroller.GetCurrentPattern(ScrollPattern.Pattern);
            scrolling.SetScrollPercent(ScrollPattern.NoScroll, 60);
            var priorGridId = timetableScroller.GetRuntimeId();
            Wait(() => Find("timetable-grid-scroller") is { } refreshed && !refreshed.GetRuntimeId().SequenceEqual(priorGridId), "The clock periodically redraws the timetable");
            Wait(() => Math.Abs(((ScrollPattern)WaitElement("timetable-grid-scroller").GetCurrentPattern(ScrollPattern.Pattern)).Current.VerticalScrollPercent - 60) < 2, "Timetable redraw retains its vertical scroll position");
            ((ScrollPattern)WaitElement("timetable-grid-scroller").GetCurrentPattern(ScrollPattern.Pattern)).SetScrollPercent(ScrollPattern.NoScroll, 0);
            Require(SetWindowPos(_process!.MainWindowHandle, 0, (int)originalWindowBounds.Left, (int)originalWindowBounds.Top,
                (int)originalWindowBounds.Width, (int)originalWindowBounds.Height, 0x0044), "The original test window size can be restored.");
            Wait(() =>
            {
                var restoredTable = WaitElement("timetable-grid-scroller").Current.BoundingRectangle;
                var restoredPage = WaitElement("page-scroller").Current.BoundingRectangle;
                return restoredTable.Height >= restoredPage.Height - 170 && restoredTable.Bottom <= restoredPage.Bottom + 4;
            }, "Restoring the window expands the timetable viewport again");
            AutomationElement? lesson = null;
            Wait(() => (lesson = Find("架空科目甲")) is not null && !lesson.Current.IsOffscreen && lesson.Current.IsEnabled, "lesson is visible after navigation");
            var periodBounds = WaitElement("timetable-period-label-1").Current.BoundingRectangle;
            clockBounds = WaitElement("timetable-clock-label-1").Current.BoundingRectangle;
            Require(clockBounds.Right + 5 <= lesson!.Current.BoundingRectangle.Left && clockBounds.Top >= periodBounds.Bottom + 1, "Timetable clocks fit within their column with padding and spacing below the period.");
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
            Invoke(WaitElement("settings-materials"));
            Wait(() => Find("material-summary-Exam")?.Current.Name.Contains("fictional-selection.pdf", StringComparison.Ordinal) == true, "file selected through the native picker survives restart");
            Console.WriteLine($"Passed {_checks} Windows UI checks: hierarchical settings, desktop timetable geometry, raw and OS URI callbacks, fake OIDC verification and three datasets, failure/cancellation recovery, transient footer, persistence, colors, pointer and keyboard operations.");
            return 0;
        }
        catch (Exception error)
        {
            // Only synthetic labels appear in this test. Do not capture screenshots or application data.
            Console.Error.WriteLine("Windows UI check failed: " + error.GetType().Name + " — " + error.Message + " (step: " + _lastStep + ", passed: " + _checks + ")");
            if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                foreach (var id in new[] { "page-scroller", "timetable-grid-scroller", "page-timetable", "架空科目甲" })
                {
                    try
                    {
                        var element = Find(id); if (element is null) continue;
                        var bounds = element.Current.BoundingRectangle;
                        Console.Error.WriteLine($"Synthetic layout {id}: offscreen={element.Current.IsOffscreen}, enabled={element.Current.IsEnabled}, bounds={bounds}");
                        if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern))
                        { var scroll = ((ScrollPattern)pattern).Current; Console.Error.WriteLine($"Scroll: horizontal={scroll.HorizontalScrollPercent}, vertical={scroll.VerticalScrollPercent}, view={scroll.HorizontalViewSize}/{scroll.VerticalViewSize}"); }
                    }
                    catch (ElementNotAvailableException) { }
                }
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
    private static bool Visible(string id) => Find(id) is { } element && !element.Current.IsOffscreen;
    private static string ReadProbe(string path)
    { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var text = new StreamReader(file); return text.ReadToEnd(); }
    private static void WriteProbe(string path, string value)
    { File.WriteAllText(path + ".tmp", value); File.Move(path + ".tmp", path, true); }
    private static void CheckAuthentication(string executable, string root)
    {
        var mode = Path.Combine(root, "offline-auth-mode.txt"); var state = Path.Combine(root, "offline-auth-state.txt");
        var tokenRequests = Path.Combine(root, "offline-token-requests.txt"); var privateRequests = Path.Combine(root, "offline-private-requests.txt");
        const string commandPath = @"Software\Classes\jp.n624.takupoke.win\shell\open\command";
        using var existing = Registry.CurrentUser.OpenSubKey(commandPath);
        var previous = existing?.GetValue("") as string;
        using (var command = Registry.CurrentUser.CreateSubKey(commandPath)) command.SetValue("", "\"" + Path.GetFullPath(executable) + "\" \"----ms-protocol:%1\"");
        using (var scheme = Registry.CurrentUser.CreateSubKey(@"Software\Classes\jp.n624.takupoke.win")) scheme.SetValue("URL Protocol", "");
        try
        {
            File.Delete(tokenRequests); File.Delete(privateRequests);
            Invoke(WaitElement("settings-account"));
            Wait(() => Find("page-account") is not null, "account child screen");
            WriteProbe(mode, "fail"); File.Delete(state);
            Invoke(WaitElement("update-account"));
            Wait(() => File.Exists(state) && Visible("cancel-operation"), "authentication waits for an OS callback");
            var attemptState = ReadProbe(state);
            Require(Find("shared-details-Links")?.Current.IsEnabled == true, "Saved data details remain available while authenticating.");
            Invoke(WaitElement("shared-details-Links")); Invoke(ByName("閉じる"));
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=forged", shell: false);
            Require(Visible("cancel-operation") && !File.Exists(tokenRequests), "An unmatched callback neither completes authentication nor exchanges a token.");
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=" + attemptState, shell: false);
            Wait(() => Find("update-account")?.Current.IsEnabled == true && !Visible("cancel-operation"), "Legacy raw-URI launch ends authentication after token failure");
            Require(ReadProbe(tokenRequests) == "1" && !File.Exists(privateRequests), "Token failure cannot download school data.");
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "学校アカウントの認証を完了できませんでした。")) is not null, "authentication failure is visible on the account screen");
            Wait(() => !Visible("status-bar"), "failure footer disappears after a short interval");
            Require(Find("account-update-error")?.Current.Name == "学校アカウントの認証を完了できませんでした。", "The account failure remains available after the transient footer disappears.");
            Wait(() => Find("shared-details-Links")?.Current.IsEnabled == true, "The account screen stays usable after the footer disappears.");
            WriteProbe(mode, "hold"); File.Delete(state);
            Invoke(WaitElement("update-account"));
            Wait(() => File.Exists(state) && Visible("cancel-operation"), "retry creates a fresh authentication attempt");
            attemptState = ReadProbe(state);
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=" + attemptState, shell: true);
            Wait(() => ReadProbe(tokenRequests) == "2", "Windows protocol launch reaches token exchange");
            Require(Find("operation-status")?.Current.Name.Contains("認証情報を検証", StringComparison.Ordinal) == true, "Progress identifies token verification rather than a stale refresh result.");
            Invoke(WaitElement("back-settings"));
            Require(Find("settings-materials")?.Current.IsEnabled == true && Find("settings-help")?.Current.IsEnabled == true, "Settings navigation stays usable during token exchange.");
            SelectMainColor("green", Path.Combine(root, "preferences.json"));
            SelectMainColor("purple", Path.Combine(root, "preferences.json"));
            Require(ReadProbe(tokenRequests) == "2" && Visible("cancel-operation"), "Local preferences save without completing or canceling the pending token exchange.");
            Invoke(WaitElement("settings-help")); Wait(() => Find("page-help") is not null, "help is readable during token exchange");
            Invoke(WaitElement("back-settings")); Invoke(WaitElement("settings-account"));
            WriteProbe(mode, "success");
            Wait(() => Find("update-account")?.Current.IsEnabled == true && !Visible("cancel-operation"), "Validated fake authentication completes downloads and releases the UI");
            Require(ReadProbe(privateRequests) == "3", "All three datasets require the verified token and download once.");
            foreach (var kind in Enum.GetValues<DataSet>()) Require(Find("shared-status-" + kind)?.Current.Name == "取得済み", "Each independently saved dataset shows acquired status.");
            Wait(() => !Visible("status-bar"), "success footer automatically hides");
            WriteProbe(Path.Combine(root, "offline-auth-revision.txt"), new string('C', 43));
            WriteProbe(mode, "hold"); File.Delete(state);
            Invoke(WaitElement("update-account")); Wait(() => File.Exists(state) && Visible("cancel-operation"), "another update is cancellable");
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=" + ReadProbe(state), shell: true);
            Wait(() => ReadProbe(tokenRequests) == "3", "cancellation test reaches token exchange");
            Invoke(WaitElement("cancel-operation"));
            Wait(() => Find("update-account")?.Current.IsEnabled == true && !Visible("cancel-operation"), "canceling token exchange releases the UI");
            Require(ReadProbe(privateRequests) == "3", "Cancellation preserves previous data and starts no private downloads.");
            foreach (var kind in Enum.GetValues<DataSet>()) Require(Find("shared-status-" + kind)?.Current.Name == "取得済み", "Canceled authentication retains all prior datasets.");
            if (Visible("dismiss-status")) Invoke(WaitElement("dismiss-status"));
            Require(!Visible("status-bar"), "The result footer can be dismissed immediately.");
            Invoke(WaitElement("back-settings"));
        }
        finally
        {
            File.Delete(mode); File.Delete(state); File.Delete(Path.Combine(root, "offline-auth-revision.txt"));
            if (previous is not null) { using var command = Registry.CurrentUser.CreateSubKey(commandPath); command.SetValue("", previous); }
            else Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\jp.n624.takupoke.win", false);
        }
    }
    private static void SendCallback(string executable, string callback, bool shell)
    {
        var info = shell ? new ProcessStartInfo(callback) { UseShellExecute = true } : new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false };
        if (!shell) info.ArgumentList.Add(callback);
        using var redirect = Process.Start(info);
        if (!shell && (redirect is null || !redirect.WaitForExit(10000) || redirect.ExitCode != 0)) throw new InvalidOperationException("The synthetic callback process did not redirect successfully.");
    }
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
        // Each installer run starts with three old fictional revisions, while
        // personal preferences are deliberately left intact for reinstallation.
        await store.WriteAsync(lease, "api.mapping", new SavedMapping(new string('A', 43), "fake-old", 1, "\"fake-old-mapping\"", new string('a', 64), "2032-04-01T00:00:00Z", now, new([], [], [])));
        await store.WriteAsync(lease, "api.times", new SavedTimes(new string('A', 43), now, new(1, [])));
    }
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
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SystemParametersInfo(uint action, uint parameter, out NativeRect value, uint update);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extra);
    private static void Invoke(AutomationElement element)
    {
        var id = element.Current.AutomationId; var name = element.Current.Name;
        Wait(() =>
        {
            var current = id.Length > 0 ? Find(id) : _window!.FindFirst(TreeScope.Descendants,
                new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
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
