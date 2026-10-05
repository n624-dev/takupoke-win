using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Takupoke.Core.Recovery;

// App-owned acquisition/provenance only. These records are never provider wire fields.
public sealed record RecoveryCapturedPage(int Page, int Width, int Height, string RasterHash, int SourceCount, bool InkCoverageVerified);
public sealed record RecoveryCaptureReceipt(string PdfHash, int PageCount, string SourceSnapshot, IReadOnlyList<RecoveryCapturedPage> Pages, int ManualSchemaVersion = 1)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RecoveryOriginalCrop>? OriginalCrops { get; init; }
}
public sealed record RecoveryOriginalCrop(RecoveryFieldTarget Target, int Page, int PixelX, int PixelY,
    int Width, int Height, string OriginalRasterHash, byte[] Bgra, string Sha256);
public sealed record RecoveryFieldTarget(string CellId, int LessonIndex, RecoveryFieldRole Role)
{
    public string Key => CellId + ":" + LessonIndex + ":" + Role;
}
public sealed record RecoveryManualTarget(RecoveryFieldTarget Target, int Page, RecoveryBox Crop,
    IReadOnlyList<string> OriginalParentIds, string OriginalOcr);
public sealed record RecoveryHumanCorrection(RecoveryFieldTarget Target, string PdfHash, string AcquisitionHash, string DocumentSnapshot,
    IReadOnlyList<string> OriginalParentIds, int Page, RecoveryBox Crop, string CorrectedText,
    bool ExplicitBlank, DateTimeOffset EnteredAt, string Provenance = "user", int ManualSchemaVersion = 1);
public sealed record RecoveryManualPlan(RecoveryDocument Document, string DocumentSnapshot, IReadOnlyList<RecoveryManualTarget> Targets);

