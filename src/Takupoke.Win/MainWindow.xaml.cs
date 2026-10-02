using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Takupoke.Core;
using Takupoke.Win.ViewModels;
using Takupoke.Win.Platform;
using Microsoft.Windows.Storage.Pickers;

namespace Takupoke.Win;

public sealed partial class MainWindow : Window
{
    private readonly AppViewModel _model;
    private string _page = "home";
    private string? _renderedPage;
    private bool _dialogOpen;
    private ContentDialog? _activeDialog;
    private bool _ready;
    private bool _selectingMaterial;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _themeTimer;
    private bool _lastHighContrast;
    private DesktopIntegration? _desktop;
    private bool _exitRequested;
    private bool _initialSetupOffered;
    public MainWindow()
    {
        InitializeComponent();
        _model = new(DispatcherQueue);
        // HighContrastChanged subscription fails for some unpackaged installations.
        // Poll only the display setting; native ThemeResources still follow Windows.
        _lastHighContrast = _accessibility.HighContrast;
        _themeTimer = DispatcherQueue.CreateTimer(); _themeTimer.Interval = TimeSpan.FromSeconds(15);
        _themeTimer.Tick += (_, _) => { var current = _accessibility.HighContrast; if (current != _lastHighContrast) { _lastHighContrast = current; if (_ready) Render(); } };
        _themeTimer.Start();
        RootGrid.ActualThemeChanged += (_, _) => { if (_ready) Render(); };
        _model.SnapshotChanged += () => { Render(); OfferInitialSetup(); };
        _model.ClockChanged += () => { if (_page is "home" or "timetable" && !_model.Busy && !_selectingMaterial) Render(); };
        _model.PropertyChanged += (_, _) => UpdateStatus();
        _model.PrivateDataCleared += () => { if (_activeDialog is { } active) { active.Content = null; active.Hide(); } CloseBrowser(); if (_pdfImage is not null) _pdfImage.Source = null; _pdfDialog?.Hide(); Render(); };
        _model.NotificationActivated += () => { _model.OpenTodayWeek(); Navigation.SelectedItem = Navigation.MenuItems[2]; ShowWindow(); };
        var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        if (display is not null)
        {
            var work = display.WorkArea; var width = Math.Min(1150, work.Width); var height = Math.Min(820, work.Height);
            AppWindow.MoveAndResize(new(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        }
        else AppWindow.Resize(new(1150, 820));
        Navigation.SelectedItem = Navigation.MenuItems[0];
        RootGrid.Loaded += Loaded;
        Activated += (_, args) => { if (_ready && !_selectingMaterial && args.WindowActivationState != WindowActivationState.Deactivated) _ = _model.RefreshAsync(); };
        AppWindow.Closing += (_, args) =>
        { if (!_exitRequested && _model.Preferences.KeepInTray && _desktop?.TrayAvailable == true) { args.Cancel = true; AppWindow.Hide(); } };
        Closed += async (_, _) => { _themeTimer?.Stop(); CloseBrowser(); _desktop?.Dispose(); Program.OpenRequested = null; await _model.DisposeAsync(); };
        Program.OpenRequested = () => DispatcherQueue.TryEnqueue(ShowWindow);
    }
    private async void Loaded(object sender, RoutedEventArgs args)
    {
        if (_ready) return;
        _ready = true;
        if (!_model.OfflineTest)
        {
            try
            {
                _desktop = new(WinRT.Interop.WindowNative.GetWindowHandle(this));
                _desktop.LockedChanged += locked => _ = _model.SetLockedAsync(locked);
                if (!DesktopIntegration.IsInputDesktopAccessible()) await _model.SetLockedAsync(true);
                _desktop.Resumed += () => _ = _model.RefreshAsync(); _desktop.Suspended += _model.Cancel;
                _desktop.OpenRequested += ShowWindow; _desktop.ExitRequested += () => { _exitRequested = true; Close(); };
            }
            catch { await _model.SetLockedAsync(true); await Message("Windowsとの連携を開始できません", "ロック通知を受け取れないため学校データの利用を停止しました。通常ユーザー権限で再起動してください。"); }
        }
        await _model.InitializeAsync();
        if (_desktop is not null)
        {
            try { _desktop.SetTray(_model.Preferences.KeepInTray); }
            catch { await Message("通知領域に表示できません", "ウィンドウを閉じると完全終了します。Windowsの通知領域を確認してください。"); }
            if (_desktop.TrayAvailable && Environment.GetCommandLineArgs().Contains("--background")) AppWindow.Hide();
        }
        OfferInitialSetup();
    }
    private void OfferInitialSetup()
    {
        if (_ready && !_initialSetupOffered && !_model.OfflineTest && !_model.Locked && !_model.Busy && _model.PreferencesReady && !_model.Preferences.SetupCompleted)
            _ = InitialSetup();
    }
    private void ShowWindow() { AppWindow.Show(); Activate(); }
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item) _page = (string)item.Tag;
        if (_model is not null) Render();
    }
    private void Cancel_Click(object sender, RoutedEventArgs args) => _model.Cancel();
    private void UpdateStatus()
    {
        StatusText.Text = _model.Status;
        Activity.Visibility = CancelButton.Visibility = _model.Busy ? Visibility.Visible : Visibility.Collapsed;
        PageHost.IsEnabled = !_model.Busy;
    }
    private void Render()
    {
        if (_dialogOpen || _selectingMaterial) { UpdateStatus(); return; }
        var pageChanged = _renderedPage != _page; _renderedPage = _page;
        var focused = RootGrid.XamlRoot is null ? null : FocusManager.GetFocusedElement(RootGrid.XamlRoot) as FrameworkElement;
        var focusId = focused is null ? "" : AutomationProperties.GetAutomationId(focused);
        // Keep body text on theme brushes; tint only standard controls and explicit actions.
        RootGrid.Resources.ThemeDictionaries.Clear();
        if (_model.Preferences.MainColor != "default")
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var tint = MainAccentColor();
                var resources = new ResourceDictionary();
                resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(tint);
                resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(tint) { Opacity = 0.9 };
                resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(tint) { Opacity = 0.8 };
                resources["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(tint);
                RootGrid.Resources.ThemeDictionaries[theme] = resources;
            }
        // Explicitly retain the standard WinUI high-contrast palette even with a selected tint.
        if (_model.Preferences.MainColor != "default")
        {
            var highContrast = new ResourceDictionary();
            foreach (var key in new[] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "AccentTextFillColorPrimaryBrush" })
                highContrast[key] = Application.Current.Resources[key];
            RootGrid.Resources.ThemeDictionaries["HighContrast"] = highContrast;
        }
        foreach (var theme in new[] { "Light", "Dark" })
            ((SolidColorBrush)((ResourceDictionary)Navigation.Resources.ThemeDictionaries[theme])["NavigationViewSelectionIndicatorForeground"]).Color = MainAccentColor();
        PageContent.Children.Clear();
        if (_page.StartsWith("material.", StringComparison.Ordinal) && Enum.TryParse<MaterialKind>(_page[9..], out var material)) BuildMaterialDetails(material);
        else if (_page.StartsWith("analysis.", StringComparison.Ordinal) && Enum.TryParse<MaterialKind>(_page[9..], out var analysed)) BuildAnalysis(analysed);
        else if (_page == "account") BuildAccountData();
        else if (_page == "setup") BuildSetup();
        else if (_page == "help") BuildHelp();
        else if (_page == "licenses") BuildLicenses();
        else switch (_page) { case "links": BuildLinks(); break; case "timetable": BuildTimetable(); break; case "settings": BuildSettings(); break; default: BuildHome(); break; }
        UpdateStatus();
        if (pageChanged)
        {
            var page = _page;
            DispatcherQueue.TryEnqueue(() => { if (_page == page) PageScroller.ChangeView(0, 0, null, true); });
        }
        if (focusId.Length > 0) FindById(PageContent, focusId)?.Focus(FocusState.Programmatic);
    }
    private Windows.UI.Color MainAccentColor() => _model.Preferences.MainColor == "default"
        ? _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent) : LinkColor(_model.Preferences.MainColor);
    private Brush ActionBrush => _accessibility.HighContrast
        ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] : new SolidColorBrush(MainAccentColor());
    private Brush WarningBrush => _accessibility.HighContrast
        ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] : new SolidColorBrush(LinkColor("orange"));
    private Button AccentButton(string label, Func<Task> action, string? id = null)
    { var button = Button(label, action, id); button.Foreground = ActionBrush; return button; }
    private static Control? FindById(DependencyObject root, string id)
    {
        if (root is Control control && AutomationProperties.GetAutomationId(control) == id) return control;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindById(VisualTreeHelper.GetChild(root, index), id) is { } result) return result;
        return null;
    }
    private static TextBlock Text(string value, double size = 14) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static StackPanel Panel(params UIElement[] elements)
    { var panel = new StackPanel { Spacing = 10 }; foreach (var element in elements) panel.Children.Add(element); return panel; }
    private Button Button(string label, Func<Task> action, string? id = null)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(button, id ?? label);
        button.Click += async (_, _) => { try { await action(); } catch { if (!_dialogOpen) await Message("処理を完了できませんでした", "保存情報または選択した資料を確認して、もう一度お試しください。"); } };
        return button;
    }
    private void Add(UIElement element) => PageContent.Children.Add(element);
    private void TitleText(string title, string id) { var heading = Text(title, 28); AutomationProperties.SetAutomationId(heading, id); Add(heading); }
    private static Border Card(UIElement content) => new() { Child = content, Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1), BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"] };
    private async Task<ContentDialogResult> Dialog(string title, UIElement content, string primary = "閉じる", string? secondary = null)
    {
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = title,
                Content = new ScrollViewer { Content = content, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = primary, CloseButtonText = secondary ?? "", DefaultButton = ContentDialogButton.Primary };
            _activeDialog = dialog;
            return await dialog.ShowAsync();
        }
        finally { _activeDialog = null; _dialogOpen = false; Render(); }
    }
    private Task Message(string title, string message) => Dialog(title, Text(message));
    private async Task SelectMaterial(MaterialKind kind)
    {
        _selectingMaterial = true;
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id) { Title = AppViewModel.MaterialLabel(kind) + "を選択 — OneDriveの同期フォルダーにある原本", SettingsIdentifier = "takupoke-" + kind };
            picker.FileTypeFilter.Add(kind == MaterialKind.Changes ? ".xlsx" : ".pdf");
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                if (string.IsNullOrWhiteSpace(file.Path))
                    await Message("原本の場所を確認できません", "OneDriveの同期フォルダーにあるPDF・XLSXを選択してください。選択情報は変更していません。");
                else await _model.SelectAsync(kind, file.Path);
            }
        }
        finally { _selectingMaterial = false; Render(); }
    }
    private Task InitialSetup()
    {
        _initialSetupOffered = true; _setupStep = 0; return OpenPage("setup");
    }
}
