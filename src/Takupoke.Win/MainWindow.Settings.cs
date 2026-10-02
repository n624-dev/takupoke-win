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
        Add(Button("クラスを選択", ChooseClasses));
        var mainColor = new ComboBox { Header = "メインカラー", ItemsSource = UserPreferences.MainColors, SelectedItem = _model.Preferences.MainColor };
        mainColor.SelectionChanged += async (_, _) => { if (mainColor.SelectedItem is string value && value != _model.Preferences.MainColor) await _model.SavePreferencesAsync(_model.Preferences with { MainColor = value }); }; Add(mainColor);
        var opening = new ComboBox { Header = "リンクの開き方", ItemsSource = new[] { "アプリ内ブラウザ", "外部ブラウザ" }, SelectedIndex = (int)_model.Preferences.OpeningMode };
        opening.SelectionChanged += async (_, _) => { if (opening.SelectedIndex is >= 0 and <= 1 && opening.SelectedIndex != (int)_model.Preferences.OpeningMode) await _model.SavePreferencesAsync(_model.Preferences with { OpeningMode = (LinkOpeningMode)opening.SelectedIndex }); }; Add(opening);
        Add(Text("学校資料", 22));
        Add(Text("OneDriveの同期フォルダーにある資料を選択してください。オフラインでも原本を読むには、OneDriveの「このデバイス上で常に保持する」を利用できます。"));
        foreach (var kind in Enum.GetValues<MaterialKind>()) Add(MaterialCard(kind));
        var year = new TextBox { Header = "時間割変更の省略日付に使う学校年度（空欄は現在の学校年度）", Text = _model.Preferences.DefaultSchoolYear ?? "", PlaceholderText = _model.Today.SchoolYear().ToString() };
        Add(year); Add(Button("年度を保存", async () =>
        {
            if (year.Text.Trim().Length > 0 && (!int.TryParse(year.Text, out var value) || value is < 1900 or > 9998)) { await Message("年度を確認してください", "1900〜9998の学校年度を入力してください。"); return; }
            await _model.SavePreferencesAsync(_model.Preferences with { DefaultSchoolYear = year.Text.Trim().Length == 0 ? null : year.Text.Trim() });
        }));
        Add(Text("データの取得・更新", 22)); Add(Button("公開更新情報と登録済み資料を確認", _model.RefreshAsync));
        foreach (var kind in Enum.GetValues<Takupoke.Infrastructure.Api.DataSet>())
        {
            var state = _model.Revisions.GetValueOrDefault(kind);
            Add(Text(AppViewModel.DataSetLabel(kind) + "：" + (state is null ? "確認前または取得不可" : state.Changed ? "取得・更新できます" : "保存済みの版です")));
        }
        Add(Button("学校アカウントでリンク・名称・授業時刻を取得", _model.UpdateSharedAsync));
        Add(Text("学校行事", 22));
        if (_model.EventSourceMessage is { } eventWarning) Add(Card(Text(eventWarning)));
        if (_model.EventsUpdateMessage is { } eventFailure) Add(Card(Text(eventFailure)));
        var eventsYear = new NumberBox { Header = "取得する学校年度", Minimum = 1900, Maximum = 9998, Value = _model.Today.SchoolYear(), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        Add(eventsYear); Add(Button("この年度の学校行事を取得", () => _model.FetchEventsAsync((int)eventsYear.Value)));
        foreach (var savedYear in _model.SavedEventYears) Add(Button(savedYear + "年度の学校行事を表示", async () =>
        { var items = _model.Data.Events?.Where(e => SchoolDate.TryParse(e.Date, out var day) && day.SchoolYear() == savedYear).OrderBy(e => e.Date).ToArray() ?? []; await Dialog(savedYear + "年度の学校行事", Text(string.Join("\n", items.Select(e => e.Date + (e.EndDate is { } end ? "〜" + end : "") + " · " + e.Title + " · " + e.Tag)))); }));
        Add(Text("通知・バックグラウンド", 22));
        Add(Text(_model.NotificationStatus));
        Add(Text("選択中クラスの今日以降の変更と、解析に成功した試験・返却PDFの更新を通知します。初回取り込みは比較基準の保存だけです。"));
        var changes = new ToggleSwitch { Header = "時間割変更の通知", IsOn = _model.Preferences.NotifyChanges };
        changes.Toggled += async (_, _) => await _model.SavePreferencesAsync(_model.Preferences with { NotifyChanges = changes.IsOn, NotificationsSetupCompleted = true }); Add(changes);
        var special = new ToggleSwitch { Header = "試験・返却PDF更新の通知", IsOn = _model.Preferences.NotifySpecials };
        special.Toggled += async (_, _) => await _model.SavePreferencesAsync(_model.Preferences with { NotifySpecials = special.IsOn, NotificationsSetupCompleted = true }); Add(special);
        var tray = new ToggleSwitch { Header = "ウィンドウを閉じても通知領域で確認を続ける", IsOn = _model.Preferences.KeepInTray, IsEnabled = _desktop is not null };
        tray.Toggled += async (_, _) => { try { _desktop?.SetTray(tray.IsOn); await _model.SavePreferencesAsync(_model.Preferences with { KeepInTray = tray.IsOn }); } catch { await Message("常駐を設定できません", "通知領域にアイコンを登録できませんでした。ウィンドウを閉じると完全終了します。"); } }; Add(tray);
        var startup = new ToggleSwitch { Header = "Windowsへのサインイン時に自動起動", IsOn = _model.Preferences.AutoStart, IsEnabled = !_model.OfflineTest };
        startup.Toggled += async (_, _) => { try { DesktopIntegration.SetAutoStart(startup.IsOn); await _model.SavePreferencesAsync(_model.Preferences with { AutoStart = startup.IsOn }); } catch { await Message("自動起動を設定できません", "Windowsの設定またはアプリの実行ファイルを確認してください。"); } }; Add(startup);
        Add(Button("アプリを完全に終了", () => { _exitRequested = true; Close(); return Task.CompletedTask; }));
        Add(Text("常駐中は15分ごとに確認し、スリープ復帰時にも確認します。完全終了・電源断・スリープ中の定刻確認は保証しません。自動起動はWindowsの「スタートアップ アプリ」からも変更できます。"));
        Add(Text("保存期限", 22)); Add(Text("学校データは日本時間4月1日・10月1日に削除します。資料のアプリ内コピー・選択情報・解析結果・学校用データが対象です。個人設定・公開行事・OneDrive上の原本は保持します。"));
        Add(Button("初期設定をもう一度表示", InitialSetup)); Add(Button("使い方", () => Message("使い方", "クラスを選び、4種類の資料を個別に選択します。ホームと時間割は同じ保存済み結果を表示します。授業を選ぶと詳細が開きます。一覧のリンクは右クリックでお気に入り・色・非表示を変更できます。新しいデータの取得は学校アカウントで行います。")));
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "開発版";
        Add(Text("たくポケ Win · " + version, 20)); Add(Text("iOS版の全機能対応とWindows実機確認が揃うまで正式版として配布しません。学校アカウント認証と実データ取得の実機確認は継続中です。"));
        Add(Button("利用規約", () => ShowProductDocument("利用規約", Path.Combine("Legal", "terms.txt"))));
        Add(Button("プライバシーポリシー", () => ShowProductDocument("プライバシーポリシー", Path.Combine("Legal", "privacy.txt"))));
        Add(Button("依存ライブラリのライセンス", () => ShowProductDocument("依存ライブラリのライセンス", "THIRD-PARTY-NOTICES.txt")));
    }
    private async Task ShowProductDocument(string title, string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relative);
        await Message(title, File.Exists(path) ? await File.ReadAllTextAsync(path) : "開発ソースからの起動では配布用文書が含まれない場合があります。依存ライセンスは配布パッケージに同梱します。プロジェクト自体のライセンスは未選定です。");
    }
    private Border MaterialCard(MaterialKind kind)
    {
        var snapshot = _model.Materials.GetValueOrDefault(kind); var source = snapshot?.Source; var analysis = snapshot?.Analysis;
        var summary = Text(source is null ? "資料を選択していません。" : source.OriginalName + "\n取得：" + source.AcquiredAt.ToLocalTime().ToString("g") + "\n確認：" + source.LastCheckedAt.ToLocalTime().ToString("g"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(summary, "material-summary-" + kind);
        var panel = Panel(Text(AppViewModel.MaterialLabel(kind), 18), summary,
            Button(source is null ? "資料を選択" : "資料を選び直す", () => SelectMaterial(kind), "select-material-" + kind));
        if (snapshot?.AcquisitionAttempt?.Failure is { } acquisition) panel.Children.Add(Text(acquisition));
        if (snapshot?.ParseAttempt?.Failure is { } failure)
        {
            panel.Children.Add(Text(failure.StartsWith('P') ? new Takupoke.Infrastructure.Parsing.PdfParseException(failure).Message : failure));
            if (analysis is not null) panel.Children.Add(Text("前回の正常な解析結果を表示しています。選択中の原本とは異なる場合があります。"));
        }
        if (source is not null)
        {
            panel.Children.Add(Button("保存した原本を再解析", () => _model.ReparseAsync(kind)));
            if (kind != MaterialKind.Changes) panel.Children.Add(Button("選択中の保存PDFを表示", () => ShowPdf(kind, false)));
        }
        if (analysis is not null)
        {
            panel.Children.Add(Text($"解析：{analysis.ParsedAt.ToLocalTime():g} · {analysis.SchoolYear}年度"));
            panel.Children.Add(Button("解析結果を確認", () => ShowAnalysis(kind)));
            if (kind != MaterialKind.Changes && source?.Id != analysis.OriginalId) panel.Children.Add(Button("正常結果に対応する保存PDFを表示", () => ShowPdf(kind, true)));
        }
        return Card(panel);
    }
    private async Task ShowAnalysis(MaterialKind kind)
    {
        var analysis = _model.Materials.GetValueOrDefault(kind)?.Analysis; if (analysis is null) return;
        var filtered = kind is MaterialKind.Timetable or MaterialKind.Changes;
        var selected = (kind == MaterialKind.Timetable ? _model.Preferences.TimetableAnalysisClasses : _model.Preferences.ChangeAnalysisClasses).ToHashSet();
        var available = kind == MaterialKind.Timetable ? analysis.Timetable?.Lessons.Select(l => l.ClassName).Distinct().Order().ToArray() ?? []
            : analysis.Changes?.Select(c => c.DisplayClassName).Distinct().Order().ToArray() ?? [];
        var result = Text("");
        void Populate()
        {
            IEnumerable<string> lines = kind switch
            {
                MaterialKind.Timetable => analysis.Timetable?.Lessons.Where(l => selected.Count == 0 || selected.Contains(l.ClassName)).Select(l => $"{ClassSelection.Display(l.ClassName)} · {new[] { "", "月", "火", "水", "木", "金", "土", "日" }[l.Weekday]} · {l.Period}限 · {l.Names.Subject} · {l.Names.Teacher} · {l.Names.Room}") ?? [],
                MaterialKind.Changes => analysis.Changes?.Where(c => selected.Count == 0 || selected.Contains(c.DisplayClassName)).Select(c => c.ChangeDate + " · " + ClassSelection.Display(c.DisplayClassName) + " · " + c.DisplayPeriod + " · " + c.BeforeSubject + " → " + c.AfterSubject + " · " + c.Teacher + " · " + c.Room + " · " + c.Note + "\n元の行：" + c.RawText) ?? [],
                _ => analysis.Special?.Lessons.Select(l => l.Date + " · " + ClassSelection.Display(l.ClassName) + " · " + l.Period + "限 · " + string.Join(" · ", l.Lines)) ?? []
            };
            result.Text = string.Join("\n", lines);
        }
        var panel = Panel(Text("解析版：" + analysis.ParserVersion + " · " + analysis.SourceName));
        if (filtered)
        {
            panel.Children.Add(Text("確認するクラス（未選択は全クラス）。時間割の選択とは独立して保存します。"));
            foreach (var cls in available.Concat(selected).Distinct().Order())
            {
                var check = new CheckBox { Content = ClassSelection.Display(cls), IsChecked = selected.Contains(cls) };
                check.Checked += (_, _) => { selected.Add(cls); Populate(); }; check.Unchecked += (_, _) => { selected.Remove(cls); Populate(); };
                panel.Children.Add(check);
            }
        }
        Populate(); panel.Children.Add(result); await Dialog("解析結果", panel);
        if (filtered) await _model.SavePreferencesAsync(kind == MaterialKind.Timetable ? _model.Preferences with { TimetableAnalysisClasses = selected.ToArray() }
            : _model.Preferences with { ChangeAnalysisClasses = selected.ToArray() });
    }
}