public static class RecoveryManualAssistance
{
    public const int MaximumFields = 3;
    public const int MaximumCropPixels = 262144;
    // Acquisition-only geometry request. This cannot authorize correction without original pixel receipts.
    public static IReadOnlyList<RecoveryManualTarget>? CaptureTargets(RecoveryDocument doc, CancellationToken token = default)
    {
        if (RecoveryValidator.InputErrors(doc, token).Count != 0) return null;
        try { return Targets(doc, new RecoveryWorkBudget(token), requireCrops: false); }
        catch (Exception error) when (error is RecoveryWorkLimitException or NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
    public static RecoveryManualPlan? Prepare(RecoveryDocument doc, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (RecoveryValidator.InputErrors(doc, token).Count != 0) return null;
        var work = new RecoveryWorkBudget(token);
        IReadOnlyList<RecoveryManualTarget>? targets;
        try { targets = Targets(doc, work); } catch (Exception error) when (error is RecoveryWorkLimitException or NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return null; }
        if (targets is not { Count: > 0 and <= MaximumFields }) return null;
        return new(doc, RecoveryValidator.Fingerprint(doc), targets);
    }
    internal static IReadOnlyList<RecoveryManualTarget>? Targets(RecoveryDocument doc, RecoveryWorkBudget work, bool requireCrops = true)
    {
        work.Step();
        var capture = doc.Capture;
        if (!doc.Complete || doc.Cells.Count > 20000 || doc.Sources.Count > 100000 || capture is null ||
            capture.PdfHash != doc.PdfHash || capture.ManualSchemaVersion != 1 || capture.PageCount is < 1 or > 12 || capture.Pages.Count != capture.PageCount) return null;
        foreach (var source in doc.Sources) work.Step(1L + source.Text.Length);
        if (capture.SourceSnapshot != RecoveryValidator.Fingerprint(doc.Sources)) return null;
        var pages = new Dictionary<int, RecoveryCapturedPage>();
        foreach (var p in capture.Pages)
        {
            work.Step();
            if (p.Page < 1 || p.Page > capture.PageCount || p.Width is < 1 or > 4096 || p.Height is < 1 or > 4096 ||
                !p.InkCoverageVerified || p.RasterHash.Length != 64 || p.RasterHash.Any(c => !char.IsAsciiHexDigit(c)) ||
                !pages.TryAdd(p.Page, p)) return null;
        }
        var sourceCounts = new Dictionary<int, int>(); var sources = new Dictionary<string, RecoverySource>();
        var uncertain = new HashSet<string>();
        foreach (var s in doc.Sources)
        {
            work.Step();
            if (!sources.TryAdd(s.Id, s) || !pages.TryGetValue(s.Page, out var page) || !s.Box.Valid ||
                s.Box.X + s.Box.Width > page.Width || s.Box.Y + s.Box.Height > page.Height) return null;
            sourceCounts[s.Page] = sourceCounts.GetValueOrDefault(s.Page) + 1;
            if (s.FromOcr)
            {
                if (s.NativeConfidence is not { } confidence || !double.IsFinite(confidence) || confidence is < 0 or > 1) return null;
                if (confidence < .8) uncertain.Add(s.Id);
            }
            else if (s.NativeConfidence is not null) return null;
        }
        if (pages.Values.Any(p => sourceCounts.GetValueOrDefault(p.Page) != p.SourceCount)) return null;
        var assigned = new HashSet<string>(); var targets = new List<RecoveryManualTarget>();
        foreach (var cell in doc.Cells)
        {
            work.Step(cell.LessonBindings.Count);
            if (cell.InputState != RecoveryInputState.Complete || cell.BindingMode != RecoveryBindingMode.Fixed) return null;
            for (var lesson = 0; lesson < cell.LessonBindings.Count; lesson++)
            {
                var binding = cell.LessonBindings[lesson];
                foreach (var (role, ids) in new[] { (RecoveryFieldRole.Subject, binding.Subject), (RecoveryFieldRole.Teacher, binding.Teacher), (RecoveryFieldRole.Room, binding.Room) })
                {
                    work.Step(ids.Count * 5L);
                    if (!ids.Any(uncertain.Contains)) continue;
                    if (ids.Count == 0 || ids.Any(id => !sources.ContainsKey(id))) return null;
                    foreach (var id in ids.Where(uncertain.Contains)) if (!assigned.Add(id)) return null;
                    var atoms = ids.Select(id => sources[id]).ToArray();
                    if (atoms.Any(s => s.CellId != cell.Id || s.Page != cell.Page || !cell.Box.Contains(s.Box))) return null;
                    var left = atoms.Min(s => s.Box.X); var top = atoms.Min(s => s.Box.Y);
                    var box = new RecoveryBox(left, top, atoms.Max(s => s.Box.X + s.Box.Width) - left, atoms.Max(s => s.Box.Y + s.Box.Height) - top);
                    targets.Add(new(new(cell.Id, lesson, role), cell.Page, box, ids.ToArray(), work.Concat(atoms.Select(s => s.Text))));
                    if (targets.Count > MaximumFields) return null;
                }
            }
        }
        work.Step(uncertain.Count);
        if (!assigned.SetEquals(uncertain)) return null;
        if (requireCrops && targets.Count > 0)
        {
            if (capture.OriginalCrops is not { } crops || crops.Count != targets.Count) return null;
            var byTarget = new Dictionary<RecoveryFieldTarget, RecoveryOriginalCrop>();
            foreach (var crop in crops)
            {
                work.Step();
                if (!byTarget.TryAdd(crop.Target, crop) || !pages.TryGetValue(crop.Page, out var page) ||
                    crop.Width < 1 || crop.Height < 1 || (long)crop.Width * crop.Height > MaximumCropPixels ||
                    crop.PixelX < 0 || crop.PixelY < 0 || (long)crop.PixelX + crop.Width > page.Width ||
                    (long)crop.PixelY + crop.Height > page.Height || crop.OriginalRasterHash != page.RasterHash ||
                    crop.Bgra.LongLength != (long)crop.Width * crop.Height * 4) return null;
                work.Step(crop.Bgra.Length * 4L); // Hash plus subsequent immutable snapshot serialization.
                if (Convert.ToHexStringLower(SHA256.HashData(crop.Bgra)) != crop.Sha256) return null;
                work.Step();
            }
            foreach (var target in targets)
            {
                work.Step();
                if (!byTarget.TryGetValue(target.Target, out var crop) || crop.Page != target.Page ||
                    crop.PixelX != (int)Math.Floor(target.Crop.X) || crop.PixelY != (int)Math.Floor(target.Crop.Y) ||
                    crop.Width != (int)Math.Ceiling(target.Crop.X + target.Crop.Width) - crop.PixelX ||
                    crop.Height != (int)Math.Ceiling(target.Crop.Y + target.Crop.Height) - crop.PixelY) return null;
            }
        }
        return targets;
    }
    internal static Dictionary<RecoveryFieldTarget, RecoveryHumanCorrection>? Corrections(RecoveryDocument doc, RecoveryResult result, RecoveryWorkBudget work)
    {
        var corrections = result.HumanCorrections;
        if (corrections is null) return null;
        work.Step(corrections.Count);
        if (corrections.Count is < 1 or > MaximumFields) return null;
        var targets = Targets(doc, work);
        if (targets is null || targets.Count != corrections.Count) return null;
        var snapshot = RecoveryValidator.Fingerprint(doc);
        var allowed = targets.ToDictionary(t => t.Target);
        var output = new Dictionary<RecoveryFieldTarget, RecoveryHumanCorrection>();
        foreach (var correction in corrections)
        {
            work.Step(correction.OriginalParentIds.Count + correction.CorrectedText.Length);
            if (!allowed.TryGetValue(correction.Target, out var target) || !output.TryAdd(correction.Target, correction) ||
                correction.PdfHash != doc.PdfHash || correction.DocumentSnapshot != snapshot || correction.Page != target.Page ||
                correction.AcquisitionHash != RecoveryValidator.Fingerprint(doc.Capture) || correction.ManualSchemaVersion != 1 ||
                correction.Crop != target.Crop || !correction.OriginalParentIds.SequenceEqual(target.OriginalParentIds) ||
                correction.EnteredAt == default || correction.Provenance != "user" || correction.CorrectedText.Length > 256) return null;
            // A low-confidence printed atom cannot become a proved empty field.
            // No new emptyVerified/white-pixel proof is created by user input.
            if (correction.ExplicitBlank || string.IsNullOrWhiteSpace(correction.CorrectedText)) return null;
        }
        return output;
    }
    public static RecoveryResult Complete(RecoveryManualPlan plan, IReadOnlyList<RecoveryHumanCorrection> corrections,
        string osVersion, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var expectedTargets = Prepare(plan.Document, token)?.Targets;
        if (expectedTargets is null || RecoveryValidator.Fingerprint(expectedTargets) != RecoveryValidator.Fingerprint(plan.Targets))
            throw new InvalidRecoveryOutputException();
        var raw = RecoveryRules.RecoverAll(plan.Document, token).Select((cell, i) => cell ??
            (plan.Document.Cells[i].ConfirmedEmpty && plan.Document.Cells[i].SourceIds.Count == 0
                ? new RecoveredCell(plan.Document.Cells[i].Id, RecoveryValueState.Empty, []) : null)).ToArray();
        if (raw.Any(c => c is null) || plan.DocumentSnapshot != RecoveryValidator.Fingerprint(plan.Document))
            throw new InvalidRecoveryOutputException();
        var metadata = plan.Document.StructureMetadata ?? new RecoveryMetadata("rule", "rules", "3", "3", "1", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, osVersion);
        var result = new RecoveryResult(plan.Document.PdfHash, plan.Document.Kind, plan.Document.SchoolYear, plan.Document.Term,
            raw.Select(c => c!).ToArray(), metadata) { HumanCorrections = corrections.ToArray() };
        var verified = Corrections(plan.Document, result, new RecoveryWorkBudget(token));
        if (verified is null || plan.Targets.Count != verified.Count) throw new InvalidRecoveryOutputException();
        RecoveryField Apply(RecoveryField field, RecoveryFieldTarget target) => verified.TryGetValue(target, out var correction)
            ? field with { Value = correction.CorrectedText } : field;
        result = result with { Cells = result.Cells.Select(cell => cell with {
            Lessons = cell.Lessons.Select((lesson, i) => lesson with {
                Subject = Apply(lesson.Subject, new(cell.CellId, i, RecoveryFieldRole.Subject)),
                Teacher = Apply(lesson.Teacher, new(cell.CellId, i, RecoveryFieldRole.Teacher)),
                Room = Apply(lesson.Room, new(cell.CellId, i, RecoveryFieldRole.Room))
            }).ToArray()
        }).ToArray() };
        var validation = RecoveryValidator.Validate(plan.Document, result, token);
        if (!validation.CanAdopt) throw new InvalidRecoveryOutputException(new InvalidDataException(string.Join(",", validation.Errors)));
        token.ThrowIfCancellationRequested(); return result;
    }
}
