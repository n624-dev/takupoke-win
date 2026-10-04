using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;

/// <summary>Revalidates an already confirmed v4 audit without altering source content.</summary>
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
        var metadata = original.Result.Metadata with { ValidatorVersion = 4 };
        var structure = original.Document.StructureMetadata;
        var old = new RecoveryAudit(original.Document with {
            StructureMetadata = structure is null ? null : structure with { ValidatorVersion = 4 }
        }, original.Result with { Metadata = metadata }, original.PreviousAcceptance);
        var exact = TryRecertify(old, token);
        token.ThrowIfCancellationRequested();
        return exact is not null && RecoveryValidator.Fingerprint(exact) == RecoveryValidator.Fingerprint(original);
    }
    internal static RecoveryAudit? TryRecertify(RecoveryAudit original, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (original.Document is null || original.Result?.Metadata is null || original.Acceptance is null) return null;
        var old = original.Acceptance;
        var metadata = original.Result.Metadata;
        var structure = original.Document.StructureMetadata;
        // This is one explicit v4→v5 compatibility transition, not permission
        // to relabel arbitrary older providers, schemas or unconfirmed results.
        if (RecoveryValidator.Version != 5 || metadata.ValidatorVersion != 4 ||
            metadata.RecoverySchemaVersion != RecoveryValidator.SchemaVersion || original.PreviousAcceptance is not null ||
            old.PdfHash != original.Document.PdfHash || old.PdfHash != original.Result.PdfHash || old.Metadata != metadata ||
            // V4's historical acceptance gate required identical metadata.
            // A newly valid two-provider history is not proof of past consent.
            structure is not null && structure != metadata)
            return null;
        if (old.ResultHash != RecoveryValidator.Fingerprint(original.Result)) return null;
        token.ThrowIfCancellationRequested();
        if (old.ScopeHash != RecoveryValidator.Fingerprint(original.Document)) return null;
        token.ThrowIfCancellationRequested();
        var current = metadata with { ValidatorVersion = RecoveryValidator.Version };
        // Structural acquisition and field extraction may have different
        // providers/prompts. Retain both histories; update only validation.
        var document = original.Document with { StructureMetadata = structure is null ? null : structure with { ValidatorVersion = RecoveryValidator.Version } };
        var result = original.Result with { Metadata = current };
        if (!RecoveryValidator.Validate(document, result, token).CanAdopt) return null;
        var acceptance = new RecoveryAcceptance(old.PdfHash, RecoveryValidator.Fingerprint(result),
            RecoveryValidator.Fingerprint(document), current, old.AcceptedAt);
        token.ThrowIfCancellationRequested();
        return new(document, result, acceptance) { PreviousAcceptance = old };
    }
}
