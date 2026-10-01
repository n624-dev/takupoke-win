using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Takupoke.Core;
using Takupoke.Win.ViewModels;
using Takupoke.Win.Platform;
using Windows.Storage.Pickers;

namespace Takupoke.Win;

public sealed partial class MainWindow : Window
{
    private readonly AppViewModel _model;
    private string _page = "home";
    private bool _dialogOpen;
    private bool _ready;
    private DesktopIntegration? _desktop;
    private bool _exitRequested;
    public MainWindow()
    {
        InitializeComponent();
        _model = new(DispatcherQueue);
        _model.SnapshotChanged += Render;
        _model.PropertyChanged += (_, _) => UpdateStatus();
        _model.PrivateDataCleared += () => { CloseBrowser(); if (_pdfImage is not null) _pdfImage.Source = null; _pdfDialog?.Hide(); Render(); };
        _model.NotificationActivated += () => { _model.WeekStart = _model.Today.DisplayWeekStart(); Navigation.SelectedItem = Navigation.MenuItems[2]; ShowWindow(); };
        AppWindow.Resize(new(1150, 820));
        Navigation.SelectedItem = Navigation.MenuItems[0];
        RootGrid.Loaded += Loaded;
        Activated += (_, args) => { if (_ready && args.WindowActivationState != WindowActivationState.Deactivated) _ = _model.RefreshAsync(); };
        AppWindow.Closing += (_, args) =>
        { if (!_exitRequested && _model.Preferences.KeepInTray && _desktop?.TrayAvailable == true) { args.Cancel = true; AppWindow.Hide(); } };
        Closed += async (_, _) => { CloseBrowser(); _desktop?.Dispose(); Program.OpenRequested = null; await _model.DisposeAsync(); };
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
        if (!_model.Preferences.SetupCompleted && !_model.OfflineTest) await InitialSetup();
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
        if (_dialogOpen) { UpdateStatus(); return; }
        var focused = RootGrid.XamlRoot is null ? null : FocusManager.GetFocusedElement(RootGrid.XamlRoot) as FrameworkElement;
        var focusId = focused is null ? "" : AutomationProperties.GetAutomationId(focused);
        PageContent.Children.Clear();
        switch (_page) { case "links": BuildLinks(); break; case "timetable": BuildTimetable(); break; case "settings": BuildSettings(); break; default: BuildHome(); break; }
        UpdateStatus();
        if (focusId.Length > 0) FindById(PageContent, focusId)?.Focus(FocusState.Programmatic);
    }
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
            return await dialog.ShowAsync();
        }
        finally { _dialogOpen = false; Render(); }
    }
    private Task Message(string title, string message) => Dialog(title, Text(message));
    private async Task SelectMaterial(MaterialKind kind)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(kind == MaterialKind.Changes ? ".xlsx" : ".pdf");
        var file = await picker.PickSingleFileAsync();
        if (file is not null && file.Path.Length > 0) await _model.SelectAsync(kind, file.Path);
    }
    private async Task InitialSetup()
    {
        var body = Panel(Text("学校資料を端末内で解析する非公式アプリです。クラスと資料は設定からいつでも選び直せます。"),
            Text("学校データは日本時間の4月1日・10月1日で削除し、再選択・再取得が必要です。お気に入りなどの個人設定とOneDriveの原本は保持します。"),
            Text("まずクラスを選び、通常時間割・変更・試験・返却の資料を個別に登録してください。学校アカウントの認証後にリンク・名称・授業時刻を取得できます。"),
            Text("通知は設定で有効にできます。完全終了中・電源断・スリープ中の更新確認は保証しません。"));
        var result = await Dialog("はじめに", body, "設定を開く", "あとで設定");
        await _model.SavePreferencesAsync(_model.Preferences with { SetupCompleted = true });
        if (result == ContentDialogResult.Primary) { _page = "settings"; Navigation.SelectedItem = Navigation.MenuItems[3]; Render(); }
    }
}
