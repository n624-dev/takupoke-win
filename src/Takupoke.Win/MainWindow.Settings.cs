using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        Add(SettingsSectionTitle("データ"));
        Add(SettingsGroup(
            SettingsRow("時間割ファイル", () => OpenPage("materials"), "settings-materials", description: "資料の選択、取得状況、解析結果を確認"),
            SettingsRow("学校行事", () => OpenPage("events"), "settings-events", description: "年度ごとの行事データを取得・確認"),
            SettingsRow("リンク・名称・授業時刻", () => OpenPage("account"), "settings-account",
                _model.RevisionFailures.Count > 0 ? "要確認" : _model.Revisions.Values.Any(value => value.Changed) ? "更新あり" : null, description: "学校アカウントで使うデータを取得・確認")));
        Add(SettingsSectionTitle("表示と操作"));
        var initialMainColor = _model.Preferences.MainColor;
        var mainColor = PreferenceControl(new ComboBox { MinWidth = 155 });
        foreach (var key in UserPreferences.MainColors) mainColor.Items.Add(new ComboBoxItem { Content = UserPreferences.MainColorLabel(key), Tag = key });
        mainColor.SelectedIndex = UserPreferences.MainColors.ToList().IndexOf(initialMainColor);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(mainColor, "main-color");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mainColor, "メインカラー");
        mainColor.SelectionChanged += async (_, _) =>
        {
            if (mainColor.SelectedItem is ComboBoxItem { Tag: string value } && value != initialMainColor
                && value != _model.Preferences.MainColor && mainColor.IsLoaded)
                await _model.SavePreferencesAsync(current => current with { MainColor = value });
        };
        var opening = PreferenceControl(new ComboBox { MinWidth = 155, ItemsSource = new[] { "アプリ内で開く", "既定のブラウザ" }, SelectedIndex = (int)_model.Preferences.OpeningMode });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(opening, "link-opening-mode");
        var initialOpening = opening.SelectedIndex;
        opening.SelectionChanged += async (_, _) =>
        {
            if (opening.IsLoaded && opening.SelectedIndex is >= 0 and <= 1 && opening.SelectedIndex != initialOpening
                && opening.SelectedIndex != (int)_model.Preferences.OpeningMode)
                { var value = (LinkOpeningMode)opening.SelectedIndex; await _model.SavePreferencesAsync(current => current with { OpeningMode = value }); }
        };
        Add(SettingsGroup(
            SettingsRow("クラス", ChooseClasses, "settings-class", _model.Preferences.SelectedClasses.Length == 0 ? "未選択" : string.Join("・", _model.Preferences.SelectedClasses.Select(ClassSelection.Display))),
            SettingsRow("通知・バックグラウンド", () => OpenPage("notifications"), "settings-notifications"),
            SettingField("メインカラー", mainColor), SettingField("リンクの開き方", opening)));
        Add(SettingsSectionTitle("サポート"));
        Add(SettingsGroup(SettingsRow("初期設定", InitialSetup, "settings-setup"),
            SettingsRow("使い方", () => OpenPage("help"), "settings-help"),
            SettingsRow("このアプリについて", () => OpenPage("about"), "settings-about")));
        var storage = Panel(SettingsDescription("アプリ内の保存データと個人設定を置く場所です。選択したOneDriveの原本とは別です。"), Text(_model.Root));
        if (_model.RootMigrationMessage is { } migration) storage.Children.Add(Text(migration));
        Add(new Expander { Header = "データの保存先", Content = storage, HorizontalAlignment = HorizontalAlignment.Stretch });
    }
    private static TextBlock SettingsSectionTitle(string title)
    {
        var text = Text(title, 18); text.FontWeight = FontWeights.SemiBold; return text;
    }
    private static TextBlock SettingsDescription(string value)
    {
        var text = Text(value, 13);
        text.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        return text;
    }
    private static Border SettingsGroup(params UIElement[] rows)
    { var panel = Panel(rows); panel.Spacing = 4; var card = Card(panel); card.Padding = new Thickness(8); return card; }
    private Button SettingsRow(string label, Func<Task> action, string id, string? value = null, string? description = null, string? icon = null)
    {
        var button = Button(label, action, id);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(12); button.MinHeight = 56;
        button.BorderThickness = new Thickness(0); button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        AutomationProperties.SetName(button, label + (value is null ? "" : "、" + value));
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var symbol = FluentIcon(icon ?? (label switch
        {
            "時間割ファイル" => "folder", "学校行事" => "calendar", "リンク・名称・授業時刻" => "globe", "クラス" => "people",
            "通知・バックグラウンド" => "bell", "初期設定" => "settings", "使い方" => "help", "このアプリについて" => "info",
            "ソースコード" => "document", "問い合わせ" => "person", "配布ページを開く" => "download", _ => "document"
        }));
        symbol.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(symbol);
        var heading = Text(label, 15); heading.FontWeight = FontWeights.SemiBold;
        var content = Panel(heading); content.Spacing = 4;
        if (description is not null) content.Children.Add(SettingsDescription(description));
        if (value is not null) content.Children.Add(SettingsDescription(value));
        foreach (var text in content.Children.OfType<TextBlock>()) text.IsTextSelectionEnabled = false;
        Grid.SetColumn(content, 1); row.Children.Add(content);
        var arrow = FluentIcon("chevron-right", 14); arrow.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(arrow, 2); row.Children.Add(arrow);
        button.Content = row; return button;
    }
    private static StackPanel SettingField(string label, Control control)
    {
        var row = Panel(Text(label, 15), control); row.Padding = new Thickness(12); row.Spacing = 8;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.MaxWidth = 420;
        AutomationProperties.SetName(control, label);
        return row;
    }
    private void BuildMaterials()
    {
        TitleText("時間割ファイル", "page-materials"); BackToSettings();
        Add(SettingsDescription("OneDriveの同期フォルダーから資料を選びます。取得や解析の状況は各資料の詳細で確認できます。"));
        foreach (var kind in Enum.GetValues<MaterialKind>()) Add(MaterialCard(kind));
        var year = PreferenceControl(new TextBox { Header = "学校年度", Text = _model.Preferences.DefaultSchoolYear ?? "", PlaceholderText = _model.Today.SchoolYear() + "（自動）", MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch });
        var saveYear = Button("年度を保存", async () =>
        {
            if (year.Text.Trim().Length > 0 && (!int.TryParse(year.Text, out var value) || value is < 1900 or > 9998)) { await Message("年度を確認してください", "1900〜9998の学校年度を入力してください。"); return; }
            var selectedYear = year.Text.Trim();
            await _model.SavePreferencesAsync(current => current with { DefaultSchoolYear = selectedYear.Length == 0 ? null : selectedYear });
        });
        Add(Card(Panel(SettingsSectionTitle("年のない変更日を補完"), SettingsDescription("空欄なら現在の学校年度を使います。1〜3月は翌年の日付として扱います。年度を変えたら、時間割変更の資料を再解析してください。"), year, saveYear)));
        var stop = Button("自動確認を中止", () => { _model.SuspendAutomaticRefresh(); Render(); return Task.CompletedTask; }, "suspend-automatic-refresh");
        stop.IsEnabled = !_model.AutomaticRefreshPaused;
        var updates = Panel(SettingsSectionTitle("資料の更新確認"), SettingsDescription("登録したファイルを確認します。OneDriveの同期が完了しているか、先に確認してください。"),
            OperationButton("登録した原本を確認", _model.RefreshAsync, "refresh-materials"), stop);
        if (_model.AutomaticRefreshPaused)
        {
            var paused = SettingsDescription("自動確認を中止中です。手動確認、資料の選択・再取得・再解析、アプリへの復帰で再開します。");
            AutomationProperties.SetAutomationId(paused, "automatic-refresh-paused"); updates.Children.Add(paused);
        }
        Add(Card(updates));
    }
    private void BuildEventsSettings()
    {
        TitleText("学校行事", "page-events"); BackToSettings();
        Add(SettingsDescription("学校年度ごとの行事データを取得します。保存した行事はホームと時間割に表示します。"));
        if (_model.EventSourceMessage is { } eventWarning) Add(Card(Text(eventWarning)));
        if (_model.EventsUpdateMessage is { } eventFailure) Add(Card(Text(eventFailure)));
        var eventsYear = OperationControl(new NumberBox { Header = "学校年度", Minimum = 1900, Maximum = 9998, Value = _model.Today.SchoolYear(), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch });
        Add(Card(Panel(SettingsSectionTitle("行事データを取得"), eventsYear,
            OperationButton("選んだ年度の行事を取得", () => _model.FetchEventsAsync(double.IsNaN(eventsYear.Value) ? _model.Today.SchoolYear() : (int)eventsYear.Value), "fetch-events"))));
        Add(SettingsSectionTitle("保存済みの年度"));
        if (_model.SavedEventYears.Count == 0) Add(Card(SettingsDescription("行事データをまだ取得していません。上で学校年度を選んで取得してください。")));
        else Add(SettingsGroup(_model.SavedEventYears.Select(savedYear => SettingsRow(savedYear + "年度", () => EventDetails(savedYear), "events-details-" + savedYear, "取得済み", "行事と取得状況を確認")).Cast<UIElement>().ToArray()));
    }
    private void BuildNotificationSettings()
    {
        TitleText("通知・バックグラウンド", "page-notifications"); BackToSettings();
        Add(Card(Panel(SettingsSectionTitle("通知の状態"), SettingsDescription(_model.NotificationStatus))));
        var changes = PreferenceControl(new ToggleSwitch { Header = "時間割変更", IsOn = _model.Preferences.NotifyChanges });
        changes.Toggled += async (_, _) => { var enabled = changes.IsOn; if (enabled == _model.Preferences.NotifyChanges) return; await _model.SavePreferencesAsync(current => current with { NotifyChanges = enabled, NotificationsSetupCompleted = true }); };
        var special = PreferenceControl(new ToggleSwitch { Header = "試験・返却", IsOn = _model.Preferences.NotifySpecials });
        special.Toggled += async (_, _) => { var enabled = special.IsOn; if (enabled == _model.Preferences.NotifySpecials) return; await _model.SavePreferencesAsync(current => current with { NotifySpecials = enabled, NotificationsSetupCompleted = true }); };
        Add(Card(Panel(SettingsSectionTitle("通知する更新"), SettingsDescription("選択中のクラスの変更と、正常に解析できた試験・返却の更新を通知します。初回の取り込みは通知しません。"), changes, special)));
        Add(SettingsSectionTitle("バックグラウンド"));
        var tray = new ToggleSwitch { Header = "通知領域に常駐", IsOn = _model.Preferences.KeepInTray, IsEnabled = _desktop is not null };
        tray.Toggled += async (_, _) => { var enabled = tray.IsOn; if (enabled == _model.Preferences.KeepInTray) return; try { _desktop?.SetTray(enabled); await _model.SavePreferencesAsync(current => current with { KeepInTray = enabled }); } catch { await Message("常駐を設定できません", "通知領域にアイコンを登録できませんでした。"); } };
        var startup = new ToggleSwitch { Header = "Windowsへのサインイン時に起動", IsOn = _model.Preferences.AutoStart, IsEnabled = !_model.OfflineTest };
        startup.Toggled += async (_, _) => { var enabled = startup.IsOn; if (enabled == _model.Preferences.AutoStart) return; try { DesktopIntegration.SetAutoStart(enabled); await _model.SavePreferencesAsync(current => current with { AutoStart = enabled }); } catch { await Message("自動起動を設定できません", "Windowsの設定を確認してください。"); } };
        Add(Card(Panel(tray, SettingsDescription("ウィンドウを閉じても動作を続け、15分ごとに資料を確認します。完全終了・スリープ中は確認しません。"), startup)));
        Add(Button("アプリを完全に終了", () => { _exitRequested = true; Close(); return Task.CompletedTask; }));
    }
    private void BuildAbout()
    {
        TitleText("このアプリについて", "page-about"); BackToSettings();
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "開発版";
        Add(Card(Panel(SettingsSectionTitle("たくポケ"), Text("香川高専詫間キャンパスの学生向けに個人が開発・運営する非公式アプリです。"),
            SettingsDescription("バージョン：" + version + "\nビルド：" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "—")))));
        Add(SettingsSectionTitle("規約とライセンス"));
        Add(SettingsGroup(
            SettingsRow("利用規約", () => ShowProductDocument("利用規約", Path.Combine("Legal", "terms.txt")), "利用規約"),
            SettingsRow("プライバシーポリシー", () => ShowProductDocument("プライバシーポリシー", Path.Combine("Legal", "privacy.txt")), "プライバシーポリシー"),
            SettingsRow("たくにん利用規約", () => OpenBrowser(new("https://takuma-gakunin.n624.jp/terms"), "たくにん利用規約"), "たくにん利用規約"),
            SettingsRow("たくにんプライバシーポリシー", () => OpenBrowser(new("https://takuma-gakunin.n624.jp/privacy"), "たくにんプライバシーポリシー"), "たくにんプライバシーポリシー"),
            SettingsRow("依存ライブラリのライセンス", () => OpenPage("licenses"), "依存ライブラリのライセンス")));
        Add(SettingsSectionTitle("開発とサポート"));
        Add(SettingsGroup(
            SettingsRow("ソースコード", () => OpenBrowser(new("https://github.com/n624-dev/takupoke-win"), "ソースコード"), "about-source", description: "GitHubで公開しています"),
            SettingsRow("問い合わせ", async () => { if (!await Windows.System.Launcher.LaunchUriAsync(new("mailto:takupoke@n624.jp"))) await Message("メールアプリを開けませんでした", "メールアプリから takupoke@n624.jp へお問い合わせください。"); }, "about-contact", description: "メールで問い合わせる"),
            SettingsRow("配布ページを開く", () => OpenBrowser(new("https://github.com/n624-dev/takupoke-win/releases"), "配布ページ"), "配布ページを開く", description: "開発確認版をダウンロード")));
        Add(Button("配布URLをコピー", () => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText("https://github.com/n624-dev/takupoke-win/releases"); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); return Task.CompletedTask; }));
    }
    private async Task ShowProductDocument(string title, string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relative);
        await Message(title, File.Exists(path) ? await File.ReadAllTextAsync(path) : "この文書を読み込めませんでした。配布版を再インストールして、もう一度お試しください。");
    }
    private Border MaterialCard(MaterialKind kind)
    {
        var snapshot = _model.Materials.GetValueOrDefault(kind); var source = snapshot?.Source;
        var state = source is null ? "資料を選択していません。" : snapshot?.AcquisitionAttempt?.Failure is not null ? "ファイルを取得できませんでした。詳細を確認してください。"
            : snapshot?.ParseAttempt?.Failure is not null ? "解析できませんでした。詳細を確認してください。" : snapshot?.Analysis is null ? "解析結果はまだありません。" : "解析結果を保存しています。";
        var summary = SettingsDescription(source is null ? state : source.OriginalName + "\n" + state);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(summary, "material-summary-" + kind);
        if (snapshot?.AcquisitionAttempt?.Failure is not null || snapshot?.ParseAttempt?.Failure is not null) summary.Foreground = WarningBrush;
        return Card(Panel(SettingsSectionTitle(AppViewModel.MaterialLabel(kind)), summary,
            OperationButton(source is null ? "資料を選択" : "資料を選び直す", () => SelectMaterial(kind), "select-material-" + kind),
            Button("詳細を見る", () => OpenPage("material." + kind), "material-details-" + kind)));
    }
    private Task ShowAnalysis(MaterialKind kind) => OpenPage("analysis." + kind);
}
