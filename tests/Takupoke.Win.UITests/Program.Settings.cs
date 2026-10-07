using System.IO;
using System.Windows.Automation;
using Takupoke.Core;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static UserPreferences SavedSettings(string path) =>
        DataCodec.Decode<UserPreferences>(PreferenceSnapshot.ReadBytes(path));

    private static LinkOpeningMode CheckSettingsKeyboardRoundTrip(string preferences)
    {
        var initial = SavedSettings(preferences);
        var ai = _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "use-ai-features"));
        Require(ai is not null && ai.Current.Name.Contains("OCR", StringComparison.Ordinal), "The OCR and AI opt-in toggle describes both engines.");
        ((TogglePattern)ai!.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        Wait(() => SavedSettings(preferences).UseAiFeatures != initial.UseAiFeatures, "AI opt-in persists the changed value.");
        ai = _window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "use-ai-features"));
        ((TogglePattern)ai!.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        Wait(() => SavedSettings(preferences).UseAiFeatures == initial.UseAiFeatures, "AI opt-in persists A to B to A.");
        var alternateColor = initial.MainColor == "green" ? "purple" : "green";
        SelectSettingsOptionWithKeyboard("main-color", UserPreferences.MainColorLabel(alternateColor),
            () => SavedSettings(preferences).MainColor == alternateColor);
        SelectSettingsOptionWithKeyboard("main-color", UserPreferences.MainColorLabel(initial.MainColor),
            () => SavedSettings(preferences).MainColor == initial.MainColor);
        var alternateOpening = initial.OpeningMode == LinkOpeningMode.InApp ? LinkOpeningMode.External : LinkOpeningMode.InApp;
        SelectSettingsOptionWithKeyboard("link-opening-mode", OpeningLabel(alternateOpening),
            () => SavedSettings(preferences).OpeningMode == alternateOpening);
        SelectSettingsOptionWithKeyboard("link-opening-mode", OpeningLabel(initial.OpeningMode),
            () => SavedSettings(preferences).OpeningMode == initial.OpeningMode);
        Require(SavedSettings(preferences).MainColor == initial.MainColor && SavedSettings(preferences).OpeningMode == initial.OpeningMode,
            "Both settings persist A to B to A on the same settings page.");
        return initial.OpeningMode;
    }
    private static string OpeningLabel(LinkOpeningMode mode) => mode == LinkOpeningMode.InApp ? "アプリ内で開く" : "既定のブラウザ";

    private static void CheckAnalysisFilterKeyboardRoundTrip(string preferences)
    {
        var initial = SavedSettings(preferences);
        var classes = new[] { "すべて", "3-IT" }; // The actual stored, wholly fictional timetable contains 3_IT.
        Require(initial.TimetableAnalysisClasses.Length <= 1 && initial.TimetableAnalysisClasses.All(value => value == "3_IT"),
            "Invented analysis round-trip starts with an available class filter.");
        var alternateClass = initial.TimetableAnalysisClasses.Length == 0 ? new[] { "3_IT" } : Array.Empty<string>();
        SelectSettingsOptionWithKeyboard("analysis-class", alternateClass.Length == 0 ? "すべて" : "3-IT",
            () => SavedSettings(preferences).TimetableAnalysisClasses.SequenceEqual(alternateClass), classes);
        SelectSettingsOptionWithKeyboard("analysis-class", initial.TimetableAnalysisClasses.Length == 0 ? "すべて" : "3-IT",
            () => SavedSettings(preferences).TimetableAnalysisClasses.SequenceEqual(initial.TimetableAnalysisClasses), classes);
        var days = new[] { "すべて", "月", "火", "水", "木", "金" };
        var alternateDay = initial.TimetableAnalysisWeekday == 1 ? 2 : 1;
        SelectSettingsOptionWithKeyboard("analysis-weekday", days[alternateDay],
            () => SavedSettings(preferences).TimetableAnalysisWeekday == alternateDay, days);
        Wait(() => _window!.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
            new PropertyCondition(AutomationElement.NameProperty, "読み取った内容 · 1件"))) is not null,
            "The selected weekday actually filters the five invented lessons to one.");
        SelectSettingsOptionWithKeyboard("analysis-weekday", days[initial.TimetableAnalysisWeekday],
            () => SavedSettings(preferences).TimetableAnalysisWeekday == initial.TimetableAnalysisWeekday, days);
        Require(SavedSettings(preferences).TimetableAnalysisClasses.SequenceEqual(initial.TimetableAnalysisClasses)
            && SavedSettings(preferences).TimetableAnalysisWeekday == initial.TimetableAnalysisWeekday,
            "Analysis class and weekday filters persist A to B to A on the same page.");
    }

    private static void SelectSettingsOptionWithKeyboard(string id, string label, Func<bool> saved, string[]? orderedLabels = null)
    {
        var labels = orderedLabels ?? (id == "main-color"
            ? UserPreferences.MainColors.Select(UserPreferences.MainColorLabel).ToArray()
            : new[] { "アプリ内で開く", "既定のブラウザ" });
        var index = Array.IndexOf(labels, label);
        Require(index >= 0, "Requested keyboard option exists in its ordered list.");
        var keyboardSent = false;
        try
        {
            Wait(() =>
            {
                // Saving can replace controls between focus/expansion and the
                // next poll. Reacquire inside the same retry boundary as Invoke.
                var current = Find(id);
                if (current?.Current.IsEnabled != true) return false;
                var expansion = (ExpandCollapsePattern)current.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                if (keyboardSent && saved() && expansion.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) return true;
                try
                {
                    if (expansion.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                    {
                        if (current.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
                        current.SetFocus();
                        if (!current.Current.HasKeyboardFocus) return false;
                        expansion.Expand();
                        return false;
                    }
                    if (expansion.Current.ExpandCollapseState != ExpandCollapseState.Expanded) return false;
                    // Popup items are virtualized. HOME/DOWN moves the actual
                    // ComboBox highlight without requiring a target UIA item.
                    System.Windows.Forms.SendKeys.SendWait("{HOME}" + string.Concat(Enumerable.Repeat("{DOWN}", index)) + "{ENTER}");
                    keyboardSent = true;
                    return false;
                }
                catch (ElementNotEnabledException) { return false; }
            }, id + " keyboard selection is persisted: " + label);
        }
        catch
        {
            var current = Find(id);
            var selected = current?.TryGetCurrentPattern(SelectionPattern.Pattern, out var pattern) == true
                ? string.Join(", ", ((SelectionPattern)pattern!).Current.GetSelection().Select(item => item.Current.Name)) : "unavailable";
            Console.WriteLine($"Keyboard setting diagnostic: {id}; expected={label}; selected={selected}; persisted={saved()}");
            throw;
        }
    }
}
