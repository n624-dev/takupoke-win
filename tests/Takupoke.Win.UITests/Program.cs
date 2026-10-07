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
            if (args is ["--capture", var capturedApp, var captureRoot] && Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                return CapturePages(capturedApp, captureRoot);
            if (args is ["--check-installer", var installer, var caption] && Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                return CheckInstallerDisplay(installer, caption);
            if (args is ["--shortcut", var shortcut, var target, var directory, var icon])
                return CheckShortcut(shortcut, target, directory, icon);
            if (args.Length != 2 || Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") != "1")
                throw new InvalidOperationException("An executable and isolated offline test data root are required.");
            // UI Automation returns physical pixels. Match the tested app's per-monitor context.
            if (SetThreadDpiAwarenessContext((nint)(-4)) == 0) throw new InvalidOperationException("Per-monitor coordinates could not be enabled for the test.");
            SeedAsync(args[1]).GetAwaiter().GetResult();
            Start(args[0]);
            Wait(() => Find("page-home") is not null, "home heading");
            Navigate("links");
            Require(_window!.Current.Name == "たくポケ", "The native window uses the product name.");
            Require(WaitElement("main-navigation").FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "たくポケ Win")).Count == 0, "The navigation body does not repeat the development name.");
            Invoke("link-menu-fake-study");
            Wait(() => ByName("お気に入りに追加", ControlType.MenuItem) is not null, "The visible link edit button exposes favorite controls");
            Invoke(ByName("お気に入りに追加", ControlType.MenuItem));
            Wait(() => Find("link-fake-study")?.Current.Name.Contains("お気に入り", StringComparison.Ordinal) == true, "Favorite editing persists through the visible menu");
            Invoke("link-menu-fake-study"); Invoke(ByName("お気に入りから外す", ControlType.MenuItem));
            Wait(() => Find("link-fake-study")?.Current.Name.Contains("お気に入り", StringComparison.Ordinal) == false, "Favorite editing can be undone");
            Wait(() => Find("link-fake-study") is not null && Find("link-fake-second") is not null, "saved links are displayed");
            // Favorite editing rebuilds the page. UIA nodes can appear before
            // layout assigns their rectangles, so wait for the original order.
            Wait(() =>
            {
                var first = Find("link-fake-study")?.Current.BoundingRectangle;
                var second = Find("link-fake-second")?.Current.BoundingRectangle;
                return first is { Height: > 0 } && second is { Height: > 0 } && first.Value.Top < second.Value.Top;
            }, "Link order follows the API array rather than sort-order metadata.");
            SetSearch("存在しない架空検索語", () => Find("link-fake-study") is null, "search excludes nonmatching links");
            SetSearch("かくうがくしゅう", () => Find("link-fake-study") is not null, "kana search finds saved link");
            Require(Find("link-fake-study")!.Current.Name.Contains("架空カテゴリ", StringComparison.Ordinal), "Search results include the source category.");
            SetSearch("!", () => Find("link-fake-study") is not null && Find("link-fake-second") is not null, "A query empty after normalization shows the complete category list");
            Navigate("timetable"); Navigate("settings");
            Require(Find("material-summary-Exam") is null && Find("fetch-events") is null, "Root settings contains destinations rather than file and event details.");
            var preferences = Path.Combine(args[1], "preferences.json");
            Wait(() => !Visible("status-bar"), "idle footer is hidden");
            var bodyColor = TextColor("page-settings");
            var initialOpeningMode = CheckSettingsKeyboardRoundTrip(preferences);
            foreach (var color in new[] { "green", "yellow", "orange", "red", "pink", "blue", "default", "purple" })
            {
                SelectMainColor(color, preferences);
                Require(TextColor("page-settings") == bodyColor, "Changing the main color must not recolor page text.");
            }
            Invoke("settings-materials");
            Wait(() => Find("material-summary-Timetable")?.Current.Name.EndsWith("解析済み", StringComparison.Ordinal) == true, "A current accepted analysis displays the same completed state as iOS");
            Invoke("back-settings");
            File.Delete(Path.Combine(args[1], "fake-available-original.pdf"));
            CheckAuthentication(args[0], args[1]);
            Invoke("settings-materials");
            Wait(() => Find("page-materials") is not null, "material list is a settings child screen");
            // Authentication cancellation intentionally pauses automatic checks.
            // Resume first so this separate test exercises an enabled stop action.
            Invoke("refresh-materials");
            Wait(() => Find("refresh-materials")?.Current.IsEnabled == true && Find("automatic-refresh-paused") is null, "Manual refresh resumes checking after authentication cancellation");
            Invoke("suspend-automatic-refresh");
            Wait(() => Find("automatic-refresh-paused") is not null, "Automatic file checking can be suspended while idle");
            Invoke("refresh-materials");
            Wait(() => Find("refresh-materials")?.Current.IsEnabled == true && Find("automatic-refresh-paused") is null, "Manual refresh resumes automatic checking");
            var schoolYearInput = WaitElement("materials-school-year");
            ((ValuePattern)schoolYearInput.GetCurrentPattern(ValuePattern.Pattern)).SetValue("2030");
            Navigate("home"); Navigate("settings"); Invoke("settings-materials");
            Require(((ValuePattern)WaitElement("materials-school-year").GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "2030",
                "An unsaved school-year draft survives rebuilding the page");
            ((ValuePattern)WaitElement("materials-school-year").GetCurrentPattern(ValuePattern.Pattern)).SetValue("");
            Invoke("material-details-Timetable");
            Wait(() => Find("page-material-Timetable") is not null, "normal material detail screen");
            Invoke("analysis-Timetable");
            Wait(() => Find("page-analysis-Timetable") is not null && Find("analysis-weekday") is not null, "normal analysis and independent weekday filter");
            CheckAnalysisFilterKeyboardRoundTrip(preferences);
            Invoke(ByName("資料の詳細に戻る")); Invoke("back-materials");
            Invoke("material-details-Exam");
            Wait(() => Find("page-material-Exam") is not null, "material detail screen");
            Invoke("back-materials"); Invoke("back-settings");
            Invoke("settings-help"); Wait(() => Find("page-help") is not null, "purpose-based help");
            Invoke(ByName("時間割を見る")); Invoke(ByName("閉じる")); Invoke("back-settings");
            Invoke("settings-setup");
            Wait(() => Find("page-setup") is not null, "guided setup");
            Invoke("setup-next"); Wait(() => Find("setup-events") is not null, "setup material and event step");
            Invoke("setup-next"); Wait(() => Find("setup-class") is not null, "setup class step");
            Invoke("setup-later"); Navigate("settings");
            Invoke("settings-about");
            Wait(() => Find("page-about") is not null, "about contains the legal documents");
            Wait(() => Find("about-source") is not null && Find("about-contact") is not null,
                "About provides source and contact destinations");
            Invoke(ByName("利用規約"));
            Invoke(ByName("閉じる"));
            Invoke(ByName("プライバシーポリシー"));
            Invoke(ByName("閉じる"));
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "閉じる"), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))) is null, "product document closes before opening the picker");
            Invoke("back-settings"); Invoke("settings-materials");
            var selectedName = "fictional-selection-" + Guid.NewGuid().ToString("N") + ".pdf";
            var selectedPdf = Path.Combine(args[1], selectedName);
            File.WriteAllText(selectedPdf, "%PDF-1.7\n% Entirely synthetic malformed PDF for selection persistence.\n", Encoding.ASCII);
            var previousSelection = ReadPickerSelectionAsync(args[1]).GetAwaiter().GetResult();
            SourceRecord? selected = null;
            try
            {
                PickMaterial(selectedPdf);
                var expectedDigest = NotificationDiff.Digest(File.ReadAllBytes(selectedPdf));
                Wait(() => (selected = ReadPickerSelectionAsync(args[1]).GetAwaiter().GetResult()) is { } current
                    && current.Id != previousSelection?.Id && current.Kind == MaterialKind.Exam
                    && string.Equals(Path.GetFullPath(current.Path), Path.GetFullPath(selectedPdf), StringComparison.OrdinalIgnoreCase)
                    && current.Digest == expectedDigest, "This picker invocation commits its unique original and digest");
                Wait(() => Find("select-material-Exam")?.Current.IsEnabled == true, "The new selection finishes parsing and reloading before cancel is tested");
                ShowPickerSummary();
                Wait(() => Visible("material-summary-Exam") && Find("material-summary-Exam")?.Current.Name.Contains(selectedName, StringComparison.Ordinal) == true, "picker selection is saved even when parsing fails");
                PickMaterial(null);
                Wait(() => Find("select-material-Exam")?.Current.IsEnabled == true, "The cancelled picker returns to its completed material page");
                var afterCancel = ReadPickerSelectionAsync(args[1]).GetAwaiter().GetResult();
                Require(afterCancel is not null && selected is not null && afterCancel.Id == selected.Id && afterCancel.Kind == selected.Kind
                    && afterCancel.Path == selected.Path && afterCancel.Digest == selected.Digest && afterCancel.FileIdentity == selected.FileIdentity,
                    "Cancel preserves the exact committed source identity, kind, path and digest");
                ShowPickerSummary();
                Wait(() => Visible("material-summary-Exam") && Find("material-summary-Exam")?.Current.Name.Contains(selectedName, StringComparison.Ordinal) == true, "canceling the picker preserves the previous selection");
            }
            catch
            {
                try
                {
                    Console.Error.WriteLine("Fictional picker diagnostic: " + JsonSerializer.Serialize(new { expectedName = selectedName, committed = selected,
                        actual = ReadPickerSelectionAsync(args[1]).GetAwaiter().GetResult(), summary = Find("material-summary-Exam")?.Current.Name,
                        summaryVisible = Visible("material-summary-Exam"), operationReady = Find("select-material-Exam")?.Current.IsEnabled,
                        activeDialogs = _window!.FindAll(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                            new PropertyCondition(AutomationElement.ClassNameProperty, "#32770"))).Cast<AutomationElement>().Take(5)
                            .Select(e => new { handle = e.Current.NativeWindowHandle, title = e.Current.Name, enabled = e.Current.IsEnabled }).ToArray() }));
                    Capture("fictional-picker-persistence-failure");
                }
                catch (Exception diagnosticFailure) { Console.Error.WriteLine("Picker diagnostic unavailable: " + diagnosticFailure.GetType().Name); }
                throw;
            }
            Invoke("back-settings");
            Invoke("settings-materials");
            Wait(() => Find("material-summary-Timetable")?.Current.Name.EndsWith("取得失敗（前回結果あり）", StringComparison.Ordinal) == true, "An unavailable source retains its accepted analysis and shows the same failure state as iOS");
            Invoke("back-settings");
            Invoke("settings-class");
            var homeroom = WaitElement("class-1_1"); Toggle(homeroom);
            var department = WaitElement("class-1_CN"); Toggle(department);
            Require(Checked(homeroom) && Checked(department), "Year one allows a homeroom and department together.");
            var upper = WaitElement("class-3_IT"); Toggle(upper);
            Require(Checked(upper) && !Checked(homeroom) && !Checked(department), "Selecting another class removes incompatible selections.");
            Invoke(ByName("保存"));
            Wait(() => SavedClass(preferences) == "3_IT", "class preference is persisted");
            Navigate("timetable");
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "クラス：3-IT")) is not null, "selected class appears on timetable");
            Wait(() => !Visible("status-bar") && !Visible("dismiss-status") && !Visible("cancel-operation"),
                "The class-save footer disappears before measuring the timetable");
            var pageBounds = VisiblePageBounds();
            var tableBounds = WaitElement("timetable-grid-scroller").Current.BoundingRectangle;
            Require(tableBounds.Width >= pageBounds.Width - 64, "The desktop table uses the available content width.");
            Require(TableFitsContentHeight(), "The timetable fits its full content height without an internal vertical viewport.");
            var clockBounds = WaitElement("timetable-clock-label-1").Current.BoundingRectangle;
            Require(clockBounds.Left >= tableBounds.Left + 5 && clockBounds.Right < tableBounds.Right && clockBounds.Height > 0, "The first timetable clock has visible horizontal padding.");
            Require(Find("timetable-clock-label-2") is not { Current.IsOffscreen: false } || Find("timetable-clock-label-2")?.Current.Name.Length == 0, "An undetermined common time is hidden while period labels remain");
            var changeList = WaitElement("timetable-change-list");
            Require(((ExpandCollapsePattern)changeList.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Current.ExpandCollapseState == ExpandCollapseState.Expanded, "Changes are initially visible.");
            var originalWindowBounds = _window!.Current.BoundingRectangle;
            Require(SetWindowPos(_process!.MainWindowHandle, 0, (int)originalWindowBounds.Left, (int)originalWindowBounds.Top,
                (int)originalWindowBounds.Width, Math.Max(400, (int)originalWindowBounds.Height - 200), 0x0044), "The test window can be shortened for the scroll regression.");
            Wait(() => VisiblePageBounds().Height < pageBounds.Height - 60
                && ((ScrollPattern)WaitElement("page-scroller").GetCurrentPattern(ScrollPattern.Pattern)).Current.VerticallyScrollable,
                "The shortened viewport is arranged and the whole page is scrollable");
            Require(!((ScrollPattern)WaitElement("timetable-grid-scroller").GetCurrentPattern(ScrollPattern.Pattern)).Current.VerticallyScrollable, "The table has no internal vertical scrolling even in a short window.");
            Require(TableFitsContentHeight(), "Shortening the window retains the complete table height.");
            var timetableScroller = WaitElement("page-scroller");
            var scrolling = (ScrollPattern)timetableScroller.GetCurrentPattern(ScrollPattern.Pattern);
            scrolling.SetScrollPercent(ScrollPattern.NoScroll, 60);
            Wait(() => Math.Abs(((ScrollPattern)WaitElement("page-scroller").GetCurrentPattern(ScrollPattern.Pattern)).Current.VerticalScrollPercent - 60) < 2,
                "The requested page scroll position is applied");
            var appliedScroll = ((ScrollPattern)WaitElement("page-scroller").GetCurrentPattern(ScrollPattern.Pattern)).Current;
            var savedOffsetFraction = appliedScroll.VerticalScrollPercent / 100 * (1 - appliedScroll.VerticalViewSize / 100);
            var priorGridId = WaitElement("timetable-grid-scroller").GetRuntimeId();
            Wait(() => Find("timetable-grid-scroller") is { } refreshed && !refreshed.GetRuntimeId().SequenceEqual(priorGridId), "The clock periodically redraws the timetable");
            Wait(() =>
            {
                var redrawn = ((ScrollPattern)WaitElement("page-scroller").GetCurrentPattern(ScrollPattern.Pattern)).Current;
                // UIA percent is relative to the remaining scroll range, so
                // viewport changes can alter it while the pixel offset stays
                // fixed. The synthetic content extent remains unchanged.
                var redrawnOffsetFraction = redrawn.VerticalScrollPercent / 100 * (1 - redrawn.VerticalViewSize / 100);
                return Math.Abs(redrawnOffsetFraction - savedOffsetFraction) <= 0.005;
            }, "Timetable redraw retains the page vertical scroll offset within the fixed synthetic content");
            ((ScrollPattern)WaitElement("page-scroller").GetCurrentPattern(ScrollPattern.Pattern)).SetScrollPercent(ScrollPattern.NoScroll, 0);
            Require(SetWindowPos(_process!.MainWindowHandle, 0, (int)originalWindowBounds.Left, (int)originalWindowBounds.Top,
                (int)originalWindowBounds.Width, (int)originalWindowBounds.Height, 0x0044), "The original test window size can be restored.");
            Wait(() =>
            {
                var restoredPage = VisiblePageBounds();
                return restoredPage.Height >= pageBounds.Height - 1
                    && TableFitsContentHeight();
            }, "Restoring the window retains the complete timetable height");
            CheckLessonFocusAcrossClock();
            ((ScrollPattern)WaitElement("page-scroller").GetCurrentPattern(ScrollPattern.Pattern)).SetScrollPercent(ScrollPattern.NoScroll, 0);
            Wait(() => !WaitElement("timetable-display-options").Current.IsOffscreen, "Display options are visible after scrolling the page to the top");
            CheckTimetableMenuAcrossClock(args[1], preferences);
            ((ScrollPattern)WaitElement("page-scroller").GetCurrentPattern(ScrollPattern.Pattern)).SetScrollPercent(ScrollPattern.NoScroll, 0);
            AutomationElement? lesson = null;
            Wait(() => (lesson = FindLessons("架空科目甲").FirstOrDefault()) is not null && !lesson.Current.IsOffscreen && lesson.Current.IsEnabled, "lesson is visible after navigation");
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
            Wait(() => FindLessons("架空科目甲").Length > 0, "accepted timetable remains after source is unavailable");
            Navigate("settings");
            Require(SavedMainColor(preferences) == "purple", "Main color survives app restart.");
            Require(SavedSettings(preferences).OpeningMode == initialOpeningMode, "A to B to A link-opening choice survives app restart.");
            Require(TextColor("page-settings") == bodyColor, "Restart retains theme text color.");
            Invoke("settings-materials");
            ShowPickerSummary();
            Wait(() => Find("material-summary-Exam")?.Current.Name.Contains(selectedName, StringComparison.Ordinal) == true, "file selected through the native picker survives restart");
            CheckRecoveryUi(args[0], args[1]);
            CheckDetailSnapshotUpdates(args[0], args[1]);
            Console.WriteLine($"Passed {_checks} Windows UI checks: hierarchical settings, desktop timetable geometry, raw and OS URI callbacks, fake OIDC verification and three datasets, failure/cancellation recovery, transient footer, persistence, colors, pointer and keyboard operations.");
            return 0;
        }
        catch (Exception error)
        {
            // Failure logs contain synthetic geometry only. Screen capture is
            // an explicit, isolated --capture mode with no production data.
            Console.Error.WriteLine("Windows UI check failed: " + error.GetType().Name + " — " + error.Message + " (step: " + _lastStep + ", passed: " + _checks + ")");
            Console.Error.WriteLine("Windows UI exception stack: " + error.StackTrace);
            if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
            {
                var exited = _process?.HasExited;
                Console.Error.WriteLine($"Synthetic app process: hasExited={exited}, exitCode={(exited == true ? (int?)_process!.ExitCode : null)}");
            }
            if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                Console.Error.WriteLine("Synthetic native window: " + _window?.Current.BoundingRectangle);
            if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                foreach (var id in new[] { "page-scroller", "status-bar", "timetable-grid-scroller", "page-timetable", "synthetic-lesson" })
                {
                    try
                    {
                        var element = id == "synthetic-lesson" ? FindLessons("架空科目甲").FirstOrDefault() : Find(id); if (element is null) continue;
                        var bounds = element.Current.BoundingRectangle;
                        Console.Error.WriteLine($"Synthetic layout {id}: automationId={element.Current.AutomationId}, focused={element.Current.HasKeyboardFocus}, offscreen={element.Current.IsOffscreen}, enabled={element.Current.IsEnabled}, bounds={bounds}");
                        if (id == "timetable-grid-scroller") Console.Error.WriteLine(element.Current.Name);
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
    private static AutomationElement[] FindLessons(string subject) => _window?.FindAll(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
        .Where(element => element.Current.Name.Contains(subject, StringComparison.Ordinal)).ToArray() ?? [];
    private static void CheckLessonFocusAcrossClock()
    {
        AutomationElement[] lessons = [];
        try { Wait(() => (lessons = FindLessons("架空科目甲")).Length == 5, "The synthetic subject appears on all five weekdays"); }
        catch
        {
            Console.Error.WriteLine("Fictional weekday fixture diagnostic: " + string.Join("; ", FindLessons("架空科目甲").Select(lesson => lesson.Current.AutomationId)));
            Capture("fictional-five-weekday-fixture-failure");
            throw;
        }
        var ids = lessons.Select(lesson => lesson.Current.AutomationId).Order(StringComparer.Ordinal).ToArray();
        Require(ids.All(id => id.Length > 0) && ids.Distinct(StringComparer.Ordinal).Count() == 5,
            "The same subject on different weekdays has five stable unique automation IDs.");
        var laterDay = WaitElement(ids[1]);
        if (laterDay.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
        laterDay.SetFocus();
        Wait(() => Find(ids[1])?.Current.HasKeyboardFocus == true, "The Tuesday lesson receives keyboard focus");
        var previousGrid = WaitElement("timetable-grid-scroller").GetRuntimeId();
        var elapsed = Stopwatch.StartNew();
        Wait(() => elapsed.Elapsed >= TimeSpan.FromSeconds(16)
            && Find("timetable-grid-scroller") is { } current && !current.GetRuntimeId().SequenceEqual(previousGrid),
            "A real periodic clock redraw occurs after focusing a later-day lesson");
        Wait(() => Find(ids[1])?.Current.HasKeyboardFocus == true,
            "Clock redraw preserves focus on the same Tuesday lesson rather than the Monday subject");
        Require(FindLessons("架空科目甲").Select(lesson => lesson.Current.AutomationId).Order(StringComparer.Ordinal).SequenceEqual(ids),
            "Clock redraw retains the same unique weekday lesson IDs.");
    }
    private static void CheckTimetableMenuAcrossClock(string root, string preferences)
    {
        var clockProbe = Path.Combine(root, "offline-clock-ticks.txt");
        Wait(() => File.Exists(clockProbe), "The isolated clock tick probe is available");
        var previousValue = SavedIncludesChanges(preferences);
        Invoke("timetable-display-options");
        var option = ByName("時間割変更を反映", ControlType.MenuItem);
        Require(Checked(option) == previousValue, "The original menu check matches the persisted display preference.");
        var menuId = option.GetRuntimeId();
        var previousGrid = WaitElement("timetable-grid-scroller").GetRuntimeId();
        var previousTick = ReadProbe(clockProbe);
        var elapsed = Stopwatch.StartNew();
        Wait(() => elapsed.Elapsed >= TimeSpan.FromSeconds(16) && ReadProbe(clockProbe) != previousTick,
            "The clock ticks while the timetable display menu remains open");
        option = ByName("時間割変更を反映", ControlType.MenuItem);
        Require(!option.Current.IsOffscreen && option.Current.IsEnabled && option.GetRuntimeId().SequenceEqual(menuId),
            "Periodic clock updates retain the original visible display menu item.");
        Require(WaitElement("timetable-grid-scroller").GetRuntimeId().SequenceEqual(previousGrid),
            "The timetable defers its periodic redraw while the display menu is open.");
        EnterFocusedMenuOption("時間割変更を反映", "The retained display menu option receives keyboard focus");
        try { Wait(() => SavedIncludesChanges(preferences) != previousValue, "The retained display menu option remains selectable and saves its value"); }
        catch { DisplayMenuDiagnostic(previousValue, preferences); throw; }
        Wait(() => !WaitElement("timetable-grid-scroller").GetRuntimeId().SequenceEqual(previousGrid),
            "Selecting the menu option closes the popup and redraws the saved preference");
        Invoke("timetable-display-options");
        option = ByName("時間割変更を反映", ControlType.MenuItem);
        Require(Checked(option) != previousValue, "The reopened display menu reflects the saved preference.");
        EnterFocusedMenuOption("時間割変更を反映", "The reopened display menu option receives keyboard focus");
        try
        {
            Wait(() => SavedIncludesChanges(preferences) == previousValue, "The menu regression restores the original display preference");
        }
        catch
        {
            var remaining = _window!.FindFirst(TreeScope.Descendants,
                new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "時間割変更を反映"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)));
            DisplayMenuDiagnostic(previousValue, preferences);
            throw;
        }
    }
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
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    private static bool SavedIncludesChanges(string path)
    {
        using var document = JsonDocument.Parse(PreferenceSnapshot.ReadBytes(path));
        return !document.RootElement.TryGetProperty("includesChanges", out var value) || value.GetBoolean();
    }
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
            Invoke("settings-account");
            Wait(() => Find("page-account") is not null, "account child screen");
            WriteProbe(mode, "fail"); File.Delete(state);
            Invoke("update-account");
            Wait(() => File.Exists(state) && Visible("cancel-operation"), "authentication waits for an OS callback");
            var attemptState = ReadProbe(state);
            Require(Find("shared-details-Links")?.Current.IsEnabled == true, "Saved data details remain available while authenticating.");
            Invoke("shared-details-Links"); Invoke(ByName("閉じる"));
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
            Invoke("update-account");
            Wait(() => File.Exists(state) && Visible("cancel-operation"), "retry creates a fresh authentication attempt");
            attemptState = ReadProbe(state);
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=" + attemptState, shell: true);
            Wait(() => ReadProbe(tokenRequests) == "2", "Windows protocol launch reaches token exchange");
            Require(Find("operation-status")?.Current.Name.Contains("認証情報を確認", StringComparison.Ordinal) == true, "Progress identifies token verification rather than a stale refresh result.");
            Invoke("back-settings");
            Wait(() => Find("page-settings") is not null && Find("settings-materials")?.Current.IsEnabled == true && Find("settings-help")?.Current.IsEnabled == true, "Settings navigation stays usable during token exchange");
            SelectMainColor("green", Path.Combine(root, "preferences.json"));
            SelectMainColor("purple", Path.Combine(root, "preferences.json"));
            try
            {
                // Preference JSON is saved before the asynchronous UI render.
                // Await the same pending-authentication condition after that render.
                Wait(() => ReadProbe(tokenRequests) == "2" && Visible("cancel-operation"),
                    "Local preferences save without completing or canceling the pending token exchange.");
            }
            catch
            {
                try
                {
                    var cancellation = Find("cancel-operation");
                    Console.WriteLine($"Pending-auth setting diagnostic: tokenRequests={ReadProbe(tokenRequests)}; cancelExists={cancellation is not null}; cancelOffscreen={cancellation?.Current.IsOffscreen}; cancelEnabled={cancellation?.Current.IsEnabled}; operation={Find("operation-status")?.Current.Name}");
                }
                catch (ElementNotAvailableException) { Console.WriteLine("Pending-auth setting diagnostic: controls changed during diagnostic collection."); }
                throw;
            }
            Invoke("settings-help"); Wait(() => Find("page-help") is not null, "help is readable during token exchange");
            Invoke("back-settings"); Invoke("settings-account");
            WriteProbe(mode, "success");
            Wait(() => Find("update-account")?.Current.IsEnabled == true && !Visible("cancel-operation"), "Validated fake authentication completes downloads and releases the UI");
            Require(ReadProbe(privateRequests) == "3", "All three datasets require the verified token and download once.");
            Wait(() => SavedAuthenticationRevisionsAsync(root).GetAwaiter().GetResult(), "All three verified downloads commit the expected fictional revision");
            try
            {
                // Operation controls can re-enable before the new UIA subtree is arranged.
                foreach (var kind in Enum.GetValues<DataSet>())
                    Wait(() => Find("shared-status-" + kind)?.Current.Name == "取得済み", "Each independently saved dataset shows acquired status: " + kind);
            }
            catch
            {
                Console.Error.WriteLine("Fictional acquisition status diagnostic: " + string.Join("; ", Enum.GetValues<DataSet>().Select(kind => kind + "=" + (Find("shared-status-" + kind)?.Current.Name ?? "<missing>"))));
                Capture("fictional-acquisition-status-failure");
                throw;
            }
            Wait(() => !Visible("status-bar"), "success footer automatically hides");
            WriteProbe(Path.Combine(root, "offline-auth-revision.txt"), new string('C', 43));
            WriteProbe(mode, "hold"); File.Delete(state);
            Invoke("update-account"); Wait(() => File.Exists(state) && Visible("cancel-operation"), "another update is cancellable");
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=" + ReadProbe(state), shell: true);
            Wait(() => ReadProbe(tokenRequests) == "3", "cancellation test reaches token exchange");
            Invoke("cancel-operation");
            Wait(() => Find("update-account")?.Current.IsEnabled == true && !Visible("cancel-operation"), "canceling token exchange releases the UI");
            Require(ReadProbe(privateRequests) == "3", "Cancellation preserves previous data and starts no private downloads.");
            foreach (var kind in Enum.GetValues<DataSet>()) Require(Find("shared-status-" + kind)?.Current.Name == "取得済み", "Canceled authentication retains all prior datasets.");
            if (Visible("dismiss-status")) Invoke("dismiss-status");
            Require(!Visible("status-bar"), "The result footer can be dismissed immediately.");
            Invoke("back-settings");
        }
        finally
        {
            File.Delete(mode); File.Delete(state); File.Delete(Path.Combine(root, "offline-auth-revision.txt"));
            if (previous is not null) { using var command = Registry.CurrentUser.CreateSubKey(commandPath); command.SetValue("", previous); }
            else Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\jp.n624.takupoke.win", false);
        }
    }
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
        // The preceding installer run adopts special schedules in this same
        // fictional root. They cover Monday and correctly replace its normal
        // lesson; this independent five-weekday fixture must start without them.
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            { DataSource = Path.Combine(root, "school", "school.sqlite"), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(); using var reset = connection.CreateCommand();
            reset.CommandText = "DELETE FROM entry WHERE key IN ('analysis.Exam','analysis.ExamReturn')";
            Console.WriteLine($"Reset {await reset.ExecuteNonQueryAsync()} prior fictional special analyses; personal preferences remain intact.");
        }
        if (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Exam") is not null || await store.ReadAsync<MaterialAnalysis>(lease, "analysis.ExamReturn") is not null)
            throw new InvalidOperationException("The five-weekday fixture still contains a special schedule.");
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7\n% Entirely synthetic accepted-store UI fixture.\n");
        // A real fictional file keeps the accepted digest current even when
        // native activation checks it. The scenario deletes it after checking
        // the completed status, then verifies retained results after failure.
        var sourcePath = Path.Combine(root, "fake-available-original.pdf");
        await File.WriteAllBytesAsync(sourcePath, bytes);
        using var content = await new FileSourceReader(new WindowsFileIdentity()).ReadAsync(sourcePath, MaterialKind.Timetable, null);
        var source = new SourceRecord(Guid.NewGuid().ToString("N"), MaterialKind.Timetable, sourcePath, content.Identity, "fake-timetable.pdf",
            NotificationDiff.Digest(bytes), bytes.Length, now, now, content.ModifiedAt);
        await store.SaveOriginalAsync(lease, source, bytes);
        var lessons = Enumerable.Range(1, 5).Select(day => new NormalLesson("3_IT", day, 1,
            new("架空科目甲", "架空教員甲", "架空教室甲", "架空科目甲（正式名称）"), "完全に架空の授業", 1)).ToArray();
        await store.SaveAnalysisAsync(lease, new(source.Id, source.Kind, PdfScheduleParser.TimetableVersion, source.Digest, source.OriginalName, now, lease.Period.SchoolYear,
            Timetable: new(lease.Period.SchoolYear, lease.Period.Half == 1 ? "前期" : "後期", lessons)));
        await store.WriteAsync(lease, "acquisition.Timetable", new MaterialAttempt(now, null, false, source.Digest));
        await store.WriteAsync(lease, "attempt.Timetable", new MaterialAttempt(now, null, true, source.Digest, lease.Period.SchoolYear, ParserVersion: PdfScheduleParser.TimetableVersion));
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
    private static void SetSearch(string query, Func<bool> resultsMatch, string label)
    {
        // The minute refresh can rebuild controls between consecutive inputs.
        Wait(() =>
        {
            var search = Find("link-search");
            if (search is null) return false;
            var edit = search.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)) ?? search;
            var value = (ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern);
            if (value.Current.Value != query) { value.SetValue(query); return false; }
            // Observe input and filtering together; a page rebuild between
            // separate waits must not certify input on a detached control.
            return resultsMatch();
        }, label);
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
        using var document = JsonDocument.Parse(PreferenceSnapshot.ReadBytes(path));
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
        var pickerHandle = dialog!.Current.NativeWindowHandle;
        if (pickerHandle == 0 || !IsWindowVisible(pickerHandle)) throw new InvalidOperationException("The selected native picker window is not visible.");
        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait(() => !IsWindowVisible(pickerHandle), "native file picker actually disappears");
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
        using var document = JsonDocument.Parse(PreferenceSnapshot.ReadBytes(path));
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
