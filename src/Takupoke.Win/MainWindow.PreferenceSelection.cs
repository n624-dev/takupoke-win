using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private void BindPreferenceSelection<T>(ComboBox control, T initial,
        Func<(bool Valid, T Value)> selected, Func<UserPreferences, T, UserPreferences> update)
    {
        var observed = initial;
        control.SelectionChanged += async (_, _) =>
        {
            if (!control.IsLoaded) return;
            var (valid, value) = selected();
            if (!valid || EqualityComparer<T>.Default.Equals(observed, value)) return;
            // Record the actual transition before the serialized save awaits.
            // A return to the initial value remains a later user edit.
            observed = value;
            await _model.SavePreferencesAsync(current => update(current, value));
        };
    }
}
