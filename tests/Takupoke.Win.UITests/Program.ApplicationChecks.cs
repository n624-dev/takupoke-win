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
    private static void CheckApplication(string[] args)
    {
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
        var selectedPdf = Path.Combine(args[1], "fictional-selection.pdf");
        File.WriteAllText(selectedPdf, "%PDF-1.7\n% Entirely synthetic malformed PDF for selection persistence.\n", Encoding.ASCII);
        PickMaterial(selectedPdf);
        Wait(() => Find("material-summary-Exam")?.Current.Name.Contains("fictional-selection.pdf", StringComparison.Ordinal) == true, "picker selection is saved even when parsing fails");
        PickMaterial(null);
        Wait(() => Find("material-summary-Exam")?.Current.Name.Contains("fictional-selection.pdf", StringComparison.Ordinal) == true, "canceling the picker preserves the previous selection");
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
        Require(TextColor("page-settings") == bodyColor, "Restart retains theme text color.");
        Invoke("settings-materials");
        Wait(() => Find("material-summary-Exam")?.Current.Name.Contains("fictional-selection.pdf", StringComparison.Ordinal) == true, "file selected through the native picker survives restart");
        CheckChangeRowSkips(args[0], args[1]);
    }
}
