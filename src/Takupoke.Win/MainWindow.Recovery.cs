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
    private readonly Dictionary<MaterialKind,(string Identity,HashSet<RecoveryFieldTarget> Targets)> _correctionSelections = [];
    private bool _selectionLifecycleBound;
    private Action? _updateRecoveryBatchStatus;
    private void UpdateManualSubmitStatus()
    { if (_manualSubmitButton is { } button) button.IsEnabled = _model.Preferences.UseAiFeatures && _manualInput.Ready && !_model.Busy && !_model.Locked; }
    private void SyncManualInput()
    {
        var snapshot = _manualKind is { } kind ? _model.Materials.GetValueOrDefault(kind) : null;
        var session = snapshot?.ManualSession;
        if (!_model.Preferences.UseAiFeatures || _model.Locked || snapshot?.AcquisitionAttempt?.Failure is not null || session is null ||
            snapshot?.Source?.Id != session.SourceId || snapshot.Source.Digest != session.Plan.Document.PdfHash)
        { _manualInput.Clear(); return; }
        var native = session.Plan.Targets.ToDictionary(t => t.Target.Key, t => t.OriginalOcr, StringComparer.Ordinal);
        _manualInput.Bind(RecoveryValidator.Fingerprint(session), session.Plan.InitialValues ?? native,
            session.Plan.Document.Capture!.ManualSchemaVersion, native,
            session.Plan.Targets.Where(t => t.HeaderProof is not null).Select(t => t.Target.Key).ToArray());
    }
    private bool KeepManualFormForSnapshot()
    {
        if (!_model.Preferences.UseAiFeatures || _manualKind is not { } kind || _page != "recovery." + kind || _renderedPage != _page || _model.Locked)
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
            Text($"読み取り文字が原本と一致していれば、そのまま確認してください。違う箇所は科目・教員・教室の全文を訂正できます。文字の変更は資料全体で{RecoveryManualAssistance.CorrectionLimit(session.Plan.Document)}項目までです。読めない文字や印字がある箇所を空欄にはできません。"))));
        Add(Button("対応する元PDFを確認", () => ShowPdf(kind, false, manual: session), "manual-original-" + kind));
        var submit = Button("入力内容を確認して全体の結果へ", async () =>
        {
            SyncManualInput();
            if (!_model.Preferences.UseAiFeatures || !_manualInput.IsBoundTo(identity) || !_manualInput.Ready || _model.Busy || _model.Locked) return;
            await _model.CompleteManualRecoveryAsync(kind, session, _manualInput.Submission());
        }, "manual-submit-" + kind);
        _manualSubmitButton = submit;
        void UpdateSubmit() => UpdateManualSubmitStatus();
        foreach (var target in session.Plan.Targets)
        {
            var key = target.Target.Key;
            if (target.HeaderProof is { } header)
            {
                var crop = session.Plan.Document.Capture!.OriginalCrops!.Single(c => c.Target == target.Target);
                var pixels = new WriteableBitmap(crop.Width, crop.Height);
                using (var stream = pixels.PixelBuffer.AsStream()) stream.Write(crop.Bgra);
                pixels.Invalidate();
                var sourceImage = new Image { Source = pixels, Stretch = Stretch.Uniform, MaxWidth = 520, MaxHeight = 360 };
                AutomationProperties.SetAutomationId(sourceImage, "manual-crop-" + key);
                AutomationProperties.SetName(sourceImage, "原本の見出し範囲");
                var ack = OperationControl(new CheckBox { Content = "原画像の見出しと一致することを確認", IsChecked = _manualInput.IsAcknowledged(key) });
                AutomationProperties.SetAutomationId(ack, "manual-ack-" + key);
                ack.Checked += (_, _) => { if (!_manualInput.IsBoundTo(identity)) return; _manualInput.Acknowledge(key, true); UpdateSubmit(); };
                ack.Unchecked += (_, _) => { if (!_manualInput.IsBoundTo(identity)) return; _manualInput.Acknowledge(key, false); UpdateSubmit(); };
                Add(Card(Panel(SettingsSectionTitle(RecoveryHeaderReviewLabel(header.Role) + " · " + target.Page + "ページ"),
                    sourceImage, Text("自動読取: " + target.OriginalOcr),
                    SettingsDescription("見出しは原画像との照合で確認します。文字が一致している場合にチェックしてください。"), ack)));
                continue;
            }
            var cell = session.Plan.Document.Cells.Single(c => c.Id == target.Target.CellId);
            var slot = cell.Slots[0];
            var day = kind == MaterialKind.Timetable ? new[] { "", "月", "火", "水", "木", "金" }[int.Parse(slot.Day)] + "曜日" : slot.Day;
            var role = target.Target.Role switch { RecoveryFieldRole.Subject => "科目", RecoveryFieldRole.Teacher => "教員", _ => "教室" };
            var panel = Panel(SettingsSectionTitle(role), Text($"{ClassSelection.Display(slot.ClassName)} · {day} · {string.Join("・", cell.Slots.Select(s => s.Period).Distinct().Order())}限"));
            var original = session.Plan.Document.Capture!.OriginalCrops!.Single(c => c.Target == target.Target);
            var bitmap = new WriteableBitmap(original.Width, original.Height);
            using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(original.Bgra);
            bitmap.Invalidate();
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = 520, MaxHeight = 360, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(image, "manual-crop-" + key);
            AutomationProperties.SetName(image, "原本の訂正対象範囲");
            var cropPanel = Panel(SettingsDescription("原本の訂正対象範囲"), image, Text("自動読取: " + target.OriginalOcr));
            if (RecoveryCodeReview.Describe(target.OriginalOcr) is { } codeHint)
                cropPanel.Children.Add(SettingsDescription(codeHint));
            if (target.OriginalLabelIds is { } labelIds)
                cropPanel.Children.Add(SettingsDescription("原画像の項目の表示（" +
                    string.Concat(labelIds.Select(id => session.Plan.Document.Sources.Single(s => s.Id == id).Text)) +
                    "）と本文を合わせて確認してください。"));
            var cropBorder = new Border { Child = cropPanel, Padding = new Thickness(12), BorderThickness = new Thickness(2),
                BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] };
            var entryPanel = Panel();
            var comparison = new Grid { ColumnSpacing = 18, RowSpacing = 12 };
            comparison.ColumnDefinitions.Add(new()); comparison.ColumnDefinitions.Add(new());
            comparison.RowDefinitions.Add(new()); comparison.RowDefinitions.Add(new());
            comparison.Children.Add(cropBorder); comparison.Children.Add(entryPanel);
            void Reflow()
            {
                var wide = RecoveryCorrectionLayout.SideBySide(comparison.ActualWidth);
                comparison.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                comparison.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Grid.SetColumn(entryPanel, wide ? 1 : 0); Grid.SetRow(entryPanel, wide ? 0 : 1);
                comparison.RowDefinitions[0].Height = GridLength.Auto;
                comparison.RowDefinitions[1].Height = wide ? new GridLength(0) : GridLength.Auto;
            }
            comparison.SizeChanged += (_, _) => Reflow(); Reflow();
            var input = OperationControl(new TextBox { Text = _manualInput.Values[key], Header = "PDFに記載された全文", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 256,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") });
            AutomationProperties.SetAutomationId(input, "manual-value-" + key);
            var acknowledgement = OperationControl(new CheckBox { Content = "原本と一致することを確認", IsChecked = _manualInput.IsAcknowledged(key) });
            AutomationProperties.SetAutomationId(acknowledgement, "manual-ack-" + key);
            // TextChanged is asynchronous and can arrive after a subsequent
            // acknowledgement. Record edits and clear consent synchronously.
            input.TextChanging += (_, _) => { if (!_manualInput.IsBoundTo(identity)) return; _manualInput.Edit(key, input.Text); acknowledgement.IsChecked = _manualInput.IsAcknowledged(key); UpdateSubmit(); };
            acknowledgement.Checked += (_, _) => { if (!_manualInput.IsBoundTo(identity)) return; _manualInput.Acknowledge(key, true); UpdateSubmit(); };
            acknowledgement.Unchecked += (_, _) => { if (!_manualInput.IsBoundTo(identity)) return; _manualInput.Acknowledge(key, false); UpdateSubmit(); };
            entryPanel.Children.Add(input); entryPanel.Children.Add(acknowledgement); panel.Children.Add(comparison); Add(Card(panel));
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
        if (!_model.Preferences.UseAiFeatures) { _manualInput.Clear(); _correctionSelections.Clear(); Add(Text("復旧はOFFです。")); return; }
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
        if (!_selectionLifecycleBound)
        { _selectionLifecycleBound = true; _model.PrivateDataCleared += () => { _correctionSelections.Clear(); _updateRecoveryBatchStatus=null; }; }
        var selectionIdentity = RecoveryValidator.Fingerprint(new { preview.SourceId,preview.Lease,preview.Document,preview.Result });
        if (!_correctionSelections.TryGetValue(kind,out var selection) || selection.Identity != selectionIdentity)
            _correctionSelections[kind] = selection = (selectionIdentity,[]);
        var selected = selection.Targets;
        if (_recoveryClass is null || !document.Classes.Contains(_recoveryClass)) _recoveryClass = document.Classes.FirstOrDefault(c => _model.Preferences.SelectedClasses.Contains(c)) ?? document.Classes[0];
        var displayedClass = _recoveryClass;
        var roomTargets = document.RoomTargets(displayedClass);
        var selectionBoxes = new Dictionary<RecoveryFieldTarget, CheckBox>();
        Button? selectRooms = null; Button? clearSelection = null;
        var batch = Button("選択した項目をまとめて照合・訂正", () =>
            _model.BeginRecoveryCorrectionsAsync(kind,preview,selected.ToArray()),"recovery-edit-selected-"+kind);
        void UpdateBatch()
        {
            var enabled = !_model.Busy && !_model.Locked && _model.PreferencesReady && _model.Preferences.UseAiFeatures;
            batch.Content = $"選択した{selected.Count}項目をまとめて照合・訂正";
            batch.IsEnabled = selected.Count > 0 && enabled && document.SelectionFits(selected);
            if (selectRooms is not null) selectRooms.IsEnabled = enabled && roomTargets.Any(t => !selected.Contains(t)) &&
                document.SelectionFits(selected.Concat(roomTargets));
            if (clearSelection is not null) clearSelection.IsEnabled = enabled && selected.Count > 0;
        }
        selectRooms = Button("表示中のクラスの教室欄をまとめて選択", () => {
            if (_correctionSelections.GetValueOrDefault(kind).Identity != selectionIdentity) return Task.CompletedTask;
            if (document.SelectRooms(displayedClass, selected))
                foreach (var (target, box) in selectionBoxes) box.IsChecked = selected.Contains(target);
            UpdateBatch(); return Task.CompletedTask;
        }, "recovery-select-rooms-" + kind);
        clearSelection = Button("項目の選択をすべて解除", () => {
            if (_correctionSelections.GetValueOrDefault(kind).Identity != selectionIdentity) return Task.CompletedTask;
            selected.Clear(); foreach (var box in selectionBoxes.Values) box.IsChecked = false;
            UpdateBatch(); return Task.CompletedTask;
        }, "recovery-clear-selection-" + kind);
        _updateRecoveryBatchStatus=UpdateBatch;
        UpdateBatch();
        if (document.Cells.Any(c=>c.Lessons.Any(l=>!l.EditableRoles.IsEmpty)))
        {
            Add(batch);
            Add(SettingsDescription("訂正する項目を選択すると、クラスや曜日をまたいで原画像をまとめて確認できます。"));
        }
        Add(Card(Panel(SettingsSectionTitle("原本を確認してから使用してください"), Text($"{document.SchoolYear}年度 {document.Term} · {AppViewModel.MaterialLabel(kind)}"), Text("表示中のクラスだけでなく、資料全体・全クラスの結果を採用します。採用するまで時間割には反映されません。学校の資料と読み取り結果は外部へ送信されません。"),
            Button("対応する元PDFを確認", () => ShowPdf(kind, false, preview), "recovery-original-" + kind))));
        if (preview.Document.Capture?.ManualSchemaVersion is 2 or 3 or 4 && document.ReviewedFieldCount > 0)
            Add(SettingsDescription($"原本と照合した項目: {document.ReviewedFieldCount} · 文字を訂正した項目: {document.Corrections.Length}"));
        foreach (var review in preview.Result.HumanCorrections ?? [])
            if (review.HeaderProof is { } header)
                Add(SettingsDescription("原本と照合した見出し: " + RecoveryHeaderReviewLabel(header.Role) + " · " + review.Page +
                    "ページ · " + review.CorrectedText));
        if(!document.ReferenceNotes.IsEmpty)
        {
            var notes=Panel();
            foreach(var note in document.ReferenceNotes)
                notes.Children.Add(SettingsDescription(note.Page+"ページ · "+note.Text));
            var expander=new Expander {Header="原本の注記（"+document.ReferenceNotes.Length+"件）",Content=notes,HorizontalAlignment=HorizontalAlignment.Stretch};
            AutomationProperties.SetAutomationId(expander,"recovery-reference-notes-"+kind);
            Add(expander);
        }
        if (!document.Corrections.IsEmpty)
        {
            Add(SettingsSectionTitle("原本と照合して入力した箇所"));
            foreach (var correction in document.Corrections)
                Add(Card(Panel(SettingsSectionTitle(RecoveryTargetLabel(kind, correction.Slots, correction.LessonIndex, correction.Role)),
                    Text("自動読取: " + correction.OriginalOcr), Text("利用者の入力: " + correction.UserText),
                    SettingsDescription("原本と照合した利用者の入力として、自動読取とは別に記録されます。"))));
        }
        if (document.PreviousComparable)
        {
            Add(SettingsSectionTitle("前回の正式な結果との変更"));
            if (document.PreviousChanges.IsEmpty) Add(SettingsDescription("同じ原本に対応する前回の正式な結果から変更はありません。"));
            foreach (var change in document.PreviousChanges)
                Add(Card(Panel(SettingsSectionTitle(RecoveryTargetLabel(kind, change.Slots, change.LessonIndex, change.Role)),
                    Text("前回: " + change.PreviousText), Text("今回: " + change.CurrentText))));
        }
        else Add(SettingsDescription("対応を確認できる前回の正式な結果がないため、変更の比較は表示していません。"));
        Add(Button("表示するクラス：" + ClassSelection.Display(_recoveryClass), async () =>
        {
            var panel = Panel(); foreach (var cls in document.Classes) panel.Children.Add(Button(ClassSelection.Display(cls), () => { _recoveryClass = cls; _activeDialog?.Hide(); Render(); return Task.CompletedTask; }));
            await Dialog("確認するクラス", panel);
        }, "recovery-class"));
        if (roomTargets.Length > 0)
        {
            Add(selectRooms); Add(clearSelection);
            Add(SettingsDescription($"表示中のクラスの教室{roomTargets.Length}項目を選択します。確認済みの項目も含め全クラス合計{RecoveryManualAssistance.MaximumReviewFields}項目まで、原画像と照合できます。"));
        }
        var firstDay = document.Days.Min();
        foreach (var cell in document.Cells.Where(c => c.Slots.Any(s => s.ClassName == _recoveryClass)).OrderBy(c => c.Slots[0].Day).ThenBy(c => c.Slots[0].Period))
        {
            var slot = cell.Slots[0];
            var day = kind == MaterialKind.Timetable ? new[] { "", "月", "火", "水", "木", "金" }[int.Parse(slot.Day)] : slot.Day;
            var periods = string.Join("・", cell.Slots.Select(s => s.Period)) + "限";
            var content = Panel(SettingsSectionTitle(day + " " + periods));
            if (kind != MaterialKind.Timetable) { var span = cell.Slots.Length > 1 ? $"{cell.Slots.Min(s => s.Period)}-{cell.Slots.Max(s => s.Period)}" : slot.Period.ToString(); var clock = (cell.Slots.Length > 1 ? document.SpanTimes : document.Times).GetValueOrDefault(slot.Day + ":" + span); content.Children.Add(Text(clock ?? "時刻未確認")); if (kind == MaterialKind.ExamReturn) content.Children.Add(SettingsDescription(slot.Day == firstDay ? "初日専用の時刻" : "PDF中の注記に基づく通常授業時間")); }
            if (cell.State == RecoveryValueState.Empty) content.Children.Add(Text("空欄（原本で確認済み）"));
            for (var index = 0; index < cell.Lessons.Length; index++)
            {
                var lesson = cell.Lessons[index];
                var corrected = document.Corrections.Where(c => c.CellId == cell.CellId && c.LessonIndex == index).ToArray();
                var lessonPanel = Panel(Text(lesson.Subject + " / " + (lesson.TeacherEmpty ? "教員記載なし" : lesson.Teacher) + " / " + (lesson.RoomEmpty ? "教室記載なし" : lesson.Room)));
                foreach (var role in lesson.CodeReviewRoles)
                {
                    var value = role switch { RecoveryFieldRole.Subject => lesson.Subject, RecoveryFieldRole.Teacher => lesson.Teacher, _ => lesson.Room };
                    var hint = SettingsDescription(RecoveryRoleLabel(role) + " · " + RecoveryCodeReview.Describe(value));
                    AutomationProperties.SetAutomationId(hint, "recovery-code-hint-" + new RecoveryFieldTarget(cell.CellId, index, role).Key);
                    lessonPanel.Children.Add(hint);
                }
                foreach (var role in lesson.EditableRoles)
                {
                    var target = new RecoveryFieldTarget(cell.CellId, index, role);
                    var edit = OperationButton(RecoveryRoleLabel(role) + "を原本と照合・訂正",
                        () => _model.BeginRecoveryCorrectionAsync(kind, preview, target), "recovery-edit-" + target.Key);
                    var choose = OperationControl(new CheckBox { Content = "まとめて選択", IsChecked = selected.Contains(target) });
                    AutomationProperties.SetAutomationId(choose,"recovery-select-"+target.Key);
                    selectionBoxes.Add(target, choose);
                    choose.Checked += (_,_) => {
                        if (_correctionSelections.GetValueOrDefault(kind).Identity != selectionIdentity) return;
                        if (!document.SelectionFits(selected.Append(target))) { choose.IsChecked=false; return; }
                        selected.Add(target); UpdateBatch();
                    };
                    choose.Unchecked += (_,_) => {
                        if (_correctionSelections.GetValueOrDefault(kind).Identity != selectionIdentity) return;
                        selected.Remove(target); UpdateBatch();
                    };
                    var row = new Grid { ColumnSpacing=8 };
                    row.ColumnDefinitions.Add(new() { Width=new GridLength(1,GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
                    row.Children.Add(edit); row.Children.Add(choose); Grid.SetColumn(choose,1);
                    lessonPanel.Children.Add(row);
                }
                if (corrected.Length > 0)
                {
                    lessonPanel.Children.Insert(0, SettingsDescription("原本と照合した利用者の入力を含む授業: " + string.Join("・", corrected.Select(c => RecoveryRoleLabel(c.Role)))));
                    var highlight = new Border { Child = lessonPanel, Padding = new Thickness(12), BorderThickness = new Thickness(2),
                        BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] };
                    AutomationProperties.SetAutomationId(highlight, "recovery-corrected-" + cell.CellId + "-" + index);
                    content.Children.Add(highlight);
                }
                else content.Children.Add(lessonPanel);
            }
            Add(Card(content));
        }
        Add(OperationButton("資料全体を確認しました。全クラスの結果を使用", () => _model.AdoptRecoveryAsync(kind, preview), "adopt-recovery-" + kind));
        Add(TechnicalDetails(DataField("端末内Provider", document.Metadata.Provider), DataField("モデル", document.Metadata.ModelId + " " + document.Metadata.ModelVersion), DataField("検証版", document.Metadata.ValidatorVersion.ToString())));
    }
    private static string RecoveryHeaderReviewLabel(RecoveryHeaderReviewRole role) => role switch
    { RecoveryHeaderReviewRole.Year => "年度", RecoveryHeaderReviewRole.Term => "学期", RecoveryHeaderReviewRole.Class => "クラス",
        RecoveryHeaderReviewRole.Day => "曜日・日付", RecoveryHeaderReviewRole.Reference => "原本の注記", _ => "時限" };
    private static string RecoveryRoleLabel(RecoveryFieldRole role) => role switch
    { RecoveryFieldRole.Subject => "科目", RecoveryFieldRole.Teacher => "教員", _ => "教室" };
    private static string RecoveryTargetLabel(MaterialKind kind, System.Collections.Immutable.ImmutableArray<RecoverySlot> slots,
        int lessonIndex, RecoveryFieldRole role)
    {
        var locations = slots.GroupBy(s => (s.ClassName, s.Day)).Select(group => {
            var day = kind == MaterialKind.Timetable ? new[] { "", "月", "火", "水", "木", "金" }[int.Parse(group.Key.Day)] + "曜日" : group.Key.Day;
            return ClassSelection.Display(group.Key.ClassName) + " · " + day + " · " + string.Join("・", group.Select(s => s.Period).Distinct().Order()) + "限";
        });
        return string.Join(" / ", locations) + " · 授業" + (lessonIndex + 1) + " · " + RecoveryRoleLabel(role);
    }
    private void BuildRecoveryModels()
    {
        TitleText("端末内AIモデル", "page-ai-models"); BackToSettings();
        if (_model.RecoveryModelMessage is { } message) Add(Text(message));
        Add(Text("学校PDF・画像・OCR文字・科目・教員名・復旧結果は外部へ送信しません。モデルファイルの取得にだけインターネットを使用します。"));
        var ocrActions = Panel();
        if (!_model.OcrModelReady)
        {
            var download = OperationButton(_model.OcrModelInstalled ? "OCRモデルを更新" : "日本語・英数字OCRモデルをダウンロード",
                _model.InstallOcrModelAsync, "download-ocr-model");
            download.IsEnabled &= _model.Preferences.UseAiFeatures; ocrActions.Children.Add(download);
        }
        if (_model.OcrModelInstalled) ocrActions.Children.Add(OperationButton("OCRモデルを削除", _model.DeleteOcrModelAsync, "delete-ocr-model"));
        Add(Card(Panel(SettingsSectionTitle("日本語・英数字OCR"), Text(_model.OcrModelReady ? "確認済みモデルを保存しています。" : _model.OcrModelInstalled ? "改善モデルへ更新できます。更新に失敗した場合は保存済みモデルを保持します。" : "画像PDF用のモデルは未ダウンロードです。"), Text($"約{Math.Ceiling(WindowsRecoveryModels.OcrBundle.Size / 1048576d)} MB · {WindowsRecoveryModels.OcrBundle.License}"),
            ocrActions)));
        if (_model.FoundryModel is { } installed)
            Add(Card(Panel(SettingsSectionTitle("保存した端末内AIモデル"), Text(installed.ModelId),
                OperationButton("AIモデルを削除", _model.DeleteFoundryModelAsync, "delete-foundry-model"))));
        foreach (var model in _model.FoundryCandidates)
        {
            var content = Panel(SettingsSectionTitle("追加の端末内AIモデル"), Text(model.ModelId), Text($"約{model.Size / 1024 / 1024} MB · {model.License}"));
            if (!model.Validated) content.Children.Add(Text("この候補は読み取り精度の検証中です。検証が完了するまで配信しません。"));
            else if (_model.FoundryModel?.ModelId == model.ModelId) content.Children.Add(Text("このモデルを保存しています。"));
            else if (_model.Preferences.UseAiFeatures) content.Children.Add(OperationButton("端末内AIモデルをダウンロード", () => _model.InstallFoundryModelAsync(model), "download-foundry-model"));
            Add(Card(content));
        }
        Add(Card(Panel(SettingsSectionTitle("Windows標準の生成AI"), Text("対応端末で準備済みの場合に使用します。通常解析とルール復旧を優先し、自動的に大きなモデルをダウンロードしません。"))));
    }
}
