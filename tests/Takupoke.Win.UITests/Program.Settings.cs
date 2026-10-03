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
        ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        AutomationElement? option = null;
        Wait(() => (option = ByName(label, ControlType.ListItem))?.Current.IsEnabled == true,
            id + " exposes the selected option");
        option!.SetFocus();
        Wait(() => option.Current.HasKeyboardFocus, id + " option supports keyboard focus");
        System.Windows.Forms.SendKeys.SendWait("{ENTER}");
        Wait(() => saved() && Find(id)?.Current.IsEnabled == true,
            id + " keyboard selection is persisted: " + label);
    }
}
