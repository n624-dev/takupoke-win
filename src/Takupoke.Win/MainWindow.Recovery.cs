using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Win.Platform;
using Takupoke.Win.ViewModels;

namespace Takupoke.Win;
public sealed partial class MainWindow
{
    private string? _recoveryClass;
    private void BuildRecoveryPreview(MaterialKind kind)
    {
        TitleText("端末内で読み取った結果", "page-recovery-" + kind);
        Add(IconButton("資料の詳細に戻る", "back", () => OpenPage("material." + kind), "back-recovery"));
        var snapshot = _model.Materials.GetValueOrDefault(kind); var preview = snapshot?.RecoveryPreview;
        var document = snapshot?.RecoveryDisplay;
        if (preview is null || document is null || preview.SourceId != snapshot?.Source?.Id || document.SourceId != preview.SourceId ||
            document.PdfHash != snapshot.Source.Digest || preview.Document.PdfHash != document.PdfHash ||
            snapshot.RecoveryJob?.State != RecoveryJobState.AwaitingConfirmation || snapshot.RecoveryJob.PdfHash != document.PdfHash ||
            snapshot.RecoveryJob.Kind != document.Kind || snapshot.RecoveryJob.ResultHash != document.ResultHash)
        { Add(Text("確認待ちの結果がありません。PDFが更新された場合は資料の詳細から復旧してください。")); return; }
        Add(Card(Panel(SettingsSectionTitle("原本を確認してから使用してください"), Text($"{document.SchoolYear}年度 {document.Term} · {AppViewModel.MaterialLabel(kind)}"), Text("表示中のクラスだけでなく、資料全体・全クラスの結果を採用します。採用するまで時間割には反映されません。学校の資料と読み取り結果は外部へ送信されません。"),
            Button("対応する元PDFを確認", () => ShowPdf(kind, false, preview), "recovery-original-" + kind))));
        if (_recoveryClass is null || !document.Classes.Contains(_recoveryClass)) _recoveryClass = document.Classes.FirstOrDefault(c => _model.Preferences.SelectedClasses.Contains(c)) ?? document.Classes[0];
        Add(Button("表示するクラス：" + ClassSelection.Display(_recoveryClass), async () =>
        {
            var panel = Panel(); foreach (var cls in document.Classes) panel.Children.Add(Button(ClassSelection.Display(cls), () => { _recoveryClass = cls; _activeDialog?.Hide(); Render(); return Task.CompletedTask; }));
            await Dialog("確認するクラス", panel);
        }, "recovery-class"));
        var firstDay = document.Days.Min();
        foreach (var cell in document.Cells.Where(c => c.Slots.Any(s => s.ClassName == _recoveryClass)).OrderBy(c => c.Slots[0].Day).ThenBy(c => c.Slots[0].Period))
        {
            var slot = cell.Slots[0];
            var day = kind == MaterialKind.Timetable ? new[] { "", "月", "火", "水", "木", "金" }[int.Parse(slot.Day)] : slot.Day;
            var periods = string.Join("・", cell.Slots.Select(s => s.Period)) + "限";
            var content = Panel(SettingsSectionTitle(day + " " + periods));
            if (kind != MaterialKind.Timetable) { var span = cell.Slots.Length > 1 ? $"{cell.Slots.Min(s => s.Period)}-{cell.Slots.Max(s => s.Period)}" : slot.Period.ToString(); var clock = (cell.Slots.Length > 1 ? document.SpanTimes : document.Times).GetValueOrDefault(slot.Day + ":" + span); content.Children.Add(Text(clock ?? "時刻未確認")); if (kind == MaterialKind.ExamReturn) content.Children.Add(SettingsDescription(slot.Day == firstDay ? "初日専用の時刻" : "PDF中の注記に基づく通常授業時間")); }
            if (cell.State == RecoveryValueState.Empty) content.Children.Add(Text("空欄（原本で確認済み）"));
            foreach (var lesson in cell.Lessons) content.Children.Add(Text(lesson.Subject + " / " + (lesson.TeacherEmpty ? "教員記載なし" : lesson.Teacher) + " / " + (lesson.RoomEmpty ? "教室記載なし" : lesson.Room)));
            Add(Card(content));
        }
        Add(OperationButton("資料全体を確認しました。全クラスの結果を使用", () => _model.AdoptRecoveryAsync(kind, preview), "adopt-recovery-" + kind));
        Add(TechnicalDetails(DataField("端末内Provider", document.Metadata.Provider), DataField("モデル", document.Metadata.ModelId + " " + document.Metadata.ModelVersion), DataField("検証版", document.Metadata.ValidatorVersion.ToString())));
    }
    private void BuildRecoveryModels()
    {
        TitleText("端末内AIモデル", "page-ai-models"); BackToSettings();
        if (_model.RecoveryModelMessage is { } message) Add(Text(message));
        Add(Text("学校PDF・画像・OCR文字・科目・教員名・復旧結果は外部へ送信しません。モデルファイルの取得にだけインターネットを使用します。"));
        Add(Card(Panel(SettingsSectionTitle("日本語OCR"), Text(_model.OcrModelReady ? "確認済みモデルを保存しています。" : _model.OcrModelInstalled ? "保存モデルを利用できません。削除して再取得してください。" : "画像PDF用のモデルは未ダウンロードです。"), Text($"約{WindowsRecoveryModels.OcrBundle.Size / 1024 / 1024} MB · {WindowsRecoveryModels.OcrBundle.License}"),
            _model.OcrModelInstalled ? OperationButton("OCRモデルを削除", _model.DeleteOcrModelAsync, "delete-ocr-model") : OperationButton("日本語OCRモデルをダウンロード", _model.InstallOcrModelAsync, "download-ocr-model"))));
        if (_model.FoundryModel is { } installed)
            Add(Card(Panel(SettingsSectionTitle("保存した端末内AIモデル"), Text(installed.ModelId),
                OperationButton("AIモデルを削除", _model.DeleteFoundryModelAsync, "delete-foundry-model"))));
        foreach (var model in _model.FoundryCandidates)
        {
            var content = Panel(SettingsSectionTitle("追加の端末内AIモデル"), Text(model.ModelId), Text($"約{model.Size / 1024 / 1024} MB · {model.License}"));
            if (!model.Validated) content.Children.Add(Text("この候補は読み取り精度の検証中です。検証が完了するまで配信しません。"));
            else if (_model.FoundryModel?.ModelId == model.ModelId) content.Children.Add(Text("このモデルを保存しています。"));
            else content.Children.Add(OperationButton("端末内AIモデルをダウンロード", () => _model.InstallFoundryModelAsync(model), "download-foundry-model"));
            Add(Card(content));
        }
        Add(Card(Panel(SettingsSectionTitle("Windows標準の生成AI"), Text("対応端末で準備済みの場合に使用します。通常解析とルール復旧を優先し、自動的に大きなモデルをダウンロードしません。"))));
    }
}
