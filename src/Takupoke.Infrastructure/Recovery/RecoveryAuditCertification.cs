using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;

/// <summary>Revalidates an already confirmed v4/v5/v6/v7 audit without altering source content.</summary>
internal static class RecoveryAuditCertification
{
    internal static RecoveryAudit? Reusable(RecoveryAudit original, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            return IsCurrent(original, token)
                ? original : TryRecertify(original, token);
        }
        catch (Exception error) when (error is NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        { return null; }
    }
    internal static bool IsCurrent(RecoveryAudit original, CancellationToken token = default)
    {
        if (original.CurrentCertification is { } certificate)
            return HistoricalHashes(original, token) && ValidAncestry(original, token) &&
                RecoveryValidator.ValidateHistoricalManual(original.Document, original.Result, original.Acceptance,
                    original.PreviousAcceptance, certificate, token).CanAdopt;
        if (!RecoveryValidator.CanReuse(original.Acceptance, original.Document, original.Result, token)) return false;
        if (original.PreviousAcceptance is null) return true;
        var priorVersion = original.PreviousAcceptance.Metadata.ValidatorVersion;
        if (priorVersion is not (4 or 5 or 6 or 7)) return false;
        var metadata = original.Result.Metadata with { ValidatorVersion = priorVersion };
        var structure = original.Document.StructureMetadata;
        var old = new RecoveryAudit(original.Document with {
            StructureMetadata = structure is null ? null : structure with { ValidatorVersion = priorVersion }
        }, original.Result with { Metadata = metadata }, original.PreviousAcceptance);
        var exact = TryRecertify(old, token);
        token.ThrowIfCancellationRequested();
        return exact is not null && RecoveryValidator.Fingerprint(exact) == RecoveryValidator.Fingerprint(original);
    }
    internal static RecoveryAudit? TryRecertify(RecoveryAudit original, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (original.Document is null || original.Result?.Metadata is null || original.Acceptance is null || original.CurrentCertification is not null) return null;
        if (!HistoricalHashes(original, token)) return null;
        var old = original.Acceptance;
        var metadata = original.Result.Metadata;
        var structure = original.Document.StructureMetadata;
        if (!ValidAncestry(original, token)) return null;
        var firstConfirmation = original.PreviousAcceptance ?? old;
        if (metadata.ValidatorVersion == 7 && (structure is null || structure.ValidatorVersion == 7) && original.Result.HumanCorrections is { Count: > 0 })
        {
            var certificate = new RecoverySemanticCertification(RecoveryValidator.SchemaVersion, RecoveryValidator.Version,
                old.ScopeHash, old.ResultHash, RecoveryValidator.Fingerprint(old), RecoveryValidator.Fingerprint(original.PreviousAcceptance));
            return RecoveryValidator.ValidateHistoricalManual(original.Document, original.Result, old,
                original.PreviousAcceptance, certificate, token).CanAdopt ? original with { CurrentCertification = certificate } : null;
        }
        var current = metadata with { ValidatorVersion = RecoveryValidator.Version };
        // Source bytes, field/structure provider histories and acceptedAt are
        // unchanged. No separator proof is invented for an old acceptance.
        var document = original.Document with { StructureMetadata = structure is null ? null : structure with { ValidatorVersion = RecoveryValidator.Version } };
        var result = original.Result with { Metadata = current };
        if (!RecoveryValidator.Validate(document, result, token).CanAdopt) return null;
        var acceptance = new RecoveryAcceptance(old.PdfHash, RecoveryValidator.Fingerprint(result),
            RecoveryValidator.Fingerprint(document), current, firstConfirmation.AcceptedAt);
        token.ThrowIfCancellationRequested();
        return new(document, result, acceptance) { PreviousAcceptance = firstConfirmation };
    }
    private static bool ValidAncestry(RecoveryAudit original, CancellationToken token)
    {
        if (original.PreviousAcceptance is not { } previous) return true;
        var metadata = original.Result.Metadata; var structure = original.Document.StructureMetadata;
        var version = previous.Metadata.ValidatorVersion;
        if (!(metadata.ValidatorVersion == 5 && version == 4 || metadata.ValidatorVersion == 6 && version is 4 or 5 ||
            metadata.ValidatorVersion == 7 && version is 4 or 5 or 6) || previous.AcceptedAt != original.Acceptance.AcceptedAt) return false;
        return HistoricalHashes(new(original.Document with { StructureMetadata = structure is null ? null : structure with { ValidatorVersion = version } },
            original.Result with { Metadata = metadata with { ValidatorVersion = version } }, previous), token);
    }
    private static bool HistoricalHashes(RecoveryAudit original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var metadata = original.Result.Metadata; var acceptance = original.Acceptance; var structure = original.Document.StructureMetadata;
        if (original.Document.Cells.Count > 20000 || original.Document.Sources.Count > 100000) return false;
        if (RecoveryValidator.Version != 8 || metadata.ValidatorVersion is not (4 or 5 or 6 or 7) ||
            metadata.RecoverySchemaVersion != RecoveryValidator.SchemaVersion || metadata.RecoveryVersion != "2" ||
            acceptance.PdfHash != original.Document.PdfHash || acceptance.PdfHash != original.Result.PdfHash || acceptance.Metadata != metadata ||
            structure is not null && (structure.ValidatorVersion != metadata.ValidatorVersion || metadata.ValidatorVersion == 4 && structure != metadata)) return false;
        // These proofs did not exist in V6 or earlier and cannot be injected
        // while rehashing old metadata. V6's existing separator proof is retained.
        if (metadata.ValidatorVersion < 7)
        {
            if (original.Document.Capture is not null || original.Result.HumanCorrections is not null) return false;
            foreach (var source in original.Document.Sources) { token.ThrowIfCancellationRequested(); if (source.NativeConfidence is not null) return false; }
        }
        // Composite AI grade evidence was not accepted before V8. New acceptance
        // must not be invented merely by rehashing a purported old receipt.
        var gradeIds = original.Document.Sources.Where(s => s.Text.Contains('年')).Select(s => s.Id).ToHashSet();
        foreach (var cell in original.Document.Cells)
        {
            token.ThrowIfCancellationRequested();
            if (cell.Slots.Any(s => s.ClassName is "AI_1" or "AI_2") && cell.ClassHeaderIds.Any(gradeIds.Contains)) return false;
        }
        foreach (var cell in original.Document.Cells) { token.ThrowIfCancellationRequested(); if (metadata.ValidatorVersion < 6 && cell.ParallelSeparators is not null) return false; }
        if (acceptance.ResultHash != RecoveryValidator.Fingerprint(original.Result)) return false;
        token.ThrowIfCancellationRequested();
        if (acceptance.ScopeHash != RecoveryValidator.Fingerprint(original.Document)) return false;
        token.ThrowIfCancellationRequested();
        return true;
    }
}
