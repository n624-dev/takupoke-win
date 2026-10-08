using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Takupoke.Core.Recovery;

public static class RecoveryValidator
{
    public const int SchemaVersion = 2;
    public const int Version = 10;
    public static string Fingerprint<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static string Text(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", "").Replace('~', '〜').Replace('～', '〜');
    public static IReadOnlyList<string> SpecialClasses { get; } = new[] { "1_1", "1_2", "1_3" }.Concat(Enumerable.Range(2, 4).SelectMany(y => new[] { "CN", "ES", "IT" }.Select(c => $"{y}_{c}"))).Concat(new[] { "AI_1", "AI_2" }).ToArray();
    private static IReadOnlyList<string> DayLabels(string day, RecoveryDocumentKind kind)
    {
        if (kind == RecoveryDocumentKind.Timetable) { var label = day switch { "1" => "月", "2" => "火", "3" => "水", "4" => "木", "5" => "金", _ => "?" }; return [label, label + "曜", label + "曜日"]; }
        if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return [];
        return [day, $"{d.Year}/{d.Month}/{d.Day}", $"{d.Month}/{d.Day}", $"{d.Month}月{d.Day}日"];
    }
    public static RecoveryValidation Validate(RecoveryDocument doc, RecoveryResult result, CancellationToken token = default)
    {
        try { return ValidateCore(doc, result, token: token); }
        catch (RecoveryWorkLimitException) { return new(["validationLimit"]); }
        catch (Exception e) when (e is NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return new(["malformedInput"]); }
    }
    // A source policy shared with Builder; callers supply original BODY sources.
    public static bool OcrBodySeparatorAmbiguous(RecoverySource source) => source.FromOcr && source.Text.Contains('·');
    private sealed record HistoricalAuditContext(int OriginVersion);

    public static RecoveryValidation ValidateHistoricalManual(RecoveryDocument doc, RecoveryResult result,
        RecoveryAcceptance acceptance, RecoveryAcceptance? previous, RecoverySemanticCertification certificate, CancellationToken token = default)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            return result.Metadata.ValidatorVersion == 7 && result.HumanCorrections is { Count: > 0 }
                ? ValidateCertifiedAudit(doc, result, acceptance, previous, certificate, token: token)
                : new(["historicalCertification"]);
        }
        catch (RecoveryWorkLimitException) { return new(["validationLimit"]); }
        catch (Exception e) when (e is NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return new(["malformedInput"]); }
    }

    // Size-only query; this cannot enable historical semantic validation.
    public static bool AuditShapeWithinLimit(RecoveryDocument doc, RecoveryResult result,
        RecoveryAcceptance acceptance, RecoveryAcceptance? previous,
        RecoverySemanticCertification? certificate = null, RecoverySemanticCertification? predecessor = null,
        CancellationToken token = default, IReadOnlyList<RecoverySemanticCertification>? history = null)
    {
        try { RecoveryAuditShape.Check(doc, result, acceptance, previous, certificate, predecessor, token, history); return true; }
        catch (RecoveryWorkLimitException) { return false; }
        catch (Exception e) when (e is NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return false; }
    }

    public static RecoveryValidation ValidateCertifiedAudit(RecoveryDocument doc, RecoveryResult result,
        RecoveryAcceptance acceptance, RecoveryAcceptance? previous, RecoverySemanticCertification certificate,
        RecoverySemanticCertification? predecessor = null, CancellationToken token = default, IReadOnlyList<RecoverySemanticCertification>? history = null)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            RecoveryAuditShape.Check(doc, result, acceptance, previous, certificate, predecessor, token, history);
            var origin = result.Metadata.ValidatorVersion;
            // Individual legacy shape caps and the complete envelope precede
            // every certification fingerprint as well as semantic context.
            if (!HistoricalAcceptanceEnvelopeValid(doc, result, acceptance, previous, token)) return new(["historicalCertification"]);
            if (Version != 10 || origin is not (4 or 5 or 6 or 7 or 8 or 9) ||
                certificate.RecoverySchemaVersion != SchemaVersion || certificate.ValidatorVersion != Version ||
                result.Metadata.RecoverySchemaVersion != SchemaVersion || doc.StructureMetadata is { } structure && structure.ValidatorVersion != origin ||
                acceptance.AcceptedAt == default || acceptance.Metadata != result.Metadata ||
                acceptance.PdfHash != doc.PdfHash || result.PdfHash != doc.PdfHash ||
                certificate.ScopeHash != acceptance.ScopeHash || certificate.ResultHash != acceptance.ResultHash ||
                certificate.ScopeHash != Fingerprint(doc) || certificate.ResultHash != Fingerprint(result) ||
                certificate.AcceptanceHash != Fingerprint(acceptance) || certificate.PreviousAcceptanceHash != Fingerprint(previous) ||
                certificate.PreviousCertificationHash != Fingerprint(predecessor)) return new(["historicalCertification"]);
            bool SameEnvelope(RecoverySemanticCertification c) => c.RecoverySchemaVersion==SchemaVersion &&
                c.ScopeHash==certificate.ScopeHash && c.ResultHash==certificate.ResultHash && c.AcceptanceHash==certificate.AcceptanceHash &&
                c.PreviousAcceptanceHash==Fingerprint(previous);
            bool Manual8(RecoverySemanticCertification c) => origin==7 && result.HumanCorrections is {Count:>0} && previous is null &&
                c.ValidatorVersion==8 && c.PreviousCertificationHash is null && SameEnvelope(c);
            var ancestryValid=predecessor is null ? history is null : predecessor.ValidatorVersion switch
            {
                8 => history is null && Manual8(predecessor),
                9 => origin is >=4 and <=8 && SameEnvelope(predecessor) &&
                    (history is null ? predecessor.PreviousCertificationHash==Fingerprint(null as RecoverySemanticCertification) :
                     history.Count==1 && Manual8(history[0]) && predecessor.PreviousCertificationHash==Fingerprint(history[0])),
                _ => false
            };
            if(!ancestryValid)return new(["historicalCertification"]);
            if (origin < 7 && (doc.Capture is not null || result.HumanCorrections is not null || doc.Sources.Any(s => s.NativeConfidence is not null)) ||
                origin == 7 && result.HumanCorrections is { Count: > 0 } && previous is not null)
                return new(["historicalCertification"]);
            token.ThrowIfCancellationRequested();
            // The context is derived only after the exact audit envelope binds it.
            return ValidateCore(doc, result, token: token, historical: new(origin));
        }
        catch (RecoveryWorkLimitException) { return new(["validationLimit"]); }
        catch (Exception e) when (e is NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return new(["malformedInput"]); }
    }
    // A complete, bounded historical receipt query. No caller-supplied origin
    // or compatibility flag can enable historical metadata in ordinary paths.
    public static bool HistoricalAcceptanceEnvelopeValid(RecoveryDocument doc, RecoveryResult result,
        RecoveryAcceptance acceptance, RecoveryAcceptance? previous, CancellationToken token = default)
    {
        try
        {
            RecoveryAuditShape.Check(doc, result, acceptance, previous, null, null, token);
            return HistoricalAcceptanceEnvelopeCore(doc, result, acceptance, previous, token);
        }
        catch (RecoveryWorkLimitException) { return false; }
        catch (Exception e) when (e is NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return false; }
    }
    private static bool HistoricalAcceptanceEnvelopeCore(RecoveryDocument doc, RecoveryResult result,
        RecoveryAcceptance acceptance, RecoveryAcceptance? previous, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!HistoricalEnvelope(doc, result, acceptance, token)) return false;
        if (previous is null) return true;
        var metadata = result.Metadata; var structure = doc.StructureMetadata;
        var version = previous.Metadata.ValidatorVersion;
        if (!(metadata.ValidatorVersion == 5 && version == 4 || metadata.ValidatorVersion == 6 && version is 4 or 5 ||
            metadata.ValidatorVersion == 7 && version is 4 or 5 or 6 || metadata.ValidatorVersion == 8 && version is 4 or 5 or 6 or 7 || metadata.ValidatorVersion == 9 && version is 4 or 5 or 6 or 7 or 8) ||
            previous.AcceptedAt != acceptance.AcceptedAt || result.HumanCorrections is not null) return false;
        var oldDocument = doc with { StructureMetadata = structure is null ? null : structure with { ValidatorVersion = version } };
        var oldResult = result with { Metadata = metadata with { ValidatorVersion = version } };
        if (!HistoricalEnvelope(oldDocument, oldResult, previous, token)) return false;
        var promotedDocument = oldDocument with { StructureMetadata = oldDocument.StructureMetadata is { } prior ? prior with { ValidatorVersion = metadata.ValidatorVersion } : null };
        var promotedResult = oldResult with { Metadata = oldResult.Metadata with { ValidatorVersion = metadata.ValidatorVersion } };
        return Fingerprint(promotedDocument) == acceptance.ScopeHash && Fingerprint(promotedResult) == acceptance.ResultHash;
    }
    private static bool HistoricalEnvelope(RecoveryDocument doc, RecoveryResult result, RecoveryAcceptance acceptance, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var metadata = result.Metadata; var structure = doc.StructureMetadata;
        if (doc.Cells.Count > 20000 || doc.Sources.Count > 100000 || result.Cells.Count > 20000 ||
            doc.Cells.Any(c => c.SourceIds.Count > 100000 || c.RoleScopes.Count > 12 || c.LessonBindings.Count > 4 || c.Slots.Count > 8 || c.ParallelSeparators is { Count: > 3 }) ||
            result.Cells.Any(c => c.Lessons.Count > 4)) return false;
        if (doc.Cells.Any(c=>c.OrderedRowProof is not null) || Version != 10 || metadata.ValidatorVersion is not (4 or 5 or 6 or 7 or 8 or 9) ||
            metadata.RecoverySchemaVersion != SchemaVersion || metadata.RecoveryVersion != "2" || acceptance.AcceptedAt == default ||
            acceptance.PdfHash != doc.PdfHash || acceptance.PdfHash != result.PdfHash || acceptance.Metadata != metadata ||
            structure is not null && (structure.ValidatorVersion != metadata.ValidatorVersion || metadata.ValidatorVersion == 4 && structure != metadata)) return false;
        foreach (var source in doc.Sources)
        {
            token.ThrowIfCancellationRequested();
            if (source.FontEvidence is not null || source.Text.Length > 4096 || metadata.ValidatorVersion < 7 && source.NativeConfidence is not null) return false;
        }
        if (metadata.ValidatorVersion < 7 && (doc.Capture is not null || result.HumanCorrections is not null)) return false;
        if (metadata.ValidatorVersion < 8)
        {
            var gradeIds = doc.Sources.Where(s => s.Text.Contains('年')).Select(s => s.Id).ToHashSet();
            foreach (var cell in doc.Cells)
            {
                token.ThrowIfCancellationRequested();
                if (cell.Slots.Any(s => s.ClassName is "AI_1" or "AI_2") && cell.ClassHeaderIds.Any(gradeIds.Contains)) return false;
            }
        }
        foreach (var cell in doc.Cells) { token.ThrowIfCancellationRequested(); if (metadata.ValidatorVersion < 6 && cell.ParallelSeparators is not null) return false; }
        if (acceptance.ResultHash != Fingerprint(result)) return false;
        token.ThrowIfCancellationRequested();
        return acceptance.ScopeHash == Fingerprint(doc);
    }
    private static RecoveryValidation ValidateCore(RecoveryDocument doc, RecoveryResult result, bool structurePreflight = false, CancellationToken token = default, HistoricalAuditContext? historical = null)
    {
        var work = new RecoveryWorkBudget(token); work.Step();
        bool Contains(RecoveryBox outer, RecoveryBox inner) { work.Step(); return outer.Contains(inner); }
        var errors = new List<string>();
        if (doc.SchoolYear is < 1900 or > 9998 || doc.Classes.Count is < 1 or > 64 || doc.Days.Count is < 1 or > 31 || doc.Cells.Count is < 1 or > 20000 || doc.Sources.Count > 100000 || result.Cells.Count > 20000 || doc.Cells.Any(c => c.SourceIds.Count > 100000 || c.RoleScopes.Count > 12 || c.LessonBindings.Count > 4 || c.Slots.Count > 8 || c.ParallelSeparators is { Count: > 3 }) || result.Cells.Any(c => c.Lessons.Count > 4)) return new(["inputLimit"]);
        void Check(bool condition, string code) { work.Step(); if (!condition && !errors.Contains(code)) errors.Add(code); }
        Check(Enum.IsDefined(doc.Kind), "documentKind");
        Check(Regex.IsMatch(doc.PdfHash, "^[a-f0-9]{64}$") && result.PdfHash == doc.PdfHash, "sourceHash");
        Check(doc.Complete && doc.Cells.Count is > 0 and <= 20000 && doc.Sources.Count <= 100000, "incompleteDocument");
        Check(result.Kind == doc.Kind && result.SchoolYear == doc.SchoolYear && result.Term == doc.Term, "documentIdentity");
        Check(doc.SchoolYear is >= 1900 and <= 9998 && (doc.Kind != RecoveryDocumentKind.Timetable || doc.Term is "前期" or "後期"), "yearTerm");
        bool ValidMetadata(RecoveryMetadata metadata) => metadata.RecoverySchemaVersion == SchemaVersion && (metadata.ValidatorVersion == (historical?.OriginVersion ?? Version)) &&
            new[] { metadata.Provider, metadata.ModelId, metadata.ModelVersion, metadata.RuntimeVersion,
                metadata.PromptVersion, metadata.OsVersion, metadata.RecoveryVersion }.All(v => !string.IsNullOrWhiteSpace(v));
        Check(ValidMetadata(result.Metadata), "versions");
        // Structural and field inference have independently recorded histories.
        Check(doc.StructureMetadata is null || ValidMetadata(doc.StructureMetadata), "structureMetadata");
        Check(doc.Classes.Count > 0 && doc.Classes.Distinct().Count() == doc.Classes.Count && doc.Classes.All(ClassSelection.Candidates.Contains) && doc.Days.Count > 0 && doc.Days.Distinct().Count() == doc.Days.Count, "scope");
        if (doc.Kind != RecoveryDocumentKind.Timetable) Check(SpecialClasses.ToHashSet().SetEquals(doc.Classes) && doc.Days.Count == 5, "specialScope");
        var maxPeriod = doc.Kind == RecoveryDocumentKind.Exam ? 6 : 8;
        if (doc.Kind == RecoveryDocumentKind.Timetable) Check(doc.Days.Order().SequenceEqual(new[] { "1", "2", "3", "4", "5" }), "weekdays");
        else foreach (var day in doc.Days) Check(DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date >= new DateOnly(doc.SchoolYear, 4, 1) && date < new DateOnly(doc.SchoolYear + 1, 4, 1), "dates");
        var required = (from cls in doc.Classes from day in doc.Days from p in Enumerable.Range(1, maxPeriod) select new RecoverySlot(cls, day, p)).ToHashSet();
        Check(doc.RequiredSlots.Count == required.Count && required.SetEquals(doc.RequiredSlots), "requiredScope");
        var slots = doc.Cells.SelectMany(c => c.Slots).ToArray();
        Check(slots.Length == required.Count && required.SetEquals(slots), "coverage");
        Check(doc.Cells.Select(c => c.Id).Distinct().Count() == doc.Cells.Count && doc.Sources.Select(s => s.Id).Distinct().Count() == doc.Sources.Count, "duplicateIds");
        // Use TryAdd: malformed provider/input IDs must produce a rejection, not an exception.
        if (doc.Sources.Any(s => { work.Step(); return s.Page < 1 || !s.Box.Valid || s.Text.Length > 4096; })) return new(["sourceLimit"]);
        var index = new RecoverySourceIndex(doc, work, spatial: true);
        var sources = index.ById;
        foreach (var cell in doc.Cells)
        foreach (var id in cell.SourceIds)
        {
            work.Step();
            if (!sources.TryGetValue(id, out var body)) continue; // Ownership errors are checked below.
            work.Step(body.Text.Length);
            if (OcrBodySeparatorAmbiguous(body)) return new(["ocrSeparatorAmbiguity"]);
        }
        var manual = RecoveryManualAssistance.Corrections(doc, result, work);
        Check(result.HumanCorrections is null || manual is not null, "humanProof");
        foreach (var source in doc.Sources)
        {
            work.Step();
            if (source.FontEvidence is { } font)
            {
                work.Step(1L + (font.Resource?.Length ?? 0) + (font.FontHash?.Length ?? 0) + (font.CidMapHash?.Length ?? 0) + source.Text.Length);
                static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
                Check(!source.FromOcr && source.NativeConfidence is null && font.ReaderVersion == 1
                    && font.Resource is { Length: > 0 and <= 128 } && !font.Resource.Any(char.IsControl)
                    && Hash(font.FontHash) && (font.CidMapHash is "identity" or "identity-default" || Hash(font.CidMapHash))
                    && font.Code is >= 0 and <= 65535 && font.Cid == font.Code && font.GlyphId is > 0 and < 65535
                    && Rune.IsValid(font.Scalar) && !Rune.IsControl(new Rune(font.Scalar)) && source.Text == new Rune(font.Scalar).ToString(), "fontEvidence");
            }
            Check(source.NativeConfidence is null || source.FromOcr && double.IsFinite(source.NativeConfidence.Value) && source.NativeConfidence.Value is >= 0 and <= 1, "nativeConfidence");
            if (source.NativeConfidence is < .8) Check(manual is not null, "humanUnresolved");
        }
        string Raw(IEnumerable<string> ids) => work.Concat(ids.Select(id => sources[id].Text));
        var cells = new Dictionary<string, RecoveredCell>(); foreach (var c in result.Cells) Check(cells.TryAdd(c.CellId, c), "duplicateCells");
        Check(cells.Count == doc.Cells.Count && doc.Cells.All(c => cells.ContainsKey(c.Id)), "resultCoverage");
        var allowedSets = new Dictionary<IReadOnlyList<string>, HashSet<string>>(ReferenceEqualityComparer.Instance);
        HashSet<string> Allowed(IReadOnlyList<string> ids)
        {
            if (!allowedSets.TryGetValue(ids, out var set)) { work.Step(ids.Count); allowedSets.Add(ids, set = ids.ToHashSet()); }
            return set;
        }
        bool AllowedContains(IReadOnlyList<string> ids, string id) { work.Step(); return Allowed(ids).Contains(id); }
        bool Evidence(IReadOnlyList<string> ids, IReadOnlyList<string> allowed, string? value = null)
        {
            work.Step(ids.Count);
            var set = Allowed(allowed);
            if (ids.Count == 0 || ids.Distinct().Count() != ids.Count || !ids.All(id => set.Contains(id) && sources.ContainsKey(id))) return false;
            if (value is null) return true;
            work.Step(value.Length);
            var original = Text(Raw(ids)); var expected = Text(value);
            return expected.Length > 0 && original == expected || Regex.IsMatch(expected, @"^\d{2}:\d{2}〜\d{2}:\d{2}$") && Regex.Replace(original, @"(?<!\d)(\d):", "0$1:") == expected;
        }
        var order = index.Order;
        bool Ordered(IReadOnlyList<string> ids)
        { work.Step(ids.Count); return ids.All(order.ContainsKey) && ids.Select(id => order[id]).Zip(ids.Skip(1).Select(id => order[id])).All(p => p.First < p.Second); }
        bool Header(IReadOnlyList<string> ids, IReadOnlyList<string> allowed, IReadOnlyList<string> labels, RecoveryCell? cell = null, RecoveryHeaderRegion? region = null)
        {
            if (!Evidence(ids, allowed) || !Ordered(ids)) return false;
            var joinedMatches = labels.Select(Text).Contains(Text(Raw(ids)));
            if (cell is null) return joinedMatches || Regex.IsMatch(Text(Raw(ids)), "^(?:" + string.Join("|", labels.Select(l => Regex.Escape(Text(l)))) + ")+$");
            if (region is null || region.Page != cell.Page || !region.Box.Valid || !Enum.IsDefined(region.Axis)) return false;
            var aligned = region.Axis == RecoveryHeaderAxis.Above ? region.Box.Y + region.Box.Height <= cell.Box.Y && Math.Min(region.Box.X + region.Box.Width, cell.Box.X + cell.Box.Width) > Math.Max(region.Box.X, cell.Box.X) : region.Box.X + region.Box.Width <= cell.Box.X && Math.Min(region.Box.Y + region.Box.Height, cell.Box.Y + cell.Box.Height) > Math.Max(region.Box.Y, cell.Box.Y);
            return joinedMatches && aligned && ids.All(id => { var src = sources[id]; return src.Page == region.Page && Contains(region.Box, src.Box); });
        }
        IReadOnlyList<string> ClassLabels(string cls) => [cls, cls.Replace('_', '-'), cls.Replace("_", ""), string.Concat(cls.Split('_').Reverse())];
        var aiOwnerCache = new Dictionary<(string, RecoveryHeaderRegion), bool>();
        bool AiOwner(string cls, RecoveryHeaderRegion region)
        {
            work.Step();
            if (aiOwnerCache.TryGetValue((cls, region), out var cached)) return cached;
            work.Step(doc.Cells.Count);
            var valid = doc.Kind == RecoveryDocumentKind.Exam && region.Axis == RecoveryHeaderAxis.Above;
            var ownerCells = doc.Cells.Where(c => c.Page == region.Page && c.ClassRegion == region && c.Slots.Count > 0 && c.Slots.All(s => s.ClassName == cls)).ToArray();
            var band = new Dictionary<int, RecoveryHeaderRegion>();
            foreach (var owner in ownerCells)
            foreach (var slot in owner.Slots)
            {
                work.Step(owner.PeriodHeaderIds.Count + 1);
                var key = slot.Period.ToString(CultureInfo.InvariantCulture);
                var allowed = doc.PeriodEvidence.GetValueOrDefault(key) ?? [];
                var ids = owner.PeriodHeaderIds.Where(id => AllowedContains(allowed, id)).ToArray();
                if (!owner.PeriodRegions.TryGetValue(key, out var period) || period.Axis != RecoveryHeaderAxis.Above ||
                    period.Box.Y != region.Box.Y + region.Box.Height || period.Box.X < region.Box.X ||
                    period.Box.X + period.Box.Width > region.Box.X + region.Box.Width ||
                    !Header(ids, allowed, [key, key + "限", key + "時限", key + "時限目", "第" + key + "時限"], owner, period))
                { valid = false; continue; }
                if (band.TryGetValue(slot.Period, out var existing) && existing != period) valid = false;
                band[slot.Period] = period;
            }
            var boxes = band.Values.Select(r => r.Box).Distinct().OrderBy(b => b.X).ToArray();
            valid &= band.Keys.Order().SequenceEqual(Enumerable.Range(1, 6)) && boxes.Length > 0 &&
                boxes[0].X == region.Box.X && boxes[^1].X + boxes[^1].Width == region.Box.X + region.Box.Width &&
                !boxes.Zip(boxes.Skip(1)).Any(pair => pair.First.X + pair.First.Width != pair.Second.X);
            return aiOwnerCache[(cls, region)] = valid;
        }
        bool ClassHeader(IReadOnlyList<string> ids, IReadOnlyList<string> allowed, string cls, RecoveryCell? cell = null)
        {
            if (!Evidence(ids, allowed) || !Ordered(ids)) return false;
            if (Header(ids, allowed, ClassLabels(cls), cell, cell?.ClassRegion)) return true;
            if (historical is { OriginVersion: < 8 } || doc.Kind != RecoveryDocumentKind.Exam || cls is not ("AI_1" or "AI_2")) return false;
            if (cell is null)
            {
                work.Step(doc.Cells.Count + ids.Count);
                var owners = doc.Cells.Where(c => c.Slots.FirstOrDefault()?.ClassName == cls).DistinctBy(c => string.Join(',', c.ClassHeaderIds)).ToArray();
                return owners.Length > 0 && ids.ToHashSet().SetEquals(owners.SelectMany(c => c.ClassHeaderIds)) &&
                    owners.All(c => ClassHeader(c.ClassHeaderIds, allowed, cls, c));
            }
            // Both complete literals, in original reading order, must belong to
            // one physical class rail above a complete adjacent six-period band.
            var grade = cls[^1] + "年";
            // Match the two canonical spellings recognized by the builder.
            var canonical = new[] { cls, cls.Replace('_', '-') };
            return cell.ClassRegion is { Axis: RecoveryHeaderAxis.Above } region &&
                Header(ids, allowed, canonical.SelectMany(label => new[] { grade + label, label + grade }).ToArray(), cell, region) && AiOwner(cls, region);
        }
        Check(doc.Sources.All(s => s.Page > 0 && s.Box.Valid && s.Text.Length <= 4096), "sourceLimit");
        Check(Evidence(doc.YearEvidence, doc.YearEvidence), "yearEvidence");
        var yearText = Raw(doc.YearEvidence.Where(sources.ContainsKey));
        Check(Regex.IsMatch(Text(yearText), "^(?:" + doc.SchoolYear + "年度|令和" + (doc.SchoolYear - 2018) + "年度)+$"), "yearEvidenceText");
        if (doc.Term is not null) Check(Header(doc.TermEvidence, doc.TermEvidence, [doc.Term]), "termEvidence");
        foreach (var cls in doc.Classes) Check(doc.ClassEvidence.TryGetValue(cls, out var ids) && ClassHeader(ids, ids, cls), "classEvidence");
        foreach (var day in doc.Days) Check(doc.DayEvidence.TryGetValue(day, out var ids) && Header(ids, ids, DayLabels(day, doc.Kind)), "dayEvidence");
        foreach (var period in Enumerable.Range(1, maxPeriod)) Check(doc.PeriodEvidence.TryGetValue(period.ToString(CultureInfo.InvariantCulture), out var ids) && Header(ids, ids, [period.ToString(), $"{period}限", $"{period}時限", $"{period}時限目", $"第{period}時限"]), "periodEvidence");
        var clockCache = new Dictionary<(string, int, int, string), bool>();
        bool ClockBound(string day, int start, int end, string clock)
        {
            work.Step(); var key = (day, start, end, clock);
            if (!clockCache.TryGetValue(key, out var valid)) clockCache[key] = valid = ClockBoundCore(day, start, end, clock);
            return valid;
        }
        bool ClockBoundCore(string day, int start, int end, string clock)
        {
            var suffix = start == end ? start.ToString() : $"{start}-{end}"; var key = $"{day}:{suffix}"; var ids = doc.ClockEvidence.GetValueOrDefault(key) ?? [];
            if (!doc.ClockBindings.TryGetValue(key, out var primary)) return false;
            var replicas = doc.ClockReplicas.GetValueOrDefault(key) ?? []; work.Step(replicas.Count);
            var allBindings = new[] { primary }.Concat(replicas).ToArray();
            if (allBindings.Select(b => b.Page).Distinct().Count() != allBindings.Length || !ids.All(id => sources.TryGetValue(id, out var src) && allBindings.Count(b => { work.Step(); return b.Page == src.Page && Contains(b.Box, src.Box); }) == 1)) return false;
            return allBindings.All(binding => Bound(binding));
            bool Bound(RecoveryClockBinding binding)
            {
            var pageIds = ids.Where(id => sources.TryGetValue(id, out var src) && src.Page == binding.Page).ToArray();
            if (binding.Day != day || binding.SpanStart != start || binding.SpanEnd != end || binding.Page < 1 || !binding.Box.Valid || !Evidence(pageIds, doc.TimeEvidence, clock) || !pageIds.All(id => sources[id].Page == binding.Page && Contains(binding.Box, sources[id].Box))) return false;
            var parts = clock.Split('〜'); if (parts.Length != 2 || string.CompareOrdinal(parts[0], parts[1]) >= 0) return false;
            var virtualCell = new RecoveryCell("clock", binding.Page, binding.Box, RecoveryInputState.Complete, [], pageIds, []);
            string[] labels = start == end ? [start.ToString(), $"{start}限", $"{start}時限", $"{start}時限目", $"第{start}時限"] : [$"{start}・{end}時限連続", $"{start}〜{end}時限連続", $"{start}〜{end}限", $"{start}-{end}限"];
            var allowed = start == end ? doc.PeriodEvidence.GetValueOrDefault(start.ToString()) ?? [] : binding.PeriodHeaderIds;
            if (!Header(binding.PeriodHeaderIds, allowed, labels, virtualCell, binding.PeriodRegion)) return false;

            if (!binding.CommonScope) return Header(binding.DayHeaderIds, doc.DayEvidence.GetValueOrDefault(day) ?? [], DayLabels(day, doc.Kind), virtualCell, binding.DayRegion);
            if (binding.DayHeaderIds.Count != 0 || binding.DayRegion is not null || !doc.CommonClockRegions.TryGetValue(binding.Page.ToString(CultureInfo.InvariantCulture), out var commonRegion) || binding.Page != commonRegion.Page || commonRegion.Axis != RecoveryHeaderAxis.Above) return false;
            var common = doc.CommonClockEvidence.Where(id => sources.TryGetValue(id, out var src) && src.Page == binding.Page).ToArray();
            var firstDate = doc.Days.Order().FirstOrDefault();
            string[] commonLabels = doc.Kind == RecoveryDocumentKind.Exam ? ["試験時間割", "定期試験時間割"] : DateOnly.TryParse(firstDate, out var first) ? [$"{first.Month}月{first.Day}日の時間割は以下のとおりです。", $"{first.Month}月{first.Day}日の時間割は以下のとおり"] : [];
            if (doc.Kind == RecoveryDocumentKind.Timetable || doc.Kind == RecoveryDocumentKind.Return && day != firstDate || !Header(common, doc.CommonClockEvidence, commonLabels, virtualCell, commonRegion)) return false;
            // The common chart must be printed on a page containing every date.
            return doc.Cells.Where(c => c.Page == binding.Page).SelectMany(c => c.Slots).Select(s => s.Day).ToHashSet().SetEquals(doc.Days);
            }
        }
        work.Step(doc.ClockBindings.Count + doc.ClockReplicas.Count);
        foreach (var replicas in doc.ClockReplicas.Values) work.Step(replicas.Count);
        var allClocks = doc.ClockBindings.Values.Concat(doc.ClockReplicas.Values.SelectMany(b => b)).ToArray();
        var commonPages = allClocks.Where(b => b.CommonScope).Select(b => b.Page.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        if (commonPages.Count > 0)
        {
            Check(doc.CommonClockRegions.Keys.All(commonPages.Contains) && doc.CommonClockRegions.Count > 0 && Evidence(doc.CommonClockEvidence, doc.CommonClockEvidence) && doc.CommonClockEvidence.All(id => sources.TryGetValue(id, out var src) && doc.CommonClockRegions.TryGetValue(src.Page.ToString(CultureInfo.InvariantCulture), out var r) && Contains(r.Box, src.Box) && src.Page == r.Page), "commonClockProof");
            foreach (var period in Enumerable.Range(1, maxPeriod))
            {
                var clocks = doc.ClockBindings.Where(p => p.Value.CommonScope && p.Value.SpanStart == period && p.Value.SpanEnd == period).ToArray();
                Check(clocks.Length > 0 && clocks.Select(p => doc.Times.GetValueOrDefault(p.Key)).Distinct().Count() == 1, "commonClockConsistency");
            }
        }
        var validClocks = doc.ClockBindings.Where(pair => { var binding = pair.Value; var clock = (binding.SpanStart == binding.SpanEnd ? doc.Times : doc.SpanTimes).GetValueOrDefault(pair.Key); return clock is not null && ClockBound(binding.Day, binding.SpanStart, binding.SpanEnd, clock); }).SelectMany(pair => new[] { pair.Value }.Concat(doc.ClockReplicas.GetValueOrDefault(pair.Key) ?? [])).ToArray();
        foreach (var cls in doc.Classes) Check((doc.ClassEvidence.GetValueOrDefault(cls) ?? []).ToHashSet().SetEquals(doc.Cells.Where(c => c.Slots.FirstOrDefault()?.ClassName == cls).SelectMany(c => c.ClassHeaderIds)), "classHeaderCoverage");
        foreach (var day in doc.Days) Check((doc.DayEvidence.GetValueOrDefault(day) ?? []).ToHashSet().SetEquals(doc.Cells.Where(c => c.Slots.FirstOrDefault()?.Day == day).SelectMany(c => c.DayHeaderIds).Concat(validClocks.Where(b => b.Day == day).SelectMany(b => b.DayHeaderIds))), "dayHeaderCoverage");
        foreach (var period in Enumerable.Range(1, maxPeriod))
        {
            var allowed = doc.PeriodEvidence.GetValueOrDefault(period.ToString(CultureInfo.InvariantCulture)) ?? [];
            var bound = doc.Cells.Where(c => c.Slots.Any(s => s.Period == period)).SelectMany(c => c.PeriodHeaderIds).Where(id => AllowedContains(allowed, id)).Concat(validClocks.Where(b => b.SpanStart == period && b.SpanEnd == period).SelectMany(b => b.PeriodHeaderIds));
            Check(allowed.ToHashSet().SetEquals(bound), "periodHeaderCoverage");
        }
        foreach (var page in doc.Cells.GroupBy(c => c.Page))
        {
            var active = new List<RecoveryCell>();
            foreach (var cell in page.OrderBy(c => c.Box.X))
            {
                work.Step(active.Count * 2L); active.RemoveAll(c => c.Box.X + c.Box.Width <= cell.Box.X);
                Check(!active.Any(c => Math.Min(c.Box.Y + c.Box.Height, cell.Box.Y + cell.Box.Height) > Math.Max(c.Box.Y, cell.Box.Y)), "cellOverlap");
                active.Add(cell);
            }
        }
        var dates = doc.Days.Order().Select(day => DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : default).ToArray();
        var noteGroups = doc.NormalTimeNoteEvidence.Where(sources.ContainsKey).GroupBy(id => sources[id].Page).ToArray();
        var expectedNote = dates.Length == 5 && dates.All(d => d != default) && dates[1].Month == dates[4].Month ? $"{dates[0].Month}月{dates[0].Day}日の時間割は以下のとおり{dates[1].Month}月{dates[1].Day}日〜{dates[4].Day}日は通常の授業日どおりの授業時間" : "";
        var noteValid = doc.Kind == RecoveryDocumentKind.Return && expectedNote.Length > 0 && Evidence(doc.NormalTimeNoteEvidence, doc.NormalTimeNoteEvidence) && noteGroups.Length > 0
            && noteGroups.All(group => Ordered(group.ToArray()) && Text(Raw(group)).Replace("。", "").Replace("です", "") == expectedNote);

        var titleLabels = doc.Kind switch { RecoveryDocumentKind.Timetable => new[] { "時間割", "通常時間割", "授業時間割" }, RecoveryDocumentKind.Exam => ["試験時間割", "定期試験時間割"], _ => ["試験返却時間割"] };
        Check(doc.DocumentTitleEvidence.Count == 0 || Header(doc.DocumentTitleEvidence, doc.DocumentTitleEvidence, titleLabels), "documentTitle");
        Check(commonPages.Count > 0 || doc.CommonClockEvidence.Count == 0 && doc.CommonClockRegions.Count == 0, "unusedCommonClockProof");
        var classified = doc.Cells.SelectMany(c => c.SourceIds).Concat(doc.CommonClockEvidence).Concat(doc.YearEvidence).Concat(doc.DocumentTitleEvidence).Concat(doc.Term is null ? [] : doc.TermEvidence).ToHashSet();
        foreach (var cls in doc.Classes) classified.UnionWith(doc.ClassEvidence.GetValueOrDefault(cls) ?? []);
        foreach (var day in doc.Days) classified.UnionWith(doc.DayEvidence.GetValueOrDefault(day) ?? []);
        foreach (var period in Enumerable.Range(1, maxPeriod)) classified.UnionWith(doc.PeriodEvidence.GetValueOrDefault(period.ToString(CultureInfo.InvariantCulture)) ?? []);
        if (doc.Kind != RecoveryDocumentKind.Timetable)
        {
            foreach (var key in doc.Times.Keys.Concat(doc.SpanTimes.Keys).Distinct())
            {
                classified.UnionWith(doc.ClockEvidence.GetValueOrDefault(key) ?? []);
                classified.UnionWith(doc.ClockBindings.GetValueOrDefault(key)?.PeriodHeaderIds ?? []);
                classified.UnionWith((doc.ClockReplicas.GetValueOrDefault(key) ?? []).SelectMany(b => b.PeriodHeaderIds));
            }
            if (doc.Kind == RecoveryDocumentKind.Return) classified.UnionWith(doc.NormalTimeNoteEvidence);
        }
        classified.UnionWith(doc.Cells.Where(c => c.BindingMode == RecoveryBindingMode.RoleProposal).SelectMany(c => c.RoleScopes).SelectMany(s => s.LabelSourceIds));
        // Complete native PDF capture can contain a literal separator space
        // between year and term. Retain its source, but it has no semantic role.
        // OCR spaces are not independent proof of absent original print.
        classified.UnionWith(doc.Sources.Where(s=>!s.FromOcr && !string.IsNullOrEmpty(s.Text) && string.IsNullOrWhiteSpace(s.Text)).Select(s=>s.Id));
        Check(classified.SetEquals(doc.Sources.Select(s => s.Id)), "unclassifiedSource");
        var normalTimes = new[] { "08:50〜09:35", "09:35〜10:20", "10:30〜11:15", "11:15〜12:00", "12:50〜13:35", "13:35〜14:20", "14:30〜15:15", "15:15〜16:00" };
        if (doc.Kind != RecoveryDocumentKind.Timetable)
        {
            var spanKeys = doc.Cells.Where(c => c.Slots.Count > 1).Select(c => $"{c.Slots[0].Day}:{c.Slots.Min(s => s.Period)}-{c.Slots.Max(s => s.Period)}").ToHashSet();
            var clockKeys = doc.Times.Keys.Concat(spanKeys).ToHashSet();
            Check(spanKeys.SetEquals(doc.SpanTimes.Keys) && clockKeys.SetEquals(doc.ClockEvidence.Keys) && doc.ClockBindings.Keys.All(clockKeys.Contains) && doc.ClockReplicas.Keys.All(doc.ClockBindings.ContainsKey), "clockScope");
            Check(doc.TimeEvidence.ToHashSet().IsSubsetOf(doc.ClockEvidence.Values.SelectMany(ids => ids).ToHashSet()), "clockCoverage");
            Check(doc.Times.Count == doc.Days.Count * maxPeriod && Evidence(doc.TimeEvidence, doc.TimeEvidence), "times");
            foreach (var day in doc.Days)
            {
                var previous = "00:00";
                foreach (var p in Enumerable.Range(1, maxPeriod))
                {
                    var valid = doc.Times.TryGetValue(day + ":" + p, out var clock) && Regex.IsMatch(clock, @"^(?:[01]\d|2[0-3]):[0-5]\d〜(?:[01]\d|2[0-3]):[0-5]\d$");
                    Check(valid, "clock");
                    var clockIds = doc.ClockEvidence.GetValueOrDefault(day + ":" + p) ?? [];
                    var explicitClock = clock is not null && ClockBound(day, p, p, clock);
                    var normal = doc.Kind == RecoveryDocumentKind.Return && day != doc.Days.Order().FirstOrDefault() && noteValid && clock == normalTimes[p - 1] && Evidence(clockIds, doc.NormalTimeNoteEvidence);
                    Check(explicitClock || normal, "clockEvidence");
                    Check(!doc.ClockBindings.ContainsKey(day + ":" + p) || explicitClock, "clockBinding");
                    if (doc.Kind == RecoveryDocumentKind.Return && day != doc.Days.Order().FirstOrDefault()) Check(noteValid && clock == normalTimes[p - 1], "normalTimeCondition");
                    if (valid) { var parts = clock!.Split('〜'); Check(string.CompareOrdinal(parts[0], previous) >= 0 && string.CompareOrdinal(parts[1], parts[0]) > 0, "clockOrder"); previous = parts[1]; }
                }
            }
            if (doc.Kind == RecoveryDocumentKind.Return) Check(noteValid, "normalTimeNote");
        }
        foreach (var cell in doc.Cells)
        {
            work.Step(cell.SourceIds.Count + index.Cell(cell.Id).Count);
            var cellIds = cell.SourceIds.ToHashSet();
            Check(index.Cell(cell.Id).Select(s => s.Id).ToHashSet().SetEquals(cellIds), "sourceInventory");
            Check(index.Intersecting(cell.Page, cell.Box).All(s => s.CellId == cell.Id && cellIds.Contains(s.Id)), "unassignedCellText");
            Check(cell.InputState == RecoveryInputState.Complete && cell.Box.Valid && cell.Page > 0, "incompleteCell");
            Check(cell.SourceIds.Distinct().Count() == cell.SourceIds.Count && cell.SourceIds.All(id => sources.TryGetValue(id, out var source)
                && source.CellId == cell.Id && source.Page == cell.Page && Contains(cell.Box, source.Box)), "sourcePosition");
            if (cell.Slots.FirstOrDefault() is { } slot)
            {
                Check(ClassHeader(cell.ClassHeaderIds, doc.ClassEvidence.GetValueOrDefault(slot.ClassName) ?? [], slot.ClassName, cell), "classBinding");
                Check(Header(cell.DayHeaderIds, doc.DayEvidence.GetValueOrDefault(slot.Day) ?? [], DayLabels(slot.Day, doc.Kind), cell, cell.DayRegion), "dayBinding");
                Check(cell.Slots.All(s => Header(cell.PeriodHeaderIds.Where(id => AllowedContains(doc.PeriodEvidence.GetValueOrDefault(s.Period.ToString()) ?? [], id)).ToArray(), doc.PeriodEvidence.GetValueOrDefault(s.Period.ToString()) ?? [], [s.Period.ToString(), $"{s.Period}限", $"{s.Period}時限", $"{s.Period}時限目", $"第{s.Period}時限"], cell, cell.PeriodRegions.GetValueOrDefault(s.Period.ToString()))), "periodBinding");
            }
            var periods = cell.Slots.Select(s => s.Period).Order().ToArray();
            if (doc.Kind != RecoveryDocumentKind.Timetable && periods.Length > 1)
            {
                var key = $"{cell.Slots[0].Day}:{periods[0]}-{periods[^1]}";
                var validClock = doc.SpanTimes.TryGetValue(key, out var clock) && Regex.IsMatch(clock, @"^(?:[01][0-9]|2[0-3]):[0-5][0-9]〜(?:[01][0-9]|2[0-3]):[0-5][0-9]$");
                var normalSpan = doc.Kind == RecoveryDocumentKind.Return && cell.Slots[0].Day != doc.Days.Order().FirstOrDefault() && noteValid && periods[0] >= 1 && periods[^1] <= 8
                    && clock == normalTimes[periods[0] - 1].Split('〜')[0] + "〜" + normalTimes[periods[^1] - 1].Split('〜')[1]
                    && (doc.ClockEvidence.GetValueOrDefault(key) ?? []).ToHashSet().SetEquals(doc.NormalTimeNoteEvidence) && !doc.ClockBindings.ContainsKey(key);
                Check(validClock && (ClockBound(cell.Slots[0].Day, periods[0], periods[^1], clock!) || normalSpan), "spanTimeEvidence");
                if (doc.Kind == RecoveryDocumentKind.Return && cell.Slots[0].Day != doc.Days.Order().FirstOrDefault())
                    Check(noteValid && periods[0] >= 1 && periods[^1] <= 8 && clock == normalTimes[periods[0] - 1].Split('〜')[0] + "〜" + normalTimes[periods[^1] - 1].Split('〜')[1], "normalSpanTimeCondition");
            }
            Check(Enum.IsDefined(cell.BindingMode), "bindingMode");
            var proposal = cell.BindingMode == RecoveryBindingMode.RoleProposal;
            Check(proposal || cell.RoleScopes.Count == 0, "unusedRoleScopes");
            if (!proposal) Check(!cell.LessonBindings.SelectMany(b => new[] { b.Subject, b.Teacher, b.Room }).Any(ids => RecoveryRoleLabels.HasPrefix(Raw(ids.Where(sources.ContainsKey)))), "unboundRoleLabel");
            var inlineLabelIds = cell.RoleScopes.Where(s => s.Proof == RecoveryRoleProof.InlineLabel).SelectMany(s => s.LabelSourceIds).ToHashSet();
            var bodyIds = cell.SourceIds.Where(id => !inlineLabelIds.Contains(id)).ToHashSet();
            if (proposal)
            {
                Check(cell.LessonBindings.Count == 0 && !cell.ConfirmedEmpty && (structurePreflight && RecoveryStructure.Pending(cell) || cell.RoleScopes.Count == cell.ParallelCount * 3) &&
                    cell.RoleScopes.Select(s => (s.LessonIndex, s.Role)).Distinct().Count() == cell.RoleScopes.Count, "roleScopeCount");
                foreach (var scope in cell.RoleScopes)
                {
                    var labels = RecoveryRoleLabels.For(scope.Role);
                    Check(scope.LessonIndex >= 0 && scope.LessonIndex < cell.ParallelCount && Enum.IsDefined(scope.Role) && Enum.IsDefined(scope.Proof) && scope.Page == cell.Page && Contains(cell.Box, scope.Box), "roleScopePosition");
                    var allowed = scope.Proof == RecoveryRoleProof.InlineLabel ? cell.SourceIds : scope.LabelSourceIds;
                    var virtualCell = cell with { Box = scope.Box };
                    Check(Header(scope.LabelSourceIds, allowed, labels.Concat(labels.Select(l => l + ":")).ToArray(), virtualCell, scope.LabelRegion), "roleScopeProof");
                    Check(!scope.EmptyVerified || scope.Role != RecoveryFieldRole.Subject && !bodyIds.Any(id => Contains(scope.Box, sources[id].Box)), "roleFalseEmpty");
                }
                foreach (var lessonScopes in cell.RoleScopes.Where(s => s.Proof == RecoveryRoleProof.InlineLabel).GroupBy(s => s.LessonIndex))
                {
                    var pluralRoles = 0;
                    foreach (var scope in lessonScopes)
                    {
                        work.Step(cell.SourceIds.Count);
                        var ids = cell.SourceIds.Where(id => !inlineLabelIds.Contains(id) && sources.TryGetValue(id, out var source) && Contains(scope.Box, source.Box)).ToArray();
                        var raw = Raw(ids); work.Step(raw.Length * 3L);
                        if (raw.Replace('･', '・').Split('・').Length > 1) pluralRoles++;
                    }
                    // One inline label per role cannot certify several tuples,
                    // including in previously stored audits and previews.
                    Check(pluralRoles < 2, "inlineParallelEvidence");
                }
                Check(!cell.RoleScopes.SelectMany((a, i) => cell.RoleScopes.Skip(i + 1).Select(b => (a, b))).Any(p =>
                    Math.Min(p.a.Box.X + p.a.Box.Width, p.b.Box.X + p.b.Box.Width) > Math.Max(p.a.Box.X, p.b.Box.X) && Math.Min(p.a.Box.Y + p.a.Box.Height, p.b.Box.Y + p.b.Box.Height) > Math.Max(p.a.Box.Y, p.b.Box.Y)), "roleScopeOverlap");
                Check(structurePreflight && RecoveryStructure.Pending(cell) || bodyIds.All(id => cell.RoleScopes.Count(s => Contains(s.Box, sources[id].Box)) == 1), "roleBodyCoverage");
            }
            var bindingIds = cell.LessonBindings.SelectMany(b => b.Subject.Concat(b.Teacher).Concat(b.Room)).ToArray();
            foreach (var binding in cell.LessonBindings)
            {
                var pluralRoles = 0;
                foreach (var ids in new[] { binding.Subject, binding.Teacher, binding.Room })
                {
                    work.Step(ids.Count);
                    var raw = Raw(ids.Where(sources.ContainsKey)); work.Step(raw.Length * 3L);
                    if (raw.Replace('･', '・').Split('・').Length > 1) pluralRoles++;
                }
                Check(pluralRoles < 2, "fixedParallelEvidence");
            }
            var separatorIds = new HashSet<string>();
            if (cell.ParallelSeparators is { } proof)
            {
                var roles = new[] { "subject", "teacher", "room" };
                work.Step(proof.Count);
                var bindingSet = new HashSet<string>();
                foreach (var id in bindingIds) { work.Step(); bindingSet.Add(id); }
                var validProof = !proposal && !cell.ConfirmedEmpty && cell.ParallelCount == 2 && cell.LessonBindings.Count == 2 &&
                    proof.Count == 3 && proof.Keys.ToHashSet().SetEquals(roles) && proof.Values.Distinct().Count() == 3;
                Check(validProof, "parallelEvidence");
                if (validProof)
                {
                    double? previousY = null;
                    foreach (var role in roles)
                    {
                        var id = proof[role];
                        if (!sources.TryGetValue(id, out var separator)) { Check(false, "parallelEvidence"); continue; }
                        work.Step(); var y = separator.Box.Y + separator.Box.Height / 2;
                        var belongs = separator.Text is "・" or "･" && separator.CellId == cell.Id && separator.Page == cell.Page &&
                            cellIds.Contains(id) && Contains(cell.Box, separator.Box) && !bindingSet.Contains(id) && (previousY is null || y > previousY + 4);
                        previousY = y;
                        IReadOnlyList<string> Ids(RecoveryLessonBinding binding) => role == "subject" ? binding.Subject : role == "teacher" ? binding.Teacher : binding.Room;
                        bool Side(IReadOnlyList<string> ids, bool left) => ids.All(sourceId => {
                            work.Step();
                            return sources.TryGetValue(sourceId, out var source) && source.CellId == cell.Id && source.Page == cell.Page &&
                                Math.Abs(source.Box.Y + source.Box.Height / 2 - y) <= 2 &&
                                (left ? source.Box.X + source.Box.Width <= separator.Box.X : source.Box.X >= separator.Box.X + separator.Box.Width);
                        });
                        belongs &= Side(Ids(cell.LessonBindings[0]), true) && Side(Ids(cell.LessonBindings[1]), false);
                        Check(belongs, "parallelEvidence");
                        // Only an individually proved original separator may be
                        // subtracted from complete source inventory.
                        if (belongs) separatorIds.Add(id);
                    }
                }
            }
            if (doc.Kind==RecoveryDocumentKind.Timetable && doc.Classes.Count==17 && doc.Days.Count==5 && doc.RequiredSlots.Count==680 &&
                !proposal && !cell.ConfirmedEmpty && cell.Slots.Count==1 && cell.ParallelCount==1 && cell.LessonBindings.Count==1)
            {
                var binding=cell.LessonBindings[0];var roles=new[]{binding.Subject,binding.Teacher,binding.Room};
                if(roles.All(ids=>ids.Count>0))
                {
                    work.Step(roles.Sum(ids=>(long)ids.Count)*4);
                    var bounds=new List<RecoveryBox>();
                    foreach(var ids in roles)
                    {
                        var original=ids.Where(sources.ContainsKey).Select(id=>sources[id].Box).ToArray();
                        if(original.Length!=ids.Count)continue;
                        bounds.Add(new(original.Min(b=>b.X),original.Min(b=>b.Y),original.Max(b=>b.X+b.Width)-original.Min(b=>b.X),original.Max(b=>b.Y+b.Height)-original.Min(b=>b.Y)));
                    }
                    Check(bounds.Count==3 && bounds.Zip(bounds.Skip(1)).All(p=>p.First.Y+p.First.Height<p.Second.Y),"orderedRowEvidence");
                }
            }
            if (cell.OrderedRowProof is { } ordered)
            {
                work.Step(ordered.SourceIds.Count+ordered.Rows.Count);
                var valid=ordered.Version==1 && doc.Kind==RecoveryDocumentKind.Timetable && doc.Classes.ToHashSet().SetEquals(SpecialClasses) &&
                    doc.Days.ToHashSet().SetEquals(new[]{"1","2","3","4","5"}) && doc.RequiredSlots.Count==680 && !proposal &&
                    !cell.ConfirmedEmpty && cell.Slots.Count==1 && cell.ParallelCount==1 && cell.LessonBindings.Count==1 &&
                    cell.BlankFields.Count==0 && separatorIds.Count==0 && ordered.SourceIds.SequenceEqual(bindingIds) &&
                    ordered.SourceIds.SequenceEqual(cell.SourceIds) && ordered.Rows.Count==3;
                Check(valid,"orderedRowContract");
                if(ordered.Rows.Count!=3 || ordered.Rows.Any(r=>r.Count>256))return new(["inputLimit"]);
                var bounds=new List<RecoveryBox>();var binding=cell.LessonBindings.SingleOrDefault();
                var roleIds=binding is null ? [] : new[]{binding.Subject,binding.Teacher,binding.Room};
                for(var role=0;role<3;role++)
                {
                    var pieces=ordered.Rows[role];work.Step(pieces.Count*4L);
                    var ids=roleIds.Length==3?roleIds[role]:[];
                    var agrees=RecoveryOrderedRowProof.SingleRow(pieces) && ids.Count==pieces.Count &&
                        !RecoveryRoleLabels.HasPrefix(string.Concat(pieces.Select(p=>p.Text))) &&
                        !pieces.Any(p=>p.Text.Any(c=>"・･/／".Contains(c))) &&
                        ids.Select((id,index)=>sources.TryGetValue(id,out var source) && source.CellId==cell.Id && source.Page==cell.Page &&
                            source.Text==pieces[index].Text && source.Box==pieces[index].Box && source.SourceLine==pieces[index].SourceLine &&
                            source.SourceOrder==pieces[index].SourceOrder && cell.Box.Contains(source.Box)).All(v=>v);
                    Check(agrees,"orderedRowEvidence");
                    if(pieces.Count>0)bounds.Add(new(pieces.Min(p=>p.Box.X),pieces.Min(p=>p.Box.Y),pieces.Max(p=>p.Box.X+p.Box.Width)-pieces.Min(p=>p.Box.X),pieces.Max(p=>p.Box.Y+p.Box.Height)-pieces.Min(p=>p.Box.Y)));
                }
                Check(bounds.Count==3 && bounds.Zip(bounds.Skip(1)).All(p=>p.First.Y+p.First.Height<p.Second.Y),"orderedRowEvidence");
            }
            Check(proposal || (cell.ConfirmedEmpty ? cell.LessonBindings.Count == 0 : cell.LessonBindings.Count == cell.ParallelCount && bindingIds.Distinct().Count() == bindingIds.Length && bindingIds.ToHashSet().SetEquals(cell.SourceIds.Where(id => !separatorIds.Contains(id)))), "lessonBinding");
            Check(cell.Slots.Count > 0 && cell.Slots.Select(s => (s.ClassName, s.Day)).Distinct().Count() == 1 &&
                cell.Slots.Select(s => s.Period).Order().Zip(cell.Slots.Select(s => s.Period).Order().Skip(1)).All(p => p.Second == p.First + 1), "span");
            if (!cells.TryGetValue(cell.Id, out var recovered)) continue;
            if (recovered.State == RecoveryValueState.Empty)
            { Check(cell.ConfirmedEmpty && cell.SourceIds.Count == 0 && recovered.Lessons.Count == 0, "falseEmpty"); continue; }
            Check(!cell.ConfirmedEmpty && recovered.State == RecoveryValueState.Present && cell.ParallelCount is >= 1 and <= 4 && recovered.Lessons.Count == cell.ParallelCount, "cellState");
            foreach (var (lesson, lessonIndex) in recovered.Lessons.Select((l, i) => (l, i)))
            {
                var binding = cell.LessonBindings.ElementAtOrDefault(lessonIndex) ?? new RecoveryLessonBinding([], [], []);
                foreach (var pair in new[] { ("subject", lesson.Subject), ("teacher", lesson.Teacher), ("room", lesson.Room) })
                {
                    var field = pair.Item2;
                    var fieldRole = pair.Item1 == "subject" ? RecoveryFieldRole.Subject : pair.Item1 == "teacher" ? RecoveryFieldRole.Teacher : RecoveryFieldRole.Room;
                    if (manual?.GetValueOrDefault(new(cell.Id, lessonIndex, fieldRole)) is { } correction)
                    {
                        Check(field.State == RecoveryValueState.Present && field.Value == correction.CorrectedText &&
                            field.Evidence.SequenceEqual(correction.OriginalParentIds), "humanField");
                        // Keep checking original ownership/order/partition. Human text
                        // is never rewritten into original OCR or provider evidence.
                        if (field.Evidence.All(sources.ContainsKey)) field = field with { Value = Raw(field.Evidence) };
                    }
                    Check(field.Value.Length <= 1024, "fieldLimit");
                    if (proposal)
                    {
                        var role = pair.Item1 == "subject" ? RecoveryFieldRole.Subject : pair.Item1 == "teacher" ? RecoveryFieldRole.Teacher : RecoveryFieldRole.Room;
                        var scope = cell.RoleScopes.SingleOrDefault(s => s.LessonIndex == lessonIndex && s.Role == role);
                        var allowed = scope is null ? [] : bodyIds.Where(id => Contains(scope.Box, sources[id].Box)).ToArray();
                        Check(scope is not null && (field.State == RecoveryValueState.Empty ? scope.EmptyVerified && field.Value.Length == 0 && field.Evidence.Count == 0 && allowed.Length == 0 : field.State == RecoveryValueState.Present && Ordered(field.Evidence) && Evidence(field.Evidence, allowed, field.Value)), "roleFieldEvidence");
                    }
                    else if (field.State == RecoveryValueState.Empty) Check((pair.Item1 == "subject" ? binding.Subject : pair.Item1 == "teacher" ? binding.Teacher : binding.Room).Count == 0 && pair.Item1 != "subject" && field.Value.Length == 0 && field.Evidence.Count == 0 && cell.BlankFields.Contains(pair.Item1), "falseBlankField");
                    else { var ids = pair.Item1 == "subject" ? binding.Subject : pair.Item1 == "teacher" ? binding.Teacher : binding.Room; Check(field.State == RecoveryValueState.Present && field.Evidence.SequenceEqual(ids) && Ordered(field.Evidence) && Evidence(field.Evidence, ids, field.Value), "fieldEvidence"); }
                }
                var day = cell.Slots.FirstOrDefault()?.Day;
                Check(day is not null && doc.DayEvidence.TryGetValue(day, out var dateIds) && Evidence(lesson.DateEvidence, cell.DayHeaderIds), "lessonDateEvidence");
                var periodIds = cell.PeriodHeaderIds;
                Check(Evidence(lesson.PeriodEvidence, periodIds) && cell.Slots.All(s => lesson.PeriodEvidence.Any(id => AllowedContains(doc.PeriodEvidence.GetValueOrDefault(s.Period.ToString(CultureInfo.InvariantCulture)) ?? [], id))), "lessonPeriodEvidence");
            }
            if (proposal)
            {
                var assigned = recovered.Lessons.SelectMany(l => l.Subject.Evidence.Concat(l.Teacher.Evidence).Concat(l.Room.Evidence)).ToArray();
                Check(assigned.Distinct().Count() == assigned.Length && assigned.ToHashSet().SetEquals(bodyIds), "rolePartition");
            }
            Check(recovered.Lessons.Select(l => Fingerprint(l)).Distinct().Count() == recovered.Lessons.Count, "parallelDuplicate");
        }
        return new(errors);
    }
    public static IReadOnlyList<string> InputErrors(RecoveryDocument doc, CancellationToken token = default) => Validate(doc, new(doc.PdfHash, doc.Kind, doc.SchoolYear, doc.Term,
        doc.Cells.Select(c => new RecoveredCell(c.Id, RecoveryValueState.Missing, [])).ToArray(), doc.StructureMetadata ?? new("rule", "rules", "1", "1", "1", SchemaVersion, Version, "preflight")), token).Errors.Where(e => e is not "cellState" and not "rolePartition" and not "humanUnresolved").ToArray();
    internal static IReadOnlyList<string> StructureInputErrors(RecoveryDocument doc, CancellationToken token = default)
    {
        try
        {
            var result = new RecoveryResult(doc.PdfHash, doc.Kind, doc.SchoolYear, doc.Term,
                doc.Cells.Select(c => new RecoveredCell(c.Id, RecoveryValueState.Missing, [])).ToArray(),
                doc.StructureMetadata ?? new("rule", "rules", "1", "1", "1", SchemaVersion, Version, "preflight"));
            return ValidateCore(doc, result, structurePreflight: true, token: token).Errors.Where(e => e is not "cellState" and not "rolePartition").ToArray();
        }
        catch (RecoveryWorkLimitException) { return ["validationLimit"]; }
        catch (Exception e) when (e is NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return ["malformedInput"]; }
    }
    public static bool CanReuse(RecoveryAcceptance acceptance, RecoveryDocument document, RecoveryResult result, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (acceptance.PdfHash != document.PdfHash || acceptance.ResultHash != Fingerprint(result) || acceptance.Metadata != result.Metadata) return false;
        token.ThrowIfCancellationRequested(); var scopeHash = Fingerprint(document); token.ThrowIfCancellationRequested();
        return acceptance.ScopeHash == scopeHash && Validate(document, result, token).CanAdopt;
    }
}
