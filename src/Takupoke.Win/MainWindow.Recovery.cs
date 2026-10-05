using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Takupoke.Infrastructure.Storage;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Win.Platform;
using Takupoke.Win.ViewModels;

namespace Takupoke.Win;
public sealed partial class MainWindow
{
    private string? _recoveryClass;
    private readonly RecoveryManualInputState _manualInput = new();
    private MaterialKind? _manualKind;
    private bool _manualLifecycleBound;
    private Button? _manualSubmitButton;
    private void UpdateManualSubmitStatus()
    { if (_manualSubmitButton is { } button) button.IsEnabled = _manualInput.Ready && !_model.Busy && !_model.Locked; }
    private void SyncManualInput()
    {
        var snapshot = _manualKind is { } kind ? _model.Materials.GetValueOrDefault(kind) : null;
        var session = snapshot?.ManualSession;
        if (_model.Locked || snapshot?.AcquisitionAttempt?.Failure is not null || session is null ||
            snapshot?.Source?.Id != session.SourceId || snapshot.Source.Digest != session.Plan.Document.PdfHash)
        { _manualInput.Clear(); return; }
        _manualInput.Bind(RecoveryValidator.Fingerprint(session), session.Plan.Targets.ToDictionary(t => t.Target.Key, t => t.OriginalOcr, StringComparer.Ordinal));
    }
    private bool KeepManualFormForSnapshot()
    {
        if (_manualKind is not { } kind || _page != "recovery." + kind || _renderedPage != _page || _model.Locked)
            return false;
        var snapshot = _model.Materials.GetValueOrDefault(kind);
        var session = snapshot?.ManualSession;
        return snapshot is not null && snapshot.AcquisitionAttempt?.Failure is null && session is not null &&
            snapshot.Source is { } source && source.Id == session.SourceId && source.Digest == session.Plan.Document.PdfHash &&
            _manualInput.IsBoundTo(RecoveryValidator.Fingerprint(session));
    }
    private void BuildManualRecovery(MaterialKind kind, RecoveryManualSession session)
    {
        _manualKind = kind;
        if (!_manualLifecycleBound)
        {
            _manualLifecycleBound = true;
            _model.PrivateDataCleared += _manualInput.Clear;
            _model.SnapshotChanged += SyncManualInput;
        }
        SyncManualInput();
        var identity = RecoveryValidator.Fingerprint(session);
        if (_manualInput.Values.Count != session.Plan.Targets.Count)
        { Add(Text("入力待ちの原本を確認できません。資料の詳細から読み直してください。")); return; }
        Add(Card(Panel(SettingsSectionTitle("原本と照合してください"),
            Text($"資料全体で確認が必要な{session.Plan.Targets.Count}項目です。入力だけでは採用されません。入力後に全クラスの結果を確認してください。"),
            Text("科目・教員・教室の全文を原本から入力してください。読めない文字や印字がある箇所を空欄にはできません。"))));
        Add(Button("対応する元PDFを確認", () => ShowPdf(kind, false, manual: session), "manual-original-" + kind));
        var submit = Button("入力内容を確認して全体の結果へ", async () =>
        {
            SyncManualInput();
            if (!_manualInput.IsBoundTo(identity) || !_manualInput.Ready || _model.Busy || _model.Locked) return;
            await _model.CompleteManualRecoveryAsync(kind, session, _manualInput.Submission());
        }, "manual-submit-" + kind);
        _manualSubmitButton = submit;
        void UpdateSubmit() => UpdateManualSubmitStatus();
        foreach (var target in session.Plan.Targets)
        {
            var key = target.Target.Key;
            var cell = session.Plan.Document.Cells.Single(c => c.Id == target.Target.CellId);
            var slot = cell.Slots[0];
            var day = kind == MaterialKind.Timetable ? new[] { "", "月", "火", "水", "木", "金" }[int.Parse(slot.Day)] + "曜日" : slot.Day;
            var role = target.Target.Role switch { RecoveryFieldRole.Subject => "科目", RecoveryFieldRole.Teacher => "教員", _ => "教室" };
            var panel = Panel(SettingsSectionTitle(role), Text($"{ClassSelection.Display(slot.ClassName)} · {day} · {string.Join("・", cell.Slots.Select(s => s.Period).Distinct().Order())}限"));
            var original = session.Plan.Document.Capture!.OriginalCrops!.Single(c => c.Target == target.Target);
            var bitmap = new WriteableBitmap(original.Width, original.Height);
            using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(original.Bgra);
            bitmap.Invalidate();
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, Width = Math.Min(320d, original.Width * 4d), MaxHeight = 180, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetAutomationId(image, "manual-crop-" + key);
            AutomationProperties.SetName(image, "原本の該当箇所"); panel.Children.Add(image);
            panel.Children.Add(Text("自動読取: " + target.OriginalOcr));
            var input = OperationControl(new TextBox { Text = _manualInput.Values[key], Header = "PDFに記載された全文", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 256 });
            AutomationProperties.SetAutomationId(input, "manual-value-" + key);
            var acknowledgement = OperationControl(new CheckBox { Content = "原本と一致することを確認", IsChecked = _manualInput.IsAcknowledged(key) });
            AutomationProperties.SetAutomationId(acknowledgement, "manual-ack-" + key);
            input.TextChanged += (_, _) => { if (!_manualInput.IsBoundTo(identity)) return; _manualInput.Edit(key, input.Text); acknowledgement.IsChecked = false; UpdateSubmit(); };
            acknowledgement.Checked += (_, _) => { if (!_manualInput.IsBoundTo(identity)) return; _manualInput.Acknowledge(key, true); UpdateSubmit(); };
            acknowledgement.Unchecked += (_, _) => { if (!_manualInput.IsBoundTo(identity)) return; _manualInput.Acknowledge(key, false); UpdateSubmit(); };
            panel.Children.Add(input); panel.Children.Add(acknowledgement); Add(Card(panel));
        }
        UpdateSubmit(); Add(submit);
        Add(OperationButton("入力による補助を中止", async () => {
            if (!_manualInput.IsBoundTo(identity)) return;
            await _model.CancelManualRecoveryAsync(kind, session);
            if (_manualInput.IsBoundTo(identity)) _manualInput.Clear();
        }, "manual-cancel-" + kind));
    }
    private void BuildRecoveryPreview(MaterialKind kind)
    {
        _manualSubmitButton = null;
        TitleText("端末内で読み取った結果", "page-recovery-" + kind);
        Add(IconButton("資料の詳細に戻る", "back", () => OpenPage("material." + kind), "back-recovery"));
        var snapshot = _model.Materials.GetValueOrDefault(kind); var preview = snapshot?.RecoveryPreview;
        if (snapshot?.AcquisitionAttempt?.Failure is not null)
        {
            Add(Text("最新の原本を取得できないため採用できません。資料の詳細から再取得してください。前回の正常結果は保持しています。"));
            return;
        }
        if (snapshot?.ManualSession is { } manual)
        { BuildManualRecovery(kind, manual); return; }
        _manualInput.Clear();
        var document = snapshot?.RecoveryDisplay;
        if (preview is null || document is null || preview.SourceId != snapshot?.Source?.Id || document.SourceId != preview.SourceId ||
            document.PdfHash != snapshot.Source.Digest || preview.Document.PdfHash != document.PdfHash ||
            snapshot.RecoveryJob?.State != RecoveryJobState.AwaitingConfirmation || snapshot.RecoveryJob.PdfHash != document.PdfHash ||
            snapshot.RecoveryJob.Kind != document.Kind || snapshot.RecoveryJob.ResultHash != document.ResultHash)
        { Add(Text("確認待ちの結果がありません。PDFが更新された場合は資料の詳細から復旧してください。")); return; }
        Add(Card(Panel(SettingsSectionTitle("原本を確認してから使用してください"), Text($"{document.SchoolYear}年度 {document.Term} · {AppViewModel.MaterialLabel(kind)}"), Text("表示中のクラスだけでなく、資料全体・全クラスの結果を採用します。採用するまで時間割には反映されません。学校の資料と読み取り結果は外部へ送信されません。"),
            Button("対応する元PDFを確認", () => ShowPdf(kind, false, preview), "recovery-original-" + kind))));
        if (preview.Result.HumanCorrections is { Count: > 0 } corrections)
            Add(Text($"原本と照合して利用者が入力した{corrections.Count}項目を含みます。自動読取文字とは別に記録されます。"));
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
