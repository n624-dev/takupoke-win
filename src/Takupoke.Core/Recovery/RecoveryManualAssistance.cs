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
    IReadOnlyList<string> OriginalParentIds, string OriginalOcr)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? OriginalLabelIds { get; init; }
}
public sealed record RecoveryHumanCorrection(RecoveryFieldTarget Target, string PdfHash, string AcquisitionHash, string DocumentSnapshot,
    IReadOnlyList<string> OriginalParentIds, int Page, RecoveryBox Crop, string CorrectedText,
    bool ExplicitBlank, DateTimeOffset EnteredAt, string Provenance = "user", int ManualSchemaVersion = 1)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? OriginalLabelIds { get; init; }
}
public sealed record RecoveryManualPlan(RecoveryDocument Document, string DocumentSnapshot, IReadOnlyList<RecoveryManualTarget> Targets)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RecoveryFieldTarget>? RequestedTargets { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? InitialValues { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecoveryResult? ReviewOriginalResult { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecoveryCaptureReceipt? ReviewOriginalCapture { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewToken { get; init; }
}

public static class RecoveryManualAssistance
{
    public const int MaximumFields = 3;
    public const int MaximumReviewFields = 128;
    public const int MaximumCropPixels = 262144;
    // Acquisition-only geometry request. This cannot authorize correction without original pixel receipts.
    public static IReadOnlyList<RecoveryManualTarget>? CaptureTargets(RecoveryDocument doc, CancellationToken token = default)
    {
        if (RecoveryValidator.InputErrors(doc, token).Count != 0) return null;
        try { return Targets(doc, new RecoveryWorkBudget(token), requireCrops: false); }
        catch (Exception error) when (error is RecoveryWorkLimitException or NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
    // Geometry request only. The capture provider must still verify original
    // pixels and complete ink coverage before Prepare can authorize a review.
    public static IReadOnlyList<RecoveryManualTarget>? CaptureTargets(RecoveryDocument doc,
        IReadOnlyList<RecoveryFieldTarget> selected, CancellationToken token = default)
    {
        if (RecoveryValidator.InputErrors(doc, token).Count != 0) return null;
        try { return Targets(doc, new RecoveryWorkBudget(token), requireCrops: false, selected: selected, requireInkCoverage: false); }
        catch (Exception error) when (error is RecoveryWorkLimitException or NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
    public static RecoveryManualPlan? Prepare(RecoveryDocument doc, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (RecoveryValidator.InputErrors(doc, token).Count != 0) return null;
        var work = new RecoveryWorkBudget(token);
        IReadOnlyList<RecoveryManualTarget>? targets;
        try { targets = Targets(doc, work); } catch (Exception error) when (error is RecoveryWorkLimitException or NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return null; }
        if (targets is not { Count: > 0 } || targets.Count > ReviewLimit(doc)) return null;
        return new(doc, RecoveryValidator.Fingerprint(doc), targets);
    }
    public static RecoveryManualPlan? Prepare(RecoveryDocument doc,
        IReadOnlyList<RecoveryFieldTarget> selected, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (doc.Capture?.ManualSchemaVersion != 2 || RecoveryValidator.InputErrors(doc, token).Count != 0) return null;
        try
        {
            var targets = Targets(doc, new RecoveryWorkBudget(token), selected: selected);
            return targets is { Count: > 0 } ? new(doc, RecoveryValidator.Fingerprint(doc), targets) { RequestedTargets = selected.ToArray() } : null;
        }
        catch (Exception error) when (error is RecoveryWorkLimitException or NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
    private static int ReviewLimit(RecoveryDocument doc) => doc.Capture?.ManualSchemaVersion == 2 ? MaximumReviewFields : MaximumFields;
    public static bool VerifyPlan(RecoveryManualPlan plan, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var verified = plan.RequestedTargets is { } selected ? Prepare(plan.Document, selected, token) : Prepare(plan.Document, token);
            if (verified is null || FingerprintAuthority(verified) != FingerprintAuthority(plan)) return false;
            if (plan.ReviewOriginalResult is not { } prior)
                return plan.ReviewOriginalCapture is null && plan.InitialValues is null && plan.ReviewToken is null;
            if (plan.RequestedTargets is null || plan.ReviewOriginalCapture is null ||
                !Guid.TryParseExact(plan.ReviewToken, "N", out var nonce) || nonce == Guid.Empty ||
                !RecoveryValidator.Validate(plan.Document with { Capture = plan.ReviewOriginalCapture }, prior, token).CanAdopt ||
                plan.InitialValues is null || plan.InitialValues.Count != plan.Targets.Count) return false;
            var cells = prior.Cells.ToDictionary(c => c.CellId);
            foreach (var target in plan.Targets)
            {
                token.ThrowIfCancellationRequested();
                var lesson = cells[target.Target.CellId].Lessons[target.Target.LessonIndex];
                var value = target.Target.Role switch { RecoveryFieldRole.Subject => lesson.Subject.Value,
                    RecoveryFieldRole.Teacher => lesson.Teacher.Value, _ => lesson.Room.Value };
                if (!plan.InitialValues.TryGetValue(target.Target.Key, out var initial) || initial != value) return false;
            }
            return true;
        }
        catch (Exception error) when (error is RecoveryWorkLimitException or NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return false; }
        static string FingerprintAuthority(RecoveryManualPlan p) => RecoveryValidator.Fingerprint(p with {
            InitialValues = null, ReviewOriginalResult = null, ReviewOriginalCapture = null, ReviewToken = null });
    }
    internal static IReadOnlyList<RecoveryManualTarget>? Targets(RecoveryDocument doc, RecoveryWorkBudget work, bool requireCrops = true,
        IReadOnlyList<RecoveryFieldTarget>? selected = null, bool requireInkCoverage = true)
    {
        work.Step();
        var capture = doc.Capture;
        if (!doc.Complete || doc.Cells.Count > 20000 || doc.Sources.Count > 100000 || capture is null ||
            capture.PdfHash != doc.PdfHash || capture.ManualSchemaVersion is not (1 or 2) || capture.PageCount is < 1 or > 12 || capture.Pages.Count != capture.PageCount) return null;
        var requested = selected?.ToHashSet() ?? [];
        if (selected is not null && (selected.Count is < 1 or > MaximumReviewFields || requested.Count != selected.Count || selected.Any(t => t is null))) return null;
        var reviewLimit = selected is null ? ReviewLimit(doc) : MaximumReviewFields;
        foreach (var source in doc.Sources) work.Step(1L + source.Text.Length);
        if (capture.SourceSnapshot != RecoveryValidator.Fingerprint(doc.Sources)) return null;
        var pages = new Dictionary<int, RecoveryCapturedPage>();
        foreach (var p in capture.Pages)
        {
            work.Step();
            if (p.Page < 1 || p.Page > capture.PageCount || p.Width is < 1 or > 4096 || p.Height is < 1 or > 4096 ||
                requireInkCoverage && !p.InkCoverageVerified || p.RasterHash.Length != 64 || p.RasterHash.Any(c => !char.IsAsciiHexDigit(c)) ||
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
        RecoverySourceIndex? scopedSources = null;
        foreach (var cell in doc.Cells)
        {
            work.Step(cell.LessonBindings.Count);
            if (cell.InputState != RecoveryInputState.Complete) return null;
            IReadOnlyList<RecoveryLessonBinding> bindings;
            if (cell.BindingMode == RecoveryBindingMode.Fixed) bindings = cell.LessonBindings;
            else if (capture.ManualSchemaVersion == 2 && cell.BindingMode == RecoveryBindingMode.RoleProposal)
            {
                // Only existing deterministic role/ownership proofs authorize
                // a review. No provider, user text or confidence edit assigns roles.
                scopedSources ??= new RecoverySourceIndex(doc, work);
                var recovered = RecoveryRules.Recover(doc, cell, scopedSources, work);
                if (recovered is null) return null;
                bindings = recovered.Lessons.Select(l => new RecoveryLessonBinding(
                    l.Subject.Evidence, l.Teacher.Evidence, l.Room.Evidence)).ToArray();
                work.Step(bindings.Count);
            }
            else return null;
            for (var lesson = 0; lesson < bindings.Count; lesson++)
            {
                var binding = bindings[lesson];
                foreach (var (role, ids) in new[] { (RecoveryFieldRole.Subject, binding.Subject), (RecoveryFieldRole.Teacher, binding.Teacher), (RecoveryFieldRole.Room, binding.Room) })
                {
                    work.Step(ids.Count * 5L);
                    var target = new RecoveryFieldTarget(cell.Id, lesson, role);
                    var labels = capture.ManualSchemaVersion == 2 && cell.BindingMode == RecoveryBindingMode.RoleProposal
                        ? cell.RoleScopes.Where(s => s.LessonIndex == lesson && s.Role == role && s.Proof == RecoveryRoleProof.InlineLabel)
                            .SelectMany(s => s.LabelSourceIds).ToArray() : [];
                    work.Step(labels.Length * 5L);
                    var reviewLabels = labels.Any(uncertain.Contains) ? labels : [];
                    if (!ids.Any(uncertain.Contains) && reviewLabels.Length == 0 && !requested.Contains(target)) continue;
                    if (ids.Count == 0 || ids.Concat(reviewLabels).Any(id => !sources.ContainsKey(id))) return null;
                    foreach (var id in ids.Concat(reviewLabels).Where(uncertain.Contains)) if (!assigned.Add(id)) return null;
                    var atoms = ids.Select(id => sources[id]).ToArray();
                    var cropAtoms = atoms.Concat(reviewLabels.Select(id => sources[id])).ToArray();
                    if (requested.Contains(target) && !atoms.Any(s => s.FromOcr)) return null;
                    if (cropAtoms.Any(s => s.CellId != cell.Id || s.Page != cell.Page || !cell.Box.Contains(s.Box))) return null;
                    var left = cropAtoms.Min(s => s.Box.X); var top = cropAtoms.Min(s => s.Box.Y);
                    var box = new RecoveryBox(left, top, cropAtoms.Max(s => s.Box.X + s.Box.Width) - left, cropAtoms.Max(s => s.Box.Y + s.Box.Height) - top);
                    targets.Add(new(target, cell.Page, box, ids.ToArray(), work.Concat(atoms.Select(s => s.Text))) {
                        OriginalLabelIds = reviewLabels.Length == 0 ? null : reviewLabels });
                    if (targets.Count > reviewLimit) return null;
                }
            }
        }
        work.Step(uncertain.Count);
        if (!assigned.SetEquals(uncertain)) return null;
        if (!requested.IsSubsetOf(targets.Select(t => t.Target))) return null;
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
        if (doc.Capture?.ManualSchemaVersion == 2 && result.Metadata.ValidatorVersion < 10) return null;
        work.Step(corrections.Count);
        if (corrections.Count < 1 || corrections.Count > ReviewLimit(doc)) return null;
        var selected = doc.Capture?.ManualSchemaVersion == 2 ? corrections.Select(c => c.Target).ToArray() : null;
        var targets = Targets(doc, work, selected: selected);
        if (targets is null || targets.Count != corrections.Count) return null;
        var snapshot = RecoveryValidator.Fingerprint(doc);
        var acquisitionHash = RecoveryValidator.Fingerprint(doc.Capture);
        var allowed = targets.ToDictionary(t => t.Target);
        var output = new Dictionary<RecoveryFieldTarget, RecoveryHumanCorrection>();
        foreach (var correction in corrections)
        {
            work.Step(correction.OriginalParentIds.Count + (correction.OriginalLabelIds?.Count ?? 0) + correction.CorrectedText.Length);
            if (!allowed.TryGetValue(correction.Target, out var target) || !output.TryAdd(correction.Target, correction) ||
                correction.PdfHash != doc.PdfHash || correction.DocumentSnapshot != snapshot || correction.Page != target.Page ||
                correction.AcquisitionHash != acquisitionHash || correction.ManualSchemaVersion != doc.Capture!.ManualSchemaVersion ||
                correction.Crop != target.Crop || !correction.OriginalParentIds.SequenceEqual(target.OriginalParentIds) ||
                (correction.OriginalLabelIds is null) != (target.OriginalLabelIds is null) ||
                !(correction.OriginalLabelIds ?? []).SequenceEqual(target.OriginalLabelIds ?? []) ||
                correction.EnteredAt == default || correction.Provenance != "user" || correction.CorrectedText.Length > 256) return null;
            // A low-confidence printed atom cannot become a proved empty field.
            // No new emptyVerified/white-pixel proof is created by user input.
            if (correction.ExplicitBlank || string.IsNullOrWhiteSpace(correction.CorrectedText)) return null;
        }
        // Schema 2 distinguishes checking uncertain OCR from actually editing it.
        // Every reviewed field retains its own source/crop/user acknowledgement;
        // only ordinal text changes consume the document-wide correction budget.
        if (doc.Capture!.ManualSchemaVersion == 2 && output.Values.Count(c =>
            !string.Equals(c.CorrectedText, allowed[c.Target].OriginalOcr, StringComparison.Ordinal)) > MaximumFields) return null;
        return output;
    }
    public static RecoveryResult Complete(RecoveryManualPlan plan, IReadOnlyList<RecoveryHumanCorrection> corrections,
        string osVersion, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!VerifyPlan(plan, token)) throw new InvalidRecoveryOutputException();
        if (plan.ReviewOriginalResult is { } prior)
        {
            if (plan.RequestedTargets is null || plan.ReviewOriginalCapture is null ||
                !RecoveryValidator.Validate(plan.Document with { Capture = plan.ReviewOriginalCapture }, prior, token).CanAdopt)
                throw new InvalidRecoveryOutputException();
        }
        else if (plan.ReviewOriginalCapture is not null || plan.InitialValues is not null || plan.ReviewToken is not null)
            throw new InvalidRecoveryOutputException();
        var expectedTargets = (plan.RequestedTargets is { } selected ? Prepare(plan.Document, selected, token) : Prepare(plan.Document, token))?.Targets;
        if (expectedTargets is null || RecoveryValidator.Fingerprint(expectedTargets) != RecoveryValidator.Fingerprint(plan.Targets))
            throw new InvalidRecoveryOutputException();
        var raw = RecoveryRules.RecoverAll(plan.Document, token).Select((cell, i) => cell ??
            (plan.Document.Cells[i].ConfirmedEmpty && plan.Document.Cells[i].SourceIds.Count == 0
                ? new RecoveredCell(plan.Document.Cells[i].Id, RecoveryValueState.Empty, []) : null)).ToArray();
        if (raw.Any(c => c is null) || plan.DocumentSnapshot != RecoveryValidator.Fingerprint(plan.Document))
            throw new InvalidRecoveryOutputException();
        var metadata = plan.ReviewOriginalResult?.Metadata ?? plan.Document.StructureMetadata ?? new RecoveryMetadata("rule", "rules", "3", "3", "1", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, osVersion);
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
