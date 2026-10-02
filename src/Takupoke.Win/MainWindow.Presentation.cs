using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private bool _micaAvailable;

    private void ConfigureWindowChrome()
    {
        _micaAvailable = MicaController.IsSupported();
        if (_micaAvailable) SystemBackdrop = new MicaBackdrop();
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            SetTitleBar(AppTitleBar);
        }
        else
        {
            AppTitleBar.Visibility = Visibility.Collapsed;
            RootGrid.RowDefinitions[0].Height = new GridLength(0);
        }
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "takupoke.ico"));
        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>())
            item.Icon = FluentIcon((string)item.Tag switch { "links" => "list", "timetable" => "calendar", _ => (string)item.Tag });
        if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
            RootGrid.RequestedTheme = Environment.GetEnvironmentVariable("TAKUPOKE_TEST_THEME") switch
            { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        PageHost.SizeChanged += (_, _) => UpdatePageLayout();
        ApplyWindowChrome();
    }

    private void ApplyWindowChrome()
    {
        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var highContrast = _accessibility.HighContrast;
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        Windows.UI.Color Color(byte value) => Windows.UI.Color.FromArgb(255, value, value, value);
        var background = highContrast ? _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background) : Color(dark ? (byte)32 : (byte)243);
        var foreground = highContrast ? _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground) : Color(dark ? (byte)255 : (byte)32);
        RootGrid.Background = new SolidColorBrush(_micaAvailable && !highContrast ? transparent : background);
        var title = AppWindow.TitleBar;
        title.BackgroundColor = title.InactiveBackgroundColor = ExtendsContentIntoTitleBar ? transparent : background;
        title.ButtonBackgroundColor = title.ButtonInactiveBackgroundColor = ExtendsContentIntoTitleBar ? transparent : background;
        title.ForegroundColor = title.ButtonForegroundColor = title.ButtonHoverForegroundColor = title.ButtonPressedForegroundColor = foreground;
        title.InactiveForegroundColor = title.ButtonInactiveForegroundColor = highContrast ? foreground : Color(dark ? (byte)170 : (byte)105);
        title.ButtonHoverBackgroundColor = highContrast ? background : Color(dark ? (byte)55 : (byte)225);
        title.ButtonPressedBackgroundColor = highContrast ? background : Color(dark ? (byte)65 : (byte)215);
    }

    private void UpdatePageLayout()
    {
        var width = PageHost.ActualWidth;
        var horizontal = width is > 0 and < 720 ? 20 : 32;
        PageContent.Padding = new Thickness(horizontal, 24, horizontal, 32);
        PageContent.MaxWidth = _page == "timetable" ? double.PositiveInfinity : 960;
    }

    private static PathIcon FluentIcon(string name, double size = 20)
    {
        var data = (string)Application.Current.Resources["FluentIcon." + name];
        // Geometry belongs to one PathIcon. Parse a fresh instance for each use.
        var icon = (PathIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"" + System.Security.SecurityElement.Escape(data) + "\" />");
        icon.Width = size; icon.Height = size; icon.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        return icon;
    }

    private Button IconButton(string label, string icon, Func<Task> action, string? id = null)
    {
        var button = Button(label, action, id);
        var text = Text(label); text.IsTextSelectionEnabled = false;
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children = { FluentIcon(icon), text }
        };
        return button;
    }
}
