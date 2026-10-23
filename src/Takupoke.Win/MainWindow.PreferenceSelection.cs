using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private string? _schoolYearDraft;
    private long _schoolYearDraftVersion;
    private void SetSchoolYearDraft(string value) { _schoolYearDraft = value; _schoolYearDraftVersion++; }
    private async Task SaveSchoolYearAsync(string selectedYear)
    {
        var submitted = _schoolYearDraftVersion;
        var saved = await _model.SavePreferencesWithResultAsync(current => current with { DefaultSchoolYear = selectedYear.Length == 0 ? null : selectedYear });
        if (saved && submitted == _schoolYearDraftVersion) _schoolYearDraft = null;
    }
    private Func<T, Task> PreferenceEdit<T>(T initial, Func<UserPreferences, T, UserPreferences> update,
        Func<UserPreferences, Task>? applyPlatform = null)
    {
        var observed = initial;
        return async value =>
        {
            if (EqualityComparer<T>.Default.Equals(observed, value)) return;
            observed = value; // Record the user's transition before any serialized await.
            if (applyPlatform is null) await _model.SavePreferencesAsync(current => update(current, value));
            else await _model.SavePlatformPreferencesAsync(current => update(current, value), applyPlatform);
        };
    }
    private void BindPreferenceToggle(ToggleSwitch control, Func<UserPreferences, bool> current,
        Func<UserPreferences, bool, UserPreferences> update, Func<UserPreferences, Task>? applyPlatform = null)
    {
        var edit = PreferenceEdit(current(_model.Preferences), update, applyPlatform);
        control.Toggled += async (_, _) => { if (control.IsLoaded) await edit(control.IsOn); };
    }
    private void BindPreferenceToggleMenu(ToggleMenuFlyoutItem control, Func<UserPreferences, bool> current,
        Func<UserPreferences, bool, UserPreferences> update, Action<bool, bool>? observe = null)
    {
        var edit = PreferenceEdit(current(_model.Preferences), update);
        control.Click += async (_, _) =>
        {
            var enabled = control.IsChecked;
            observe?.Invoke(false, enabled); await edit(enabled); observe?.Invoke(true, enabled);
        };
    }
    private void BindPreferenceSelection<T>(ComboBox control, T initial,
        Func<(bool Valid, T Value)> selected, Func<UserPreferences, T, UserPreferences> update)
    {
        var edit = PreferenceEdit(initial, update);
        control.SelectionChanged += async (_, _) =>
        {
            if (!control.IsLoaded) return;
            var (valid, value) = selected();
            if (valid) await edit(value);
        };
    }
}
