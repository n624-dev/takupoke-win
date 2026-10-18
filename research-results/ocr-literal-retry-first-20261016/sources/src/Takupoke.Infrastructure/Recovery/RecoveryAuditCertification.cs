using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;

/// <summary>Revalidates immutable accepted historical objects under current semantics.</summary>
internal static class RecoveryAuditCertification
{
    internal static RecoveryAudit? Reusable(RecoveryAudit original, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try { return IsCurrent(original, token) ? original : TryRecertify(original, token); }
        catch (Exception error) when (error is NullReferenceException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        { return null; }
    }
    internal static bool IsCurrent(RecoveryAudit original, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (original.CurrentCertification is { } certificate)
            return certificate.ValidatorVersion == RecoveryValidator.Version && HistoricalHashes(original, token) &&
                RecoveryValidator.ValidateCertifiedAudit(original.Document, original.Result, original.Acceptance,
                    original.PreviousAcceptance, certificate, original.PreviousCertification, token, original.CertificationHistory).CanAdopt;
        // Current ordinary acceptances cannot smuggle an old certificate or a
        // metadata-only historical promotion into the fresh-validation path.
        return original.Result?.Metadata?.ValidatorVersion == RecoveryValidator.Version &&
            original.PreviousCertification is null && original.CertificationHistory is null && original.PreviousAcceptance is null &&
            RecoveryValidator.CanReuse(original.Acceptance, original.Document, original.Result, token);
    }
    internal static RecoveryAudit? TryRecertify(RecoveryAudit original, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (original.Document is null || original.Result?.Metadata is null || original.Acceptance is null ||
            original.CertificationHistory is not null || original.PreviousCertification is not null && original.CurrentCertification?.ValidatorVersion!=9 || !HistoricalHashes(original, token)) return null;
        if (!RecoveryValidator.AuditShapeWithinLimit(original.Document, original.Result, original.Acceptance,
            original.PreviousAcceptance, original.CurrentCertification, original.PreviousCertification, token, original.CertificationHistory)) return null;
        var predecessor = original.CurrentCertification;
        if (predecessor is not null && predecessor.ValidatorVersion!=9 && !GenuineManual7Certificate8(original, predecessor, token)) return null;
        IReadOnlyList<RecoverySemanticCertification>? history=predecessor?.ValidatorVersion==9 && original.PreviousCertification is { } ancestor ? new[]{ancestor} : null;
        var old = original.Acceptance;
        var certificate = new RecoverySemanticCertification(RecoveryValidator.SchemaVersion, RecoveryValidator.Version,
            old.ScopeHash, old.ResultHash, RecoveryValidator.Fingerprint(old), RecoveryValidator.Fingerprint(original.PreviousAcceptance))
        { PreviousCertificationHash = RecoveryValidator.Fingerprint(predecessor) };
        if (!RecoveryValidator.ValidateCertifiedAudit(original.Document, original.Result, old,
            original.PreviousAcceptance, certificate, predecessor, token, history).CanAdopt) return null;
        // No metadata, correction snapshot, original consent, ancestry or proof
        // object is rewritten. The old manual certificate remains available.
        return original with { CurrentCertification = certificate, PreviousCertification = predecessor, CertificationHistory = history };
    }
    private static bool GenuineManual7Certificate8(RecoveryAudit original, RecoverySemanticCertification certificate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return original.Result.Metadata.ValidatorVersion == 7 && original.Result.HumanCorrections is { Count: > 0 } &&
            original.PreviousAcceptance is null && certificate.RecoverySchemaVersion == RecoveryValidator.SchemaVersion &&
            certificate.ValidatorVersion == 8 && certificate.PreviousCertificationHash is null &&
            certificate.ScopeHash == original.Acceptance.ScopeHash && certificate.ResultHash == original.Acceptance.ResultHash &&
            certificate.AcceptanceHash == RecoveryValidator.Fingerprint(original.Acceptance) &&
            certificate.PreviousAcceptanceHash == RecoveryValidator.Fingerprint(original.PreviousAcceptance);
    }
    private static bool HistoricalHashes(RecoveryAudit original, CancellationToken token) =>
        RecoveryValidator.HistoricalAcceptanceEnvelopeValid(original.Document, original.Result, original.Acceptance, original.PreviousAcceptance, token);
}
