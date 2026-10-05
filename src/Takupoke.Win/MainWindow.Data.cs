using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Win.ViewModels;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private Task OpenPage(string page)
    {
        // These detail/help/setup views belong to Settings, including entry from Home.
        Navigation.SelectedItem = Navigation.MenuItems[3];
        _page = page; Render(); return Task.CompletedTask;
    }
    private void BackToSettings() => Add(IconButton("設定に戻る", "back", () => OpenPage("settings"), "back-settings"));
    private static StackPanel DataField(string label, string value)
    {
        var field = Panel(SettingsDescription(label), Text(value.Length == 0 ? "記載なし" : value)); field.Spacing = 4; return field;
    }
    private static Expander TechnicalDetails(params UIElement[] content) => new()
    {
        Header = "データの識別情報", HorizontalAlignment = HorizontalAlignment.Stretch, Content = Panel(content)
    };
    private void BuildMaterialDetails(MaterialKind kind)
    {
        TitleText(AppViewModel.MaterialLabel(kind), "page-material-" + kind);
        Add(IconButton("時間割ファイルに戻る", "back", () => OpenPage("materials"), "back-materials"));
        var snapshot = _model.Materials.GetValueOrDefault(kind); var source = snapshot?.Source; var analysis = snapshot?.Analysis;
        var attempts = Panel(SettingsSectionTitle("取得と解析の状況"));
        if (snapshot?.AcquisitionAttempt is { } acquisition)
            attempts.Children.Add(DataField("最終取得の試行 · " + DisplayDateTime(acquisition.At), acquisition.Failure ?? "取得済み"));
        if (snapshot?.ParseAttempt is { } attempt)
        {
            if (attempt.RecoveryPending && attempt.SourceDigest == source?.Digest)
                attempts.Children.Add(SettingsDescription("端末内AIによる復旧待ちです。新しい資料をまだ反映できていません。前回の正常結果を保持しています。"));
            attempts.Children.Add(DataField("最終解析の試行 · " + DisplayDateTime(attempt.At), attempt.Failure is { } failure
                ? kind != MaterialKind.Changes ? new PdfParseException(failure, attempt.Page, attempt.Cell).Message : failure : "解析済み"));
            if (attempt.ChangeError is ChangeErrorCode.FormulaCache or ChangeErrorCode.WeekdayMismatch)
                attempts.Children.Add(Button("警告を確認して内容を見る", PreviewChanges, "preview-changes"));
        }
        if (source is null) attempts.Children.Add(SettingsDescription("未選択"));
        attempts.Children.Add(OperationButton(source is null ? "資料を選択" : "資料を選び直す", () => SelectMaterial(kind), "select-material-" + kind));
        if (source is not null)
        {
            attempts.Children.Add(OperationButton("同じファイルを再取得", () => _model.ReacquireAsync(kind), "reacquire-" + kind));
            attempts.Children.Add(OperationButton("最新を取得して再解析", () => _model.ReparseAsync(kind), "reparse-" + kind));
            if (kind != MaterialKind.Changes && snapshot?.ParseAttempt?.RecoveryPending == true && snapshot.ParseAttempt.SourceDigest == source.Digest)
            {
                attempts.Children.Add(OperationButton("端末内でPDFを復旧", async () => { await _model.PrepareRecoveryAsync(kind); if (_model.Materials.GetValueOrDefault(kind) is { } prepared && (prepared.RecoveryPreview is not null || prepared.ManualSession is not null)) await OpenPage("recovery." + kind); }, "recover-pdf-" + kind));
                if (snapshot.RecoveryPreview is not null || snapshot.ManualSession is not null) attempts.Children.Add(Button(snapshot.ManualSession is not null ? "原本と照合して入力" : "復旧した内容を確認", () => OpenPage("recovery." + kind), "recovery-preview-" + kind));
                attempts.Children.Add(Button("端末内モデルを管理", () => OpenPage("ai-models"), "recovery-models-" + kind));
            }
            if (kind != MaterialKind.Changes) attempts.Children.Add(Button("保存済みのPDFを見る", () => ShowPdf(kind, false), "view-pdf-" + kind));
        }
        Add(Card(attempts));
        if (source is not null)
            Add(Card(Panel(SettingsSectionTitle("ファイル情報"), DataField("ファイル名", source.OriginalName), DataField("サイズ", $"{source.ByteCount:N0}バイト"),
                DataField("最終取得", DisplayDateTime(source.AcquiredAt)), DataField("最終確認", DisplayDateTime(source.LastCheckedAt)),
                DataField("元ファイルの更新", source.SourceModifiedAt is { } modified ? DisplayDateTime(modified) : "未確認"))));
        var results = Panel(SettingsSectionTitle("保存済みの解析結果"));
        if (analysis is null)
        {
            results.Children.Add(SettingsDescription("未解析")); Add(Card(results)); return;
        }
        if (analysis.SourceDigest != source?.Digest || analysis.ParserVersion != MaterialCoordinator.ParserVersion(kind))
            results.Children.Add(SettingsDescription("前回の正常な解析結果を表示しています。選択中の原本と異なる場合があります。"));
        var count = analysis.Timetable?.Lessons.Count ?? analysis.Changes?.Count ?? analysis.Special?.Lessons.Count ?? 0;
        results.Children.Add(DataField("最終解析成功", DisplayDateTime(analysis.ParsedAt)));
        results.Children.Add(DataField("解析した資料", analysis.SourceName));
        results.Children.Add(DataField("学校年度", analysis.SchoolYear + "年度"));
        results.Children.Add(DataField("件数", count + "件"));
        if (analysis.Timetable is { } timetable) results.Children.Add(DataField("学期", timetable.Term ?? "未確認"));
        if (analysis.Special is { } special)
        {
            results.Children.Add(DataField("対象日", string.Join("・", special.CoveredDates)));
            results.Children.Add(DataField("対象クラス", string.Join("・", special.CoveredClasses.Select(ClassSelection.Display))));
        }
        results.Children.Add(Button("解析結果を確認", () => ShowAnalysis(kind), "analysis-" + kind));
        if (kind != MaterialKind.Changes && source?.Id != analysis.OriginalId) results.Children.Add(Button("正常結果に対応する保存PDFを見る", () => ShowPdf(kind, true)));
        if (analysis.Recovery is { } recovery) results.Children.Add(TechnicalDetails(DataField("端末内復旧", recovery.Result.Metadata.Provider), DataField("モデル", recovery.Result.Metadata.ModelId + " · " + recovery.Result.Metadata.ModelVersion), DataField("確認日時", DisplayDateTime(recovery.Acceptance.AcceptedAt))));
        results.Children.Add(TechnicalDetails(DataField("解析版", analysis.ParserVersion.ToString())));
        Add(Card(results));
    }
    private async Task PreviewChanges()
    {
        if (await Dialog("解析の警告", Text("日付・曜日や数式の保存値を確認できない部分があります。警告を確認して内容を閲覧できます。正常結果の保存や時間割への反映は行いません。"), "確認して表示", "キャンセル") != ContentDialogResult.Primary) return;
        var epoch = _model.PrivateEpoch;
        var preview = await _model.PreviewChangesAsync();
        if (epoch != _model.PrivateEpoch || _model.Locked) return;
        var panel = Panel(Card(Panel(SettingsSectionTitle("確認用の表示"), Text("閲覧のみです。正常結果の保存や時間割への反映は行いません。"))));
        foreach (var warning in preview.Warnings) panel.Children.Add(Text(warning.Message));
        foreach (var change in preview.Changes) panel.Children.Add(Card(Panel(Text(change.BeforeSubject + " → " + change.AfterSubject, 16), SettingsDescription(change.ChangeDate + " · " + ClassSelection.Display(change.DisplayClassName) + " · " + change.DisplayPeriod), Text(change.RawText))));
        await Dialog("時間割変更のプレビュー", panel);
        panel.Children.Clear();
    }
    private void BuildAccountData()
    {
        TitleText("リンク・名称・授業時刻", "page-account"); BackToSettings();
        if (_model.PlatformMessage is { } platformMessage) Add(Card(Panel(SettingsSectionTitle("認証の準備を確認してください"), Text(platformMessage))));
        foreach (var kind in Enum.GetValues<DataSet>())
        {
            var state = _model.Revisions.GetValueOrDefault(kind);
            var saved = kind switch { DataSet.Links => _model.LinksRecord is not null, DataSet.Mapping => _model.MappingRecord is not null, _ => _model.TimesRecord is not null };
            var failure = _model.SharedUpdateResults.FirstOrDefault(result => result.Kind == kind)?.Failure ?? (_model.RevisionFailures.TryGetValue(kind, out var revisionFailure) ? revisionFailure : (ApiFailure?)null);
            var status = Text(failure is not null ? "要確認" : !saved ? "未取得" : state is null ? "取得済み" : state.Changed ? "更新あり" : "取得済み");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "shared-status-" + kind);
            if (failure is not null) status.Foreground = WarningBrush;
            var panel = Panel(SettingsSectionTitle(AppViewModel.DataSetLabel(kind)), status,
                Button("詳細を見る", () => SharedDetails(kind), "shared-details-" + kind));
            if (failure is { } value) panel.Children.Add(Text(new ApiException(value).Message));
            Add(Card(panel));
        }
        if (_model.SharedUpdateMessage is { } message) { var error = Text(message); error.Foreground = WarningBrush; Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(error, "account-update-error"); Add(error); }
        Add(Card(Panel(OperationButton("更新を確認・取得", _model.UpdateSharedAsync, "update-account"))));
    }
    private Task SharedDetails(DataSet kind)
    {
        if (kind == DataSet.Times && _model.TimesRecord is { } savedTimes)
        {
            var panel = Panel(Card(Panel(DataField("取得日時（日本時間）", DisplayDateTime(savedTimes.FetchedAt)),
                DataField("授業時刻の件数", savedTimes.Data.Days.Sum(day => day.Periods.Count) + "件"))));
            foreach (var day in savedTimes.Data.Days)
                panel.Children.Add(Card(Panel(SettingsSectionTitle(day.Date), Text(string.Join("\n", day.Periods.Select(period => $"{period.Period}限　{period.Start}〜{period.End}"))))));
            if (savedTimes.Data.Days.Count == 0) panel.Children.Add(SettingsDescription("授業時刻の記載はありません。"));
            panel.Children.Add(TechnicalDetails(DataField("更新識別子", savedTimes.Revision)));
            return Dialog("授業時刻", panel);
        }
        if (kind == DataSet.Links && _model.LinksRecord is { } links)
            return Dialog("リンク一覧", Panel(Card(Panel(DataField("取得日時（日本時間）", DisplayDateTime(links.CheckedAt)),
                DataField("リンク数", links.Payload.Items.Count() + "件"), DataField("カテゴリ数", links.Payload.Categories.Count + "件"))),
                TechnicalDetails(DataField("データの版", links.Payload.LinksVersion), DataField("更新識別子", links.Revision))));
        if (kind == DataSet.Mapping && _model.MappingRecord is { } mapping)
            return Dialog("名称対応表", Panel(Card(Panel(DataField("取得日時（日本時間）", DisplayDateTime(mapping.FetchedAt)),
                DataField("科目", mapping.Rules.Subjects.Count + "件"), DataField("教員", mapping.Rules.Teachers.Count + "件"),
                DataField("教室", mapping.Rules.Rooms.Count + "件"), DataField("条件付きの教員名", (mapping.Rules.TeacherContexts?.Count ?? 0) + "件"))),
                TechnicalDetails(DataField("データの版", mapping.Version), DataField("更新識別子", mapping.Revision))));
        return Message(AppViewModel.DataSetLabel(kind), "データをまだ取得していません。学校アカウントで取得してください。");
    }
    private Task EventDetails(int year)
    {
        if (!_model.EventRecords.TryGetValue(year, out var record)) return Message("学校行事", "この年度の行事データをまだ取得していません。");
        var items = record.Payload.Project().OrderBy(item => item.Date).ToArray();
        var content = Panel(Card(Panel(DataField("取得日時（日本時間）", DisplayDateTime(record.FetchedAt)), DataField("行事数", items.Length + "件"))));
        foreach (var item in items)
            content.Children.Add(Card(Panel(Text(item.Title, 16), SettingsDescription(item.Date + (item.EndDate is { } end ? "〜" + end : "")), SettingsDescription(item.Tag))));
        content.Children.Add(TechnicalDetails(DataField("データの版", record.Payload.Version), DataField("配信ETag", record.ApiETag ?? "未確認"), DataField("元PDF ETag", record.Payload.SourcePdfETag ?? "未確認")));
        return Dialog(year + "年度の学校行事", content);
    }
    private int _setupStep;
    private void BuildSetup()
    {
        TitleText("初期設定", "page-setup"); Add(SettingsDescription($"手順 {_setupStep + 1} / 3"));
        if (_setupStep == 0)
        {
            Add(Card(Panel(SettingsSectionTitle("学校アカウントでデータを取得"), OperationButton("学校アカウントで取得", _model.UpdateSharedAsync, "setup-account"))));
        }
        else if (_setupStep == 1)
        {
            Add(SettingsSectionTitle("時間割ファイルと学校行事"));
            Add(Text("試験・返却PDFは任意です。"));
            foreach (var kind in Enum.GetValues<MaterialKind>()) Add(MaterialCard(kind));
            Add(OperationButton("今年度の学校行事を取得", () => _model.FetchEventsAsync(_model.Today.SchoolYear()), "setup-events"));
        }
        else
        {
            Add(Card(Panel(SettingsSectionTitle("クラスを選択"), Text("1年生はホームルーム＋学科を選択。"), Button("クラスを選択", ChooseClasses, "setup-class"), SettingsDescription(_model.Preferences.SelectedClasses.Length == 0 ? "クラスはまだ選択していません。" : string.Join("・", _model.Preferences.SelectedClasses.Select(ClassSelection.Display))))));
        }
        if (_setupStep > 0) Add(Button("戻る", () => { _setupStep--; Render(); return Task.CompletedTask; }, "setup-back"));
        Add(AccentButton(_setupStep == 2 ? "はじめる" : "次へ", async () =>
        {
            if (_setupStep < 2) { _setupStep++; Render(); return; }
            if (!_model.Preferences.NotificationsSetupCompleted)
            {
                var allow = await Dialog("更新の通知", Text("選択中クラスの時間割変更と試験・返却PDFの更新を通知します。種類別に設定から変更できます。"), "通知を有効にする", "今は有効にしない");
                await _model.SavePreferencesAsync(current => current with { NotificationsSetupCompleted = true, NotifyChanges = allow == ContentDialogResult.Primary, NotifySpecials = allow == ContentDialogResult.Primary });
            }
            await FinishSetup();
        }, "setup-next"));
        Add(Button("あとで設定", FinishSetup, "setup-later"));
    }
    private async Task FinishSetup()
    {
        await _model.SavePreferencesAsync(current => current with { SetupCompleted = true });
        _page = "home"; Navigation.SelectedItem = Navigation.MenuItems[0]; Render();
    }
    private void BuildHelp()
    {
        TitleText("使い方", "page-help"); BackToSettings();
        var topics = new (string Title, string Body)[]
        {
            ("はじめに", "1. OneDriveの同期フォルダーで資料を開けることを確認します。「このデバイス上で常に保持する」でオフラインでも原本を利用できます。\n2. 設定から学校アカウントでリンク・名称・授業時刻を取得します。\n3. 通常時間割PDFと時間割変更XLSXを選びます。試験・返却PDFは必要なときに選びます。\n4. 学校行事の年度を確認して取得します。\n5. クラスを選びます。"),
            ("時間割を見る", "ホームには今日の授業と行事を表示します。「時間割を見る」で今日を含む週へ移動します。ホームは常に変更込みです。\n時間割は前週・翌週・カレンダーで移動できます。授業を選ぶと詳細が開きます。連続授業はまとめ、重なる授業は並べます。\n変更一覧は今日以降・この週・全件で絞り込めます。一覧専用のクラスを選ぶこともできます。"),
            ("リンクを使う", "一覧の検索はかな・ローマ字にも対応します。\n各リンクの「…」から、お気に入り・アイコンの色・非表示・今回だけ別の開き方を選べます。右クリックやShift+F10でも同じメニューを開けます。お気に入りはホームに表示します。\n非表示のリンクを管理する画面から再表示できます。設定でアプリ内・外部ブラウザを選べます。"),
            ("更新と通知", "資料は起動・復帰・ファイル変更・手動確認で読み直し、内容が変わると解析します。同じ保存先で同期更新された資料も選び直す必要はありません。「最新を取得して再解析」は選択中の原本を読み直して解析します。保存済み年度の学校行事も確認します。\nホームの更新案内からデータの取得画面を開けます。認証は取得操作のときだけ開始します。\n通知は今日以降・選択中クラスの変更と、解析成功した試験・返却PDFの更新が対象です。初回は通知しません。\n通知領域での常駐を有効にすると、閉じた後も15分ごとに確認します。完全終了・電源断・スリープ中は確認しません。\n4月1日・10月1日の切替後は資料を選び直し、学校データを再取得してください。個人設定とOneDriveの原本は保持します。"),
            ("困ったとき", "更新されない場合はOneDriveの同期状況を確認し、エクスプローラーで資料を開いてから再確認します。移動・削除・アクセス不能では資料を選び直してください。\n解析失敗は資料の詳細で確認します。再解析に失敗しても保存期間内の前回正常結果を保持します。\n年のない変更日には学校年度を使い、1〜3月は翌年の日付になります。年度を変えたら再解析してください。\n半期切替でファイル選択が消えた場合は再選択が必要です。設定から初期設定を再度開くこともできます。")
        };
        Add(SettingsGroup(topics.Select(topic => SettingsRow(topic.Title, () => Message(topic.Title, topic.Body), topic.Title, icon: "help")).Cast<UIElement>().ToArray()));
    }
    private void BuildLicenses()
    {
        TitleText("依存ライブラリのライセンス", "page-licenses"); Add(IconButton("このアプリについてに戻る", "back", () => OpenPage("about"), "back-about"));
        var directory = Path.Combine(AppContext.BaseDirectory, "Licenses");
        if (Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory, "*.txt", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                Add(Card(SettingsRow(Path.GetFileNameWithoutExtension(file), async () => await Message(Path.GetFileNameWithoutExtension(file), await File.ReadAllTextAsync(file)), Path.GetFileNameWithoutExtension(file), icon: "document")));
        else Add(Text("配布パッケージにライブラリ別のライセンス全文を同梱します。"));
        var iconLicense = Path.Combine(AppContext.BaseDirectory, "Assets", "FluentIcons", "LICENSE.txt");
        if (File.Exists(iconLicense)) Add(Card(SettingsRow("Microsoft Fluent System Icons", () => ShowProductDocument("Microsoft Fluent System Icons", Path.Combine("Assets", "FluentIcons", "LICENSE.txt")), "license-fluent-icons", icon: "palette")));
        Add(Button("同梱部品の一覧", () => ShowProductDocument("同梱部品の一覧", "THIRD-PARTY-NOTICES.txt")));
    }
}
