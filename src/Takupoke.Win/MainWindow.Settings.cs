using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Takupoke.Core;
using Takupoke.Win.ViewModels;
using Takupoke.Win.Platform;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private void BuildSettings()
    {
        TitleText("設定", "page-settings");
        Add(Text("データ", 20));
        Add(Card(Panel(
            SettingsRow("時間割ファイル", () => OpenPage("materials"), "settings-materials"),
            SettingsRow("学校行事", () => OpenPage("events"), "settings-events"),
            SettingsRow("リンク・名称・授業時刻", () => OpenPage("account"), "settings-account",
                _model.Revisions.Values.Any(value => value.Changed) ? "更新あり" : null))));
        Add(Text("アプリ設定", 20));
        var initialMainColor = _model.Preferences.MainColor;
        var mainColor = OperationControl(new ComboBox { MinWidth = 155 });
        foreach (var key in UserPreferences.MainColors) mainColor.Items.Add(new ComboBoxItem { Content = UserPreferences.MainColorLabel(key), Tag = key });
        mainColor.SelectedIndex = UserPreferences.MainColors.ToList().IndexOf(initialMainColor);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(mainColor, "main-color");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mainColor, "メインカラー");
        mainColor.SelectionChanged += async (_, _) =>
        {
            if (mainColor.SelectedItem is ComboBoxItem { Tag: string value } && value != initialMainColor
                && value != _model.Preferences.MainColor && mainColor.IsLoaded)
                await _model.SavePreferencesAsync(_model.Preferences with { MainColor = value });
        };
        var opening = OperationControl(new ComboBox { MinWidth = 155, ItemsSource = new[] { "アプリ内で開く", "デフォルトのブラウザ" }, SelectedIndex = (int)_model.Preferences.OpeningMode });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(opening, "link-opening-mode");
        var initialOpening = opening.SelectedIndex;
        opening.SelectionChanged += async (_, _) =>
        {
            if (opening.IsLoaded && opening.SelectedIndex is >= 0 and <= 1 && opening.SelectedIndex != initialOpening
                && opening.SelectedIndex != (int)_model.Preferences.OpeningMode)
                await _model.SavePreferencesAsync(_model.Preferences with { OpeningMode = (LinkOpeningMode)opening.SelectedIndex });
        };
        Add(Card(Panel(
            SettingsRow("クラス", ChooseClasses, "settings-class", _model.Preferences.SelectedClasses.Length == 0 ? "未選択" : string.Join("・", _model.Preferences.SelectedClasses.Select(ClassSelection.Display))),
            SettingsRow("通知・バックグラウンド", () => OpenPage("notifications"), "settings-notifications"),
            SettingField("メインカラー", mainColor), SettingField("リンクの開き方", opening))));
        Add(Text("サポート", 20));
        Add(Card(Panel(SettingsRow("初期設定", InitialSetup, "settings-setup"),
            SettingsRow("使い方", () => OpenPage("help"), "settings-help"),
            SettingsRow("このアプリについて", () => OpenPage("about"), "settings-about"))));
    }
    private Button SettingsRow(string label, Func<Task> action, string id, string? value = null)
    {
        var button = Button(label, action, id); button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.MinHeight = 44;
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(Text(label, 15));
        if (value is not null) { var summary = Text(value, 13); Grid.SetColumn(summary, 1); row.Children.Add(summary); }
        var arrow = new FontIcon { Glyph = "\uE76C", FontSize = 12, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(arrow, 2); row.Children.Add(arrow);
        foreach (var text in row.Children.OfType<TextBlock>()) text.IsTextSelectionEnabled = false;
        button.Content = row; return button;
    }
    private static Grid SettingField(string label, Control control)
    {
        var row = new Grid { ColumnSpacing = 16, MinHeight = 44 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = Text(label, 15); text.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(text);
        Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    private void BuildMaterials()
    {
        TitleText("時間割ファイル", "page-materials"); BackToSettings();
        foreach (var kind in Enum.GetValues<MaterialKind>()) Add(MaterialCard(kind));
        var year = OperationControl(new TextBox { Header = "学校年度", Text = _model.Preferences.DefaultSchoolYear ?? "", PlaceholderText = _model.Today.SchoolYear().ToString() });
        Add(year); Add(OperationButton("年度を保存", async () =>
        {
            if (year.Text.Trim().Length > 0 && (!int.TryParse(year.Text, out var value) || value is < 1900 or > 9998)) { await Message("年度を確認してください", "1900〜9998の学校年度を入力してください。"); return; }
            await _model.SavePreferencesAsync(_model.Preferences with { DefaultSchoolYear = year.Text.Trim().Length == 0 ? null : year.Text.Trim() });
        }));
        Add(OperationButton("登録した原本を確認", _model.RefreshAsync, "refresh-materials"));
    }
    private void BuildEventsSettings()
    {
        TitleText("学校行事", "page-events"); BackToSettings();
        if (_model.EventSourceMessage is { } eventWarning) Add(Card(Text(eventWarning)));
        if (_model.EventsUpdateMessage is { } eventFailure) Add(Card(Text(eventFailure)));
        var eventsYear = OperationControl(new NumberBox { Header = "学校年度", Minimum = 1900, Maximum = 9998, Value = _model.Today.SchoolYear(), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline });
        Add(eventsYear); Add(OperationButton("取得", () => _model.FetchEventsAsync(double.IsNaN(eventsYear.Value) ? _model.Today.SchoolYear() : (int)eventsYear.Value), "fetch-events"));
        foreach (var savedYear in _model.SavedEventYears) Add(SettingsRow(savedYear + "年度", () => EventDetails(savedYear), "events-details-" + savedYear, "取得済み"));
    }
    private void BuildNotificationSettings()
    {
        TitleText("通知・バックグラウンド", "page-notifications"); BackToSettings();
        Add(Text(_model.NotificationStatus));
        var changes = OperationControl(new ToggleSwitch { Header = "時間割変更", IsOn = _model.Preferences.NotifyChanges });
        changes.Toggled += async (_, _) => { if (changes.IsOn == _model.Preferences.NotifyChanges) return; await _model.SavePreferencesAsync(_model.Preferences with { NotifyChanges = changes.IsOn, NotificationsSetupCompleted = true }); }; Add(changes);
        var special = OperationControl(new ToggleSwitch { Header = "試験・返却", IsOn = _model.Preferences.NotifySpecials });
        special.Toggled += async (_, _) => { if (special.IsOn == _model.Preferences.NotifySpecials) return; await _model.SavePreferencesAsync(_model.Preferences with { NotifySpecials = special.IsOn, NotificationsSetupCompleted = true }); }; Add(special);
        Add(Text("バックグラウンド", 20));
        var tray = new ToggleSwitch { Header = "通知領域に常駐", IsOn = _model.Preferences.KeepInTray, IsEnabled = _desktop is not null };
        tray.Toggled += async (_, _) => { if (tray.IsOn == _model.Preferences.KeepInTray) return; try { _desktop?.SetTray(tray.IsOn); await _model.SavePreferencesAsync(_model.Preferences with { KeepInTray = tray.IsOn }); } catch { await Message("常駐を設定できません", "通知領域にアイコンを登録できませんでした。"); } }; Add(tray);
        var startup = new ToggleSwitch { Header = "Windowsへのサインイン時に起動", IsOn = _model.Preferences.AutoStart, IsEnabled = !_model.OfflineTest };
        startup.Toggled += async (_, _) => { if (startup.IsOn == _model.Preferences.AutoStart) return; try { DesktopIntegration.SetAutoStart(startup.IsOn); await _model.SavePreferencesAsync(_model.Preferences with { AutoStart = startup.IsOn }); } catch { await Message("自動起動を設定できません", "Windowsの設定を確認してください。"); } }; Add(startup);
        Add(Button("アプリを完全に終了", () => { _exitRequested = true; Close(); return Task.CompletedTask; }));
    }
    private void BuildAbout()
    {
        TitleText("このアプリについて", "page-about"); BackToSettings();
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "開発版";
        Add(Text("たくポケ Win · " + version, 20));
        Add(Button("利用規約", () => ShowProductDocument("利用規約", Path.Combine("Legal", "terms.txt"))));
        Add(Button("プライバシーポリシー", () => ShowProductDocument("プライバシーポリシー", Path.Combine("Legal", "privacy.txt"))));
        Add(Button("たくにん利用規約", () => OpenBrowser(new("https://takuma-gakunin.n624.jp/terms"), "たくにん利用規約")));
        Add(Button("たくにんプライバシーポリシー", () => OpenBrowser(new("https://takuma-gakunin.n624.jp/privacy"), "たくにんプライバシーポリシー")));
        Add(Button("依存ライブラリのライセンス", () => OpenPage("licenses")));
        Add(Button("配布ページを開く", () => OpenBrowser(new("https://github.com/n624-dev/takupoke-win/releases"), "配布ページ")));
        Add(Button("配布URLをコピー", () => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText("https://github.com/n624-dev/takupoke-win/releases"); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); return Task.CompletedTask; }));
    }
    private async Task ShowProductDocument(string title, string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relative);
        await Message(title, File.Exists(path) ? await File.ReadAllTextAsync(path) : "開発ソースからの起動では配布用文書が含まれない場合があります。依存ライセンスは配布パッケージに同梱します。プロジェクト自体のライセンスは未選定です。");
    }
    private Border MaterialCard(MaterialKind kind)
    {
        var snapshot = _model.Materials.GetValueOrDefault(kind); var source = snapshot?.Source;
        var state = source is null ? "資料を選択していません。" : snapshot?.AcquisitionAttempt?.Failure is not null ? "原本を確認できません。"
            : snapshot?.ParseAttempt?.Failure is not null ? "解析を確認してください。" : snapshot?.Analysis is null ? "未解析です。" : "解析結果を保存しています。";
        var summary = Text(source is null ? state : source.OriginalName + "\n" + state);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(summary, "material-summary-" + kind);
        return Card(Panel(Text(AppViewModel.MaterialLabel(kind), 18), summary,
            OperationButton(source is null ? "資料を選択" : "資料を選び直す", () => SelectMaterial(kind), "select-material-" + kind),
            Button("詳細を見る", () => OpenPage("material." + kind), "material-details-" + kind)));
    }
    private Task ShowAnalysis(MaterialKind kind) => OpenPage("analysis." + kind);
}
