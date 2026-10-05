using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;

/// <summary>Revalidates an already confirmed v4/v5 audit without altering source content.</summary>
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
        if (!RecoveryValidator.CanReuse(original.Acceptance, original.Document, original.Result, token)) return false;
        if (original.PreviousAcceptance is null) return true;
        var priorVersion = original.PreviousAcceptance.Metadata.ValidatorVersion;
        if (priorVersion is not (4 or 5)) return false;
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
        if (original.Document is null || original.Result?.Metadata is null || original.Acceptance is null) return null;
        if (!HistoricalHashes(original, token)) return null;
        var old = original.Acceptance;
        var metadata = original.Result.Metadata;
        var structure = original.Document.StructureMetadata;
        var firstConfirmation = old;
        if (original.PreviousAcceptance is { } previous)
        {
            // The only historical chain is the exact V4→V5 revalidation.
            // V5 added no source proof and retained the original V4 consent.
            if (metadata.ValidatorVersion != 5 || previous.Metadata?.ValidatorVersion != 4 || previous.AcceptedAt != old.AcceptedAt) return null;
            var predecessor = new RecoveryAudit(original.Document with {
                StructureMetadata = structure is null ? null : structure with { ValidatorVersion = 4 }
            }, original.Result with { Metadata = metadata with { ValidatorVersion = 4 } }, previous);
            if (!HistoricalHashes(predecessor, token)) return null;
            firstConfirmation = previous;
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
    private static bool HistoricalHashes(RecoveryAudit original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var metadata = original.Result.Metadata; var acceptance = original.Acceptance; var structure = original.Document.StructureMetadata;
        if (original.Document.Cells.Count > 20000 || original.Document.Sources.Count > 100000) return false;
        if (RecoveryValidator.Version != 6 || metadata.ValidatorVersion is not (4 or 5) ||
            metadata.RecoverySchemaVersion != RecoveryValidator.SchemaVersion || metadata.RecoveryVersion != "2" ||
            acceptance.PdfHash != original.Document.PdfHash || acceptance.PdfHash != original.Result.PdfHash || acceptance.Metadata != metadata ||
            structure is not null && (structure.ValidatorVersion != metadata.ValidatorVersion || metadata.ValidatorVersion == 4 && structure != metadata)) return false;
        foreach (var cell in original.Document.Cells) { token.ThrowIfCancellationRequested(); if (cell.ParallelSeparators is not null) return false; }
        if (acceptance.ResultHash != RecoveryValidator.Fingerprint(original.Result)) return false;
        token.ThrowIfCancellationRequested();
        if (acceptance.ScopeHash != RecoveryValidator.Fingerprint(original.Document)) return false;
        token.ThrowIfCancellationRequested();
        return true;
    }
}
