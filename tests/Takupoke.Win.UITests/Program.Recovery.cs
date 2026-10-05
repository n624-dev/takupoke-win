using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Automation;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    // A prevalidated preview isolates UI and transactional adoption. Native
    // model/recognition behavior is covered separately, with fictional inputs.
    private static (RecoveryDocument Doc, RecoveryResult Result) RecoveryUiFixture(SchoolDataPeriod period, bool parallel = false)
    {
        var slots = (from d in Enumerable.Range(1, 5) from p in Enumerable.Range(1, 8) select new RecoverySlot("3_IT", d.ToString(), p)).ToArray();
        var box = new RecoveryBox(110, 110, 60, 10);
        var sources = new List<RecoverySource> { new RecoverySource("heading", "header", 1, $"{period.SchoolYear}年度", new(0, 0, 90, 10)),
            new RecoverySource("subject", "c0", 1, "架空科目A", box), new RecoverySource("teacher", "c0", 1, "架空教員A", box), new RecoverySource("room", "c0", 1, "架空教室A", box) };
        sources.Add(new("term", "header", 1, period.Half == 1 ? "前期" : "後期", new(0, 20, 40, 10)));
        sources.Add(new("class", "header", 1, "3_IT", new(10, 110, 20, 10)));
        sources.AddRange(Enumerable.Range(1, 5).Select(d => new RecoverySource("day" + d, "header", 1, new[] { "月", "火", "水", "木", "金" }[d - 1], new(d * 100 + 10, 20, 60, 10))));
        sources.AddRange(Enumerable.Range(1, 8).Select(p => new RecoverySource("period" + p, "header", 1, p.ToString(), new(50, p * 100 + 10, 20, 10))));
        var cells = slots.Select((slot, i) => new RecoveryCell("c" + i, 1, new(int.Parse(slot.Day) * 100, slot.Period * 100, 100, 100), RecoveryInputState.Complete, [slot], i == 0 ? ["subject", "teacher", "room"] : [], [], i != 0) { ClassHeaderIds = ["class"], DayHeaderIds = ["day" + slot.Day], PeriodHeaderIds = ["period" + slot.Period], LessonBindings = i == 0 ? [new(["subject"], ["teacher"], ["room"])] : [], ClassRegion = new(1, new(0, 100, 40, 800), RecoveryHeaderAxis.Left), DayRegion = new(1, new(int.Parse(slot.Day) * 100, 0, 100, 50), RecoveryHeaderAxis.Above), PeriodRegions = new Dictionary<string, RecoveryHeaderRegion> { [slot.Period.ToString()] = new(1, new(40, slot.Period * 100, 40, 100), RecoveryHeaderAxis.Left) } }).ToArray();
        var doc = new RecoveryDocument(new string('a', 64), RecoveryDocumentKind.Timetable, period.SchoolYear, period.Half == 1 ? "前期" : "後期", ["3_IT"], ["1", "2", "3", "4", "5"], slots, cells, sources, true, ["heading"], ["term"],
            Enumerable.Range(1, 5).ToDictionary(i => i.ToString(), i => (IReadOnlyList<string>)new[] { "day" + i }), new Dictionary<string, IReadOnlyList<string>> { ["3_IT"] = ["class"] },
            Enumerable.Range(1, 8).ToDictionary(i => i.ToString(), i => (IReadOnlyList<string>)new[] { "period" + i }), new Dictionary<string, string>(), [], []);
        var lesson = new RecoveryLesson(new(RecoveryValueState.Present, "架空科目A", ["subject"]), new(RecoveryValueState.Present, "架空教員A", ["teacher"]), new(RecoveryValueState.Present, "架空教室A", ["room"]), ["day1"], ["period1"]);
        var result = new RecoveryResult(doc.PdfHash, doc.Kind, period.SchoolYear, period.Half == 1 ? "前期" : "後期", cells.Select((c, i) => new RecoveredCell(c.Id, i == 0 ? RecoveryValueState.Present : RecoveryValueState.Empty, i == 0 ? [lesson] : [])).ToArray(),
            new("rule", "rules", "1", "1", "1", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, "test"));
        // Independently confirmed blank teacher; never map unreadable to empty.
        doc = doc with { Sources = doc.Sources.Where(s => s.Id != "teacher").Select(s => s.Id == "room" ? s with { Box = s.Box with { Y = 145 } } : s).ToArray(),
            Cells = doc.Cells.Select((c, i) => i == 0 ? c with { SourceIds = ["subject", "room"], BlankFields = ["teacher"], LessonBindings = [c.LessonBindings[0] with { Teacher = [] }] } : c).ToArray() };
        result = result with { Cells = result.Cells.Select((c, i) => i == 0 ? c with { Lessons = [c.Lessons[0] with { Teacher = new(RecoveryValueState.Empty, "", []) }] } : c).ToArray() };
        if (parallel)
        {
            var atoms = new List<RecoverySource>(); var lessons = new List<RecoveryLesson>(); var bindings = new List<RecoveryLessonBinding>();
            foreach (var index in Enumerable.Range(0, 2))
            {
                var suffix = index == 0 ? "A" : "B";
                var ids = new[] { "parallel-subject-" + index, "parallel-teacher-" + index, "parallel-room-" + index };
                var values = new[] { "架空並記科目" + suffix, "架空並記担当" + suffix, "架空並記教室" + suffix };
                atoms.AddRange(ids.Select((id, role) => new RecoverySource(id, "c0", 1, values[role], new(110, 110 + index * 45 + role * 15, 80, 10))));
                bindings.Add(new([ids[0]], [ids[1]], [ids[2]]));
                lessons.Add(new(new(RecoveryValueState.Present, values[0], [ids[0]]), new(RecoveryValueState.Present, values[1], [ids[1]]),
                    new(RecoveryValueState.Present, values[2], [ids[2]]), ["day1"], ["period1"]));
            }
            doc = doc with { Sources = doc.Sources.Where(s => s.CellId != "c0").Concat(atoms).ToArray(),
                Cells = doc.Cells.Select((c, i) => i == 0 ? c with { ParallelCount = 2, SourceIds = atoms.Select(s => s.Id).ToArray(), BlankFields = [], LessonBindings = bindings } : c).ToArray() };
            result = result with { Cells = result.Cells.Select((c, i) => i == 0 ? c with { Lessons = lessons } : c).ToArray() };
        }
        return (doc, result);
    }
    private static byte[] RecoveryUiPdf()
    {
        const string content = "BT /F1 12 Tf 20 160 Td (Entirely synthetic recovery original) Tj ET";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"];
        var pdf = new StringBuilder("%PDF-1.7\n"); var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString())); pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString()); pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n"); return Encoding.ASCII.GetBytes(pdf.ToString());
    }
    private static async Task<string> SeedRecoveryUiAsync(string root, bool parallel = false)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector()); var lease = await store.BeginAsync();
        var bytes = RecoveryUiPdf(); var path = Path.Combine(root, "fictional-recovery-original.pdf"); await File.WriteAllBytesAsync(path, bytes);
        using var content = await new FileSourceReader(new WindowsFileIdentity()).ReadAsync(path, MaterialKind.Timetable, null);
        var now = DateTimeOffset.UtcNow; var hash = NotificationDiff.Digest(bytes);
        var source = new SourceRecord(Guid.NewGuid().ToString("N"), MaterialKind.Timetable, path, content.Identity, "fictional-recovery.pdf", hash, bytes.Length, now, now, content.ModifiedAt);
        await store.SaveOriginalAsync(lease, source, bytes);
        await store.WriteAsync(lease, "acquisition.Timetable", new MaterialAttempt(now, null, false, hash));
        await store.WriteAsync(lease, "attempt.Timetable", new MaterialAttempt(now, "P13", true, hash, lease.Period.SchoolYear, ParserVersion: PdfScheduleParser.TimetableVersion, RecoveryPending: true));
        var (doc, result) = RecoveryUiFixture(lease.Period, parallel); doc = doc with { PdfHash = hash }; result = result with { PdfHash = hash };
        var errors = RecoveryValidator.Validate(doc, result).Errors;
        if (errors.Count > 0) throw new InvalidOperationException("Synthetic recovery preview failed validation: " + string.Join(",", errors));
        var preview = new RecoveryPreview(source.Id, lease, doc, result, now);
        var job = new RecoveryJob(hash, RecoveryDocumentKind.Timetable, RecoveryJobState.Pending, now);
        await store.WriteAsync(lease, "recovery.Timetable", job);
        await store.SaveRecoveryProgressAsync(lease, source, job with { State = RecoveryJobState.AwaitingConfirmation, ResultHash = RecoveryValidator.Fingerprint(result) }, preview);
        return hash;
    }
    private static async Task<MaterialAnalysis?> RecoveryUiFormalAsync(string root, MaterialKind kind = MaterialKind.Timetable)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector()); var lease = await store.BeginAsync();
        return await store.ReadAsync<MaterialAnalysis>(lease, "analysis." + kind);
    }
    private sealed record RecoveryUiSpecialFixture(RecoveryDocument Document, RecoveryResult Result);
    private static async Task SeedRecoverySpecialUiAsync(string root, MaterialKind kind)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector()); var lease = await store.BeginAsync();
        var name = kind == MaterialKind.Exam ? "exam" : "return";
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "recovery-" + name + ".json"));
        // Move all ISO dates, evidence and date-keyed clocks into the active
        // school half before advancing the wholly fictional fixture year.
        var month = lease.Period.Half == 1 ? "04" : "10";
        json = json.Replace("2026-10-", $"{lease.Period.SchoolYear}-{month}-", StringComparison.Ordinal);
        json = json.Replace("10月", lease.Period.Half == 1 ? "4月" : "10月", StringComparison.Ordinal);
        json = json.Replace("10/", lease.Period.Half == 1 ? "4/" : "10/", StringComparison.Ordinal);
        json = json.Replace("2026", lease.Period.SchoolYear.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var fixture = DataCodec.Decode<RecoveryUiSpecialFixture>(Encoding.UTF8.GetBytes(json));
        // The immutable fixture records v4; native preview/adoption tests use
        // the current validator while preserving its original semantic inputs.
        fixture = fixture with { Result = fixture.Result with { Metadata = fixture.Result.Metadata with { ValidatorVersion = RecoveryValidator.Version } } };
        var bytes = RecoveryUiPdf(); var path = Path.Combine(root, "fictional-recovery-" + name + ".pdf"); await File.WriteAllBytesAsync(path, bytes);
        using var content = await new FileSourceReader(new WindowsFileIdentity()).ReadAsync(path, kind, null);
        var now = DateTimeOffset.UtcNow; var hash = NotificationDiff.Digest(bytes);
        var source = new SourceRecord(Guid.NewGuid().ToString("N"), kind, path, content.Identity, "fictional-recovery-" + name + ".pdf", hash, bytes.Length, now, now, content.ModifiedAt);
        await store.SaveOriginalAsync(lease, source, bytes);
        await store.WriteAsync(lease, "acquisition." + kind, new MaterialAttempt(now, null, false, hash));
        await store.WriteAsync(lease, "attempt." + kind, new MaterialAttempt(now, "P13", true, hash, lease.Period.SchoolYear, ParserVersion: PdfScheduleParser.SpecialVersion, RecoveryPending: true));
        var doc = fixture.Document with { PdfHash = hash }; var result = fixture.Result with { PdfHash = hash };
        var errors = RecoveryValidator.Validate(doc, result).Errors;
        if (errors.Count > 0) throw new InvalidOperationException("Synthetic special recovery preview failed validation: " + string.Join(",", errors));
        var job = new RecoveryJob(hash, doc.Kind, RecoveryJobState.Pending, now);
        await store.WriteAsync(lease, "recovery." + kind, job);
        await store.SaveRecoveryProgressAsync(lease, source, job with { State = RecoveryJobState.AwaitingConfirmation, ResultHash = RecoveryValidator.Fingerprint(result) }, new(source.Id, lease, doc, result, now));
    }
    private static void CheckRecoverySpecialUi(string executable, string root, MaterialKind kind)
    {
        Stop(); SeedRecoverySpecialUiAsync(root, kind).GetAwaiter().GetResult(); Start(executable);
        OpenRecoveryUiPreview(kind);
        Invoke("recovery-class"); Invoke(ByName("1-1"));
        Wait(() => RecoveryUiText("08:00〜09:00"), "The merged lesson uses its explicit span clock rather than the first period clock");
        Require(RecoveryUiText(kind == MaterialKind.Exam ? "08:05〜08:30" : "08:50〜09:35"), "Every day retains its own source-grounded clock, including confirmed empty cells.");
        if (kind == MaterialKind.ExamReturn) Require(RecoveryUiText("PDF中の注記に基づく通常授業時間"), "Return timetable distinguishes note-grounded later-day clocks from first-day clocks.");
        Invoke("adopt-recovery-" + kind);
        Wait(() => Find("adopt-recovery-" + kind) is null, "Explicit special adoption closes confirmation");
        var accepted = RecoveryUiFormalAsync(root, kind).GetAwaiter().GetResult();
        Require(accepted?.Recovery is not null && accepted.Special?.Lessons.Any(l => l.SpanStart == 1 && l.SpanEnd == 2) == true, "Special schedule adoption preserves the explicit merged period span.");
        Stop(); Start(executable);
        var restarted = RecoveryUiFormalAsync(root, kind).GetAwaiter().GetResult();
        Require(restarted?.Recovery is not null && restarted.Recovery.Document.PdfHash == accepted?.Recovery?.Document.PdfHash && restarted.Special?.Lessons.Any(l => l.SpanStart == 1 && l.SpanEnd == 2) == true, "The adopted special document and merged span persist across native app restart.");
    }
    private static bool RecoveryUiText(string fragment) => _window?.FindFirst(TreeScope.Descendants,
        new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text), new PropertyCondition(AutomationElement.NameProperty, fragment))) is not null;
    private static void CheckRecoveryParallelUi(string executable, string root)
    {
        Stop(); SeedRecoveryUiAsync(root, parallel: true).GetAwaiter().GetResult(); Start(executable);
        OpenRecoveryUiPreview();
        foreach (var suffix in new[] { "A", "B" }) Require(RecoveryUiText($"架空並記科目{suffix} / 架空並記担当{suffix} / 架空並記教室{suffix}"),
            "The preview keeps each parallel subject, teacher and room together: " + suffix);
        foreach (var suffix in new[] { "A", "B" })
        {
            var text = ByName($"架空並記科目{suffix} / 架空並記担当{suffix} / 架空並記教室{suffix}", ControlType.Text);
            var bounds = text.Current.BoundingRectangle;
            Require(!text.Current.IsOffscreen && !bounds.IsEmpty && _window!.Current.BoundingRectangle.Contains(bounds),
                "Both parallel lesson pairs are simultaneously visible in the preview: " + suffix);
        }
        Capture("windows-recovery-parallel", "TAKUPOKE_PARALLEL_UI_IMAGE");
        Require(RecoveryUiFormalAsync(root).GetAwaiter().GetResult()?.Timetable?.Lessons.Count != 2,
            "The parallel preview leaves the previously adopted formal result unchanged.");
        Invoke("adopt-recovery-Timetable");
        Wait(() => Find("adopt-recovery-Timetable") is null, "Explicit parallel adoption removes the confirmation action");
        var accepted = RecoveryUiFormalAsync(root).GetAwaiter().GetResult();
        Require(accepted?.Recovery is not null && accepted.Timetable?.Lessons.Count == 2 && accepted.Timetable.Lessons.All(l => l.ClassName == "3_IT" && l.Weekday == 1 && l.Period == 1),
            "The formal result retains both lessons in the same class, weekday and period.");
        Stop(); Start(executable);
        Navigate("settings"); Invoke("settings-materials"); Invoke("material-details-Timetable"); Invoke("analysis-Timetable");
        foreach (var suffix in new[] { "A", "B" })
        {
            Wait(() => RecoveryUiText("架空並記科目" + suffix), "The restarted formal analysis displays parallel subject " + suffix);
            Wait(() => RecoveryUiText($"架空並記担当{suffix} · 架空並記教室{suffix}"), "The restarted formal analysis retains the paired teacher and room " + suffix);
        }
        Console.WriteLine("Recovery parallel UI: two paired lessons in preview, explicit adoption and restarted formal analysis passed.");
    }
    private static void OpenRecoveryUiPreview(MaterialKind kind = MaterialKind.Timetable)
    {
        Navigate("settings"); Invoke("settings-materials"); Invoke("material-details-" + kind); Invoke("recovery-preview-" + kind);
        Wait(() => Find("page-recovery-" + kind) is not null, "validated recovery preview opens");
    }
    private static void CaptureRecoveryUi(string label)
    {
        OpenRecoveryUiPreview(); Capture(label + "-recovery-preview");
        var window = _window!.Current.BoundingRectangle;
        Require(SetWindowPos(_process!.MainWindowHandle, 0, (int)window.Left, (int)window.Top, 680, 680, 0x0044), "Resize recovery review window");
        Capture(label + "-recovery-preview-narrow");
        Invoke("recovery-original-Timetable"); Wait(() => RecoveryUiText("1 / 1ページ"), "Recovery original renders for screen review");
        Capture(label + "-recovery-original"); Invoke(ByName("閉じる"));
        Navigate("settings"); Invoke("settings-recovery-models"); Wait(() => Find("page-ai-models") is not null, "capture AI model settings");
        Capture(label + "-recovery-models");
    }
    private static void CheckRecoveryUi(string executable, string root)
    {
        Stop(); var hash = SeedRecoveryUiAsync(root).GetAwaiter().GetResult(); Start(executable);
        OpenRecoveryUiPreview();
        Require(RecoveryUiText("架空科目A / 教員記載なし / 架空教室A"), "The confirmed blank teacher keeps the original room in its own field.");
        Require(RecoveryUiText("空欄（原本で確認済み）"), "Confirmed empty cells are shown separately from lessons.");
        Require(Find("adopt-recovery-Timetable")!.Current.Name.Contains("全クラス", StringComparison.Ordinal), "The adoption scope explicitly includes the whole document.");
        Require(RecoveryUiFormalAsync(root).GetAwaiter().GetResult()?.Recovery is null, "Preview opening leaves the previous formal analysis untouched.");
        Invoke("recovery-original-Timetable"); Wait(() => RecoveryUiText("1 / 1ページ"), "The hash-bound saved original renders in the native PDF viewer");
        Invoke(ByName("閉じる")); Invoke("back-recovery");
        Require(Find("recovery-preview-Timetable") is not null, "Returning from preview retains the confirmation job without adopting it.");
        Stop(); Start(executable); OpenRecoveryUiPreview();
        Require(RecoveryUiFormalAsync(root).GetAwaiter().GetResult()?.Recovery is null, "Restart retains both the pending preview and previous formal analysis.");
        Invoke("adopt-recovery-Timetable");
        Wait(() => Find("adopt-recovery-Timetable") is null && Find("page-recovery-Timetable") is not null, "Explicit adoption removes the confirmation action");
        var accepted = RecoveryUiFormalAsync(root).GetAwaiter().GetResult();
        Require(accepted?.SourceDigest == hash && accepted.Recovery is not null && accepted.Timetable!.Lessons.Single().Names.Teacher == "", "Only the explicit adoption action commits the hash-bound validated result and blank teacher.");
        Stop(); Start(executable);
        Require(RecoveryUiFormalAsync(root).GetAwaiter().GetResult()?.Recovery?.Document.PdfHash == hash, "Recovery adoption persists across native app restart.");
        Navigate("settings"); Invoke("settings-recovery-models"); Wait(() => Find("page-ai-models") is not null, "Model management remains accessible after successful adoption");
        Require(Find("download-foundry-model") is null, "A quality-unvalidated additional model has no download action.");
        Require(Find("download-ocr-model") is not null, "The OCR download action is available independently of a pending recovery job.");
        CheckRecoverySpecialUi(executable, root, MaterialKind.Exam);
        CheckRecoverySpecialUi(executable, root, MaterialKind.ExamReturn);
        CheckRecoveryLatestAcquisitionFailure(executable, root);
        CheckRecoveryParallelUi(executable, root);
        CheckManualRecoveryUi(executable, root);
        Console.WriteLine("Recovery UI: original, empty fields/cells, whole-document scope, cancellation by leaving, restart, explicit adoption and independent model management passed.");
    }
    private static void CheckRecoveryLatestAcquisitionFailure(string executable, string root)
    {
        Stop(); SeedRecoveryUiAsync(root).GetAwaiter().GetResult();
        var before = DetailStoreAsync(root).GetAwaiter().GetResult();
        File.Delete(before.Source.Path); Start(executable);
        // Offline fixtures deliberately skip the production startup refresh.
        // Exercise the real acquisition control before expecting its failure UI.
        Navigate("settings"); Invoke("settings-materials"); Invoke("material-details-Timetable");
        Wait(() => Find("reacquire-Timetable") is { Current.IsEnabled: true }, "Latest acquisition control is ready");
        Invoke("reacquire-Timetable");
        Wait(() => RecoveryUiAcquisitionFailedAsync(root).GetAwaiter().GetResult(), "The real acquisition reports the deleted original");
        Wait(() => Find("reacquire-Timetable") is { Current.IsEnabled: true }, "The failed acquisition has finished reloading its snapshot");
        Invoke("recovery-preview-Timetable");
        Wait(() => RecoveryUiText("最新の原本を取得できないため採用できません。資料の詳細から再取得してください。前回の正常結果は保持しています。"),
            "Latest acquisition failure explains why pending recovery cannot be adopted");
        Require(Find("adopt-recovery-Timetable") is null && Find("recovery-original-Timetable") is null,
            "An unavailable latest original hides both adoption and the pending original viewer");
        Require(DetailStoreAsync(root).GetAwaiter().GetResult().Analysis.SourceDigest == before.Analysis.SourceDigest,
            "Latest acquisition failure keeps the previous accepted formal result");
        Capture("latest-original-unavailable-recovery");
        Invoke("back-recovery"); File.WriteAllBytes(before.Source.Path, RecoveryUiPdf());
        Invoke("reacquire-Timetable");
        Wait(() => DetailStoreAsync(root).GetAwaiter().GetResult().Source.FileIdentity != before.Source.FileIdentity,
            "Restoring the identical selected PDF reacquires its new native file identity");
        Wait(() => Find("reacquire-Timetable") is { Current.IsEnabled: true }, "The restored acquisition has finished reloading its snapshot");
        Invoke("recovery-preview-Timetable");
        Wait(() => Find("adopt-recovery-Timetable") is not null && Find("recovery-original-Timetable") is not null,
            "Successful identical-byte reacquisition restores confirmation for the same hash-bound preview");
    }
    private static async Task<bool> RecoveryUiAcquisitionFailedAsync(string root)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector()); var lease = await store.BeginAsync();
        return (await store.ReadAsync<MaterialAttempt>(lease, "acquisition.Timetable"))?.Failure is not null;
    }
}
