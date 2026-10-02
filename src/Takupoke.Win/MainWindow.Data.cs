using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;
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
    private void BackToSettings() => Add(Button("設定に戻る", () => OpenPage("settings"), "back-settings"));
    private void BuildMaterialDetails(MaterialKind kind)
    {
        TitleText(AppViewModel.MaterialLabel(kind), "page-material-" + kind); Add(Button("時間割ファイルに戻る", () => OpenPage("materials"), "back-materials"));
        var snapshot = _model.Materials.GetValueOrDefault(kind); var source = snapshot?.Source; var analysis = snapshot?.Analysis;
        Add(Text("状態・操作", 22));
        if (snapshot?.AcquisitionAttempt is { } acquisition)
            Add(Text($"取得試行：{acquisition.At.ToLocalTime():g}\n" + (acquisition.Failure ?? "原本を確認しました。")));
        if (snapshot?.ParseAttempt is { } attempt)
        {
            Add(Text($"解析試行：{attempt.At.ToLocalTime():g}\n" + (attempt.Failure is { } failure
                ? failure.StartsWith('P') ? new PdfParseException(failure, attempt.Page, attempt.Cell).Message : failure : "解析結果を保存しました。")));
            if (attempt.ChangeError is ChangeErrorCode.FormulaCache or ChangeErrorCode.WeekdayMismatch)
                Add(Button("警告を確認して内容を見る", PreviewChanges, "preview-changes"));
        }
        Add(OperationButton(source is null ? "資料を選択" : "資料を選び直す", () => SelectMaterial(kind), "select-material-" + kind));
        if (source is not null)
        {
            Add(OperationButton("同じファイルを再取得", () => _model.ReacquireAsync(kind), "reacquire-" + kind));
            Add(OperationButton("保存した原本を再解析", () => _model.ReparseAsync(kind), "reparse-" + kind));
            if (kind != MaterialKind.Changes) Add(Button("保存済みのPDFを見る", () => ShowPdf(kind, false), "view-pdf-" + kind));
            Add(Text("ファイル情報", 22));
            Add(Text($"名前：{source.OriginalName}\nサイズ：{source.ByteCount:N0}バイト\n最終取得：{source.AcquiredAt.ToLocalTime():g}\n最終確認：{source.LastCheckedAt.ToLocalTime():g}\n元ファイルの更新：{source.SourceModifiedAt?.ToLocalTime().ToString("g") ?? "未確認"}"));
        }
        else Add(Text("資料を選択していません。"));
        Add(Text("解析結果", 22));
        if (analysis is null) { Add(Text("正常な解析結果はありません。")); return; }
        if (analysis.SourceDigest != source?.Digest || analysis.ParserVersion != MaterialCoordinator.ParserVersion(kind)) Add(Text("前回の解析結果を表示しています。選択中の原本と異なる場合があります。"));
        var count = analysis.Timetable?.Lessons.Count ?? analysis.Changes?.Count ?? analysis.Special?.Lessons.Count ?? 0;
        Add(Text($"最終解析成功：{analysis.ParsedAt.ToLocalTime():g}\n元資料：{analysis.SourceName}\n年度：{analysis.SchoolYear}\n件数：{count}\n解析版：{analysis.ParserVersion}"));
        if (analysis.Timetable is { } timetable) Add(Text("学期：" + (timetable.Term ?? "未確認")));
        if (analysis.Special is { } special) Add(Text("対象日：" + string.Join("・", special.CoveredDates) + "\n対象クラス：" + string.Join("・", special.CoveredClasses.Select(ClassSelection.Display))));
        Add(Button("解析結果を確認", () => ShowAnalysis(kind), "analysis-" + kind));
        if (kind != MaterialKind.Changes && source?.Id != analysis.OriginalId) Add(Button("正常結果に対応する保存PDFを見る", () => ShowPdf(kind, true)));
    }
    private async Task PreviewChanges()
    {
        if (await Dialog("曜日の警告", Text("曜日の計算結果を確認できません。日付欄を基準に内容を表示します。閲覧のみで、正常結果の保存や時間割への反映は行いません。"), "確認して表示", "キャンセル") != ContentDialogResult.Primary) return;
        var epoch = _model.PrivateEpoch;
        var preview = await _model.PreviewChangesAsync();
        if (epoch != _model.PrivateEpoch || _model.Locked) return;
        var panel = Panel(Text("閲覧のみ — 保存・時間割への反映は行いません。", 20));
        foreach (var warning in preview.Warnings) panel.Children.Add(Text(warning.Message));
        foreach (var change in preview.Changes) panel.Children.Add(Text(change.ChangeDate + " · " + ClassSelection.Display(change.DisplayClassName)
            + " · " + change.DisplayPeriod + " · " + change.BeforeSubject + " → " + change.AfterSubject + "\n" + change.RawText));
        await Dialog("時間割変更のプレビュー", panel);
        panel.Children.Clear();
    }
    private void BuildAccountData()
    {
        TitleText("リンク・名称・授業時刻", "page-account"); BackToSettings();

        foreach (var kind in Enum.GetValues<DataSet>())
        {
            var state = _model.Revisions.GetValueOrDefault(kind);
            var saved = kind switch { DataSet.Links => _model.LinksRecord is not null, DataSet.Mapping => _model.MappingRecord is not null, _ => _model.TimesRecord is not null };
            var failure = _model.SharedUpdateResults.FirstOrDefault(result => result.Kind == kind)?.Failure;
            var status = Text(failure is not null ? "要確認" : !saved ? "未取得" : state is null ? "取得済み" : state.Changed ? "更新あり" : "取得済み");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "shared-status-" + kind);
            var panel = Panel(Text(AppViewModel.DataSetLabel(kind), 20), status, Button("詳細を見る", () => SharedDetails(kind), "shared-details-" + kind));
            if (failure is { } value) panel.Children.Add(Text(new ApiException(value).Message));
            Add(Card(panel));
        }
        if (_model.SharedUpdateMessage is { } message) { var error = Text(message); error.Foreground = WarningBrush; Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(error, "account-update-error"); Add(error); }
        Add(OperationButton("更新を確認・取得", _model.UpdateSharedAsync, "update-account"));
    }
    private Task SharedDetails(DataSet kind)
    {
        var detail = kind switch
        {
            DataSet.Links when _model.LinksRecord is { } links => $"取得日時：{links.CheckedAt.ToLocalTime():g}\n件数：{links.Payload.Items.Count()}\nカテゴリ数：{links.Payload.Categories.Count}\n版：{links.Payload.LinksVersion}\n更新識別子：{links.Revision}",
            DataSet.Mapping when _model.MappingRecord is { } mapping => $"取得日時：{mapping.FetchedAt.ToLocalTime():g}\n科目：{mapping.Rules.Subjects.Count}件\n教員：{mapping.Rules.Teachers.Count}件\n教室：{mapping.Rules.Rooms.Count}件\n条件付き教員：{mapping.Rules.TeacherContexts?.Count ?? 0}件\n版：{mapping.Version}\n更新識別子：{mapping.Revision}",
            DataSet.Times when _model.TimesRecord is { } times => $"取得日時：{times.FetchedAt.ToLocalTime():g}\n件数：{times.Data.Days.Count}\n更新識別子：{times.Revision}",
            _ => "保存したデータはありません。"
        };
        return Message(AppViewModel.DataSetLabel(kind), detail);
    }
    private Task EventDetails(int year)
    {
        if (!_model.EventRecords.TryGetValue(year, out var record)) return Message("学校行事", "保存したデータはありません。");
        var items = record.Payload.Project().OrderBy(item => item.Date).ToArray();
        return Dialog(year + "年度の学校行事", Panel(Text($"取得日時：{record.FetchedAt.ToLocalTime():g}\n件数：{items.Length}\n版：{record.Payload.Version}\n配信ETag：{record.ApiETag ?? "未確認"}\n元PDF ETag：{record.Payload.SourcePdfETag ?? "未確認"}"),
            Text(string.Join("\n", items.Select(item => item.Date + (item.EndDate is { } end ? "〜" + end : "") + " · " + item.Title + " · " + item.Tag)))));
    }
    private int _setupStep;
    private void BuildSetup()
    {
        TitleText("初期設定", "page-setup"); Add(Text($"{_setupStep + 1} / 3", 20));
        if (_setupStep == 0)
        {
            Add(Text("学校アカウントでデータを取得", 22));
            Add(Text("リンク一覧・名称データ・授業時刻を取得します。後から設定することもできます。"));
            Add(OperationButton("学校アカウントで取得", _model.UpdateSharedAsync, "setup-account"));
        }
        else if (_setupStep == 1)
        {
            Add(Text("時間割ファイルと学校行事", 22));
            Add(Text("OneDriveの同期フォルダーから通常時間割PDFと時間割変更XLSXを選びます。試験・返却PDFは手元にある場合に選択してください。"));
            foreach (var kind in Enum.GetValues<MaterialKind>()) Add(MaterialCard(kind));
            Add(OperationButton("今年度の学校行事を取得", () => _model.FetchEventsAsync(_model.Today.SchoolYear()), "setup-events"));
        }
        else
        {
            Add(Text("クラスを選択", 22)); Add(Text("表示するクラスを選んでください。1年生はホームルームと学科を組み合わせられます。"));
            Add(Button("クラスを選択", ChooseClasses, "setup-class"));
            Add(Text(string.Join("・", _model.Preferences.SelectedClasses.Select(ClassSelection.Display))));
        }
        if (_setupStep > 0) Add(Button("戻る", () => { _setupStep--; Render(); return Task.CompletedTask; }, "setup-back"));
        Add(AccentButton(_setupStep == 2 ? "はじめる" : "次へ", async () =>
        {
            if (_setupStep < 2) { _setupStep++; Render(); return; }
            if (!_model.Preferences.NotificationsSetupCompleted)
            {
                var allow = await Dialog("更新の通知", Text("選択中クラスの時間割変更と試験・返却PDFの更新を通知します。種類別に設定から変更できます。"), "通知を有効にする", "今は有効にしない");
                await _model.SavePreferencesAsync(_model.Preferences with { NotificationsSetupCompleted = true, NotifyChanges = allow == ContentDialogResult.Primary, NotifySpecials = allow == ContentDialogResult.Primary });
            }
            await FinishSetup();
        }, "setup-next"));
        Add(Button("あとで設定", FinishSetup, "setup-later"));
    }
    private async Task FinishSetup()
    {
        await _model.SavePreferencesAsync(_model.Preferences with { SetupCompleted = true });
        _page = "home"; Navigation.SelectedItem = Navigation.MenuItems[0]; Render();
    }
    private void BuildHelp()
    {
        TitleText("使い方", "page-help"); BackToSettings();
        var topics = new (string Title, string Body)[]
        {
            ("はじめに", "1. OneDriveの同期フォルダーで資料を開けることを確認します。「このデバイス上で常に保持する」でオフラインでも原本を利用できます。\n2. 設定から学校アカウントでリンク・名称・授業時刻を取得します。\n3. 通常時間割PDFと時間割変更XLSXを選びます。試験・返却PDFは必要なときに選びます。\n4. 学校行事の年度を確認して取得します。\n5. クラスを選びます。"),
            ("時間割を見る", "ホームには今日の授業と行事を表示します。「時間割を見る」で今日を含む週へ移動します。ホームは常に変更込みです。\n時間割は前週・翌週・カレンダーで移動できます。授業を選ぶと詳細が開きます。連続授業はまとめ、重なる授業は並べます。\n変更一覧は今日以降・この週・全件で絞り込めます。一覧専用のクラスを選ぶこともできます。"),
            ("リンクを使う", "一覧の検索はかな・ローマ字にも対応します。\n右クリックまたはShift+F10で、お気に入り・色・既定色への復帰・非表示・今回だけ別の開き方を選べます。お気に入りはホームに表示します。\n非表示のリンクを管理する画面から再表示できます。設定でアプリ内・外部ブラウザを選べます。"),
            ("更新と通知", "資料は起動・復帰・ファイル変更・手動確認で読み直し、内容が変わると解析します。保存済み年度の学校行事も確認します。\nホームの更新案内からデータの取得画面を開けます。認証は取得操作のときだけ開始します。\n通知は今日以降・選択中クラスの変更と、解析成功した試験・返却PDFの更新が対象です。初回は通知しません。\n通知領域での常駐を有効にすると、閉じた後も15分ごとに確認します。完全終了・電源断・スリープ中は確認しません。\n4月1日・10月1日の切替後は資料を選び直し、学校データを再取得してください。個人設定とOneDriveの原本は保持します。"),
            ("困ったとき", "更新されない場合はOneDriveの同期状況を確認し、エクスプローラーで資料を開いてから再確認します。移動・削除・アクセス不能では資料を選び直してください。\n解析失敗は資料の詳細で確認します。再解析に失敗しても保存期間内の前回正常結果を保持します。\n年のない変更日には学校年度を使い、1〜3月は翌年の日付になります。年度を変えたら再解析してください。\n半期切替でファイル選択が消えた場合は再選択が必要です。設定から初期設定を再度開くこともできます。")
        };
        foreach (var topic in topics) Add(Button(topic.Title, () => Message(topic.Title, topic.Body)));
    }
    private void BuildLicenses()
    {
        TitleText("依存ライブラリのライセンス", "page-licenses"); BackToSettings();
        var directory = Path.Combine(AppContext.BaseDirectory, "Licenses");
        if (Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                Add(Button(Path.GetFileNameWithoutExtension(file), async () => await Message(Path.GetFileNameWithoutExtension(file), await File.ReadAllTextAsync(file))));
        else Add(Text("配布パッケージにライブラリ別のライセンス全文を同梱します。"));
        Add(Button("同梱部品の一覧", () => ShowProductDocument("同梱部品の一覧", "THIRD-PARTY-NOTICES.txt")));
    }
}
