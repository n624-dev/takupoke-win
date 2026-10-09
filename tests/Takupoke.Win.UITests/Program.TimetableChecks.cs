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
    private static bool Visible(string id) => Find(id) is { } element && !element.Current.IsOffscreen;
    private static AutomationElement[] FindLessons(string subject) => _window?.FindAll(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
        .Where(element => element.Current.Name.Contains(subject, StringComparison.Ordinal)).ToArray() ?? [];
    private static void CheckLessonFocusAcrossClock()
    {
        AutomationElement[] lessons = [];
        Wait(() => (lessons = FindLessons("架空科目甲")).Length == 5, "The synthetic subject appears on all five weekdays");
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
        Console.WriteLine("Synthetic timetable menu: open-before-clock");
        Invoke("timetable-display-options");
        var option = ByName("時間割変更を反映", ControlType.MenuItem);
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
        ActivateDisplayMenuOption(option, previousValue);
        Wait(() => SavedIncludesChanges(preferences) != previousValue, "The retained display menu option remains selectable and saves its value");
        // Saving can precede the native close animation. Observe closure
        // before one new open operation; never invoke again to rescue it.
        Wait(() => _window!.FindFirst(TreeScope.Descendants,
            new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "時間割変更を反映"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)))
            is not { } item || item.Current.IsOffscreen,
            "The selected display menu closes before opening it to restore the preference");
        Console.WriteLine("Synthetic timetable menu: open-to-restore");
        Invoke("timetable-display-options");
        ActivateDisplayMenuOption(ByName("時間割変更を反映", ControlType.MenuItem), !previousValue);
        Wait(() => SavedIncludesChanges(preferences) == previousValue, "The menu regression restores the original display preference");
    }
    private static void ActivateDisplayMenuOption(AutomationElement option, bool stored)
    {
        var before = Checked(option);
        Console.WriteLine($"Synthetic display option before activation: checked={before};stored={stored}");
        Require(before == stored && option.Current.IsEnabled && !option.Current.IsOffscreen,
            "The visible display menu agrees with its saved preference before activation.");
        option.SetFocus();
        Wait(() => option.Current.HasKeyboardFocus,
            "The retained display menu item receives real keyboard focus");
        // One Enter on the observed, focused native item. Never retry the action
        // or set its ToggleState; the existing Click handler must persist it.
        System.Windows.Forms.SendKeys.SendWait("{ENTER}");
    }
    private static bool SavedIncludesChanges(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return !document.RootElement.TryGetProperty("includesChanges", out var value) || value.GetBoolean();
    }
}
