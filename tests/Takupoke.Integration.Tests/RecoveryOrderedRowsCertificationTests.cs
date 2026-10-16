using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryOrderedRowsCertificationTests
{
    private static RecoverySemanticCertification Certificate(RecoveryAudit audit, int version,
        RecoverySemanticCertification? predecessor = null) =>
        new(RecoveryValidator.SchemaVersion, version, audit.Acceptance.ScopeHash, audit.Acceptance.ResultHash,
            RecoveryValidator.Fingerprint(audit.Acceptance), RecoveryValidator.Fingerprint(audit.PreviousAcceptance))
        { PreviousCertificationHash = version == 8 ? null : RecoveryValidator.Fingerprint(predecessor) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalNineConsentRevalidatesWithoutRewritingOriginalObjects(bool structure)
    {
        var seed = RecoveryManualCertificationTests.Historical(structure);
        var doc = seed.Document with { StructureMetadata = seed.Document.StructureMetadata is { } m ? m with { ValidatorVersion = 9 } : null };
        var result = seed.Result with { Metadata = seed.Result.Metadata with { ValidatorVersion = 9 },
            HumanCorrections = seed.Result.HumanCorrections!.Select(c => c with { DocumentSnapshot = RecoveryValidator.Fingerprint(doc) }).ToArray() };
        var old = new RecoveryAudit(doc, result, seed.Acceptance with { Metadata = result.Metadata,
            ScopeHash = RecoveryValidator.Fingerprint(doc), ResultHash = RecoveryValidator.Fingerprint(result) });
        var before = JsonSerializer.SerializeToUtf8Bytes(old);
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.Reusable(old));
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(current with { CurrentCertification = null }));
        Assert.Equal(old.Acceptance.AcceptedAt, current.Acceptance.AcceptedAt);
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
    }

    [Fact]
    public void ManualSevenCertificateEightThenNineRetainsBothAncestorsAndConsent()
    {
        var old = RecoveryManualCertificationTests.Historical(true);
        var eight = Certificate(old, 8);
        var nine = Certificate(old, 9, eight);
        old = old with { CurrentCertification = nine, PreviousCertification = eight };
        var before = JsonSerializer.SerializeToUtf8Bytes(old);
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.Reusable(old));
        Assert.Equal(nine, current.PreviousCertification);
        Assert.Equal(eight, Assert.Single(current.CertificationHistory!));
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(current with {
            CurrentCertification = nine, PreviousCertification = eight, CertificationHistory = null }));
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.Same(current, RecoveryAuditCertification.Reusable(current));
        Assert.Equal(RecoveryValidator.Fingerprint(current), RecoveryValidator.Fingerprint(
            JsonSerializer.Deserialize<RecoveryAudit>(JsonSerializer.Serialize(current))!));
    }

    [Theory]
    [InlineData("nineHash")]
    [InlineData("eightScope")]
    [InlineData("eightHash")]
    [InlineData("emptyHistory")]
    [InlineData("extraHistory")]
    [InlineData("lostHistory")]
    public void ChangedAncestryCannotAuthorizeAReuse(string mutation)
    {
        var seed = RecoveryManualCertificationTests.Historical(true);
        var eight = Certificate(seed, 8);
        var nine = Certificate(seed, 9, eight);
        var historical = seed with { CurrentCertification = nine, PreviousCertification = eight };
        if (mutation.StartsWith("eight", StringComparison.Ordinal) || mutation == "nineHash")
        {
            var damaged = mutation switch {
                "nineHash" => historical with { CurrentCertification = nine with { PreviousCertificationHash = new string('a', 64) } },
                "eightScope" => historical with { PreviousCertification = eight with { ScopeHash = new string('a', 64) } },
                _ => historical with { PreviousCertification = eight with { PreviousCertificationHash = RecoveryValidator.Fingerprint(null as RecoverySemanticCertification) } }
            };
            Assert.Null(RecoveryAuditCertification.Reusable(damaged));
            return;
        }
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.Reusable(historical));
        var altered = current with { CertificationHistory = mutation switch {
            "emptyHistory" => [], "extraHistory" => [eight, eight], _ => null } };
        Assert.False(RecoveryAuditCertification.IsCurrent(altered));
        Assert.Null(RecoveryAuditCertification.Reusable(altered));
    }
}
