using System.IO;
using System.Windows.Automation;
using Takupoke.Core;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static UserPreferences SavedSettings(string path) => File.Exists(path)
        ? DataCodec.Decode<UserPreferences>(File.ReadAllBytes(path)) : new UserPreferences();

    private static LinkOpeningMode CheckSettingsKeyboardRoundTrip(string preferences)
    {
        var initial = SavedSettings(preferences);
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

    private static void SelectSettingsOptionWithKeyboard(string id, string label, Func<bool> saved)
    {
        var combo = WaitElement(id);
        if (combo.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
        combo.SetFocus();
        Wait(() => combo.Current.HasKeyboardFocus, id + " supports keyboard focus");
        var expansion = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expansion.Expand();
        // The popup virtualizes items around the current selection. Its target
        // item need not be exposed to UIA until keyboard navigation reaches it.
        Wait(() => expansion.Current.ExpandCollapseState == ExpandCollapseState.Expanded,
            id + " popup is expanded for keyboard selection");
        // UIA SetFocus on a popup item need not move the ComboBox's highlighted
        // item. Exercise its standard keyboard selection from a known position.
        var labels = id == "main-color"
            ? UserPreferences.MainColors.Select(UserPreferences.MainColorLabel).ToArray()
            : new[] { "アプリ内で開く", "既定のブラウザ" };
        var index = Array.IndexOf(labels, label);
        Require(index >= 0, "Requested keyboard option exists in its ordered list.");
        System.Windows.Forms.SendKeys.SendWait("{HOME}" + string.Concat(Enumerable.Repeat("{DOWN}", index)) + "{ENTER}");
        try
        {
            Wait(() => saved() && Find(id)?.Current.IsEnabled == true,
                id + " keyboard selection is persisted: " + label);
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
