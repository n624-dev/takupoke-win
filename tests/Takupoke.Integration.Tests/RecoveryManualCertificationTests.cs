using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryManualCertificationTests
{
    internal static RecoveryAudit Historical(bool structure, string? pdfHash = null)
    {
        var document = RecoveryManualAssistanceTests.Captured(1);
        if (pdfHash is not null) document = document with { PdfHash = pdfHash, Capture = document.Capture! with { PdfHash = pdfHash } };
        if (structure) document = document with { StructureMetadata = new("fictional-structure", "fictional-model", "1", "test", "3", 2, RecoveryValidator.Version, "test") };
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(document));
        var correction = RecoveryManualAssistanceTests.Correction(plan, Assert.Single(plan.Targets), "架空訂正科目");
        var result = RecoveryManualAssistance.Complete(plan, [correction], "host-control");
        // Construct an independently fictional historical receipt. The production
        // migration must preserve these original V7 artifacts without rewriting.
        document = document with { StructureMetadata = document.StructureMetadata is { } metadata ? metadata with { ValidatorVersion = 7 } : null };
        result = result with { Metadata = result.Metadata with { ValidatorVersion = 7 },
            HumanCorrections = [correction with { DocumentSnapshot = RecoveryValidator.Fingerprint(document) }] };
        return new(document, result, new(document.PdfHash, RecoveryValidator.Fingerprint(result), RecoveryValidator.Fingerprint(document),
            result.Metadata, DateTimeOffset.Parse("2032-04-01T00:00:00Z")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactV7ManualHistoriesRevalidateWithoutChangingOriginalHumanProof(bool structure)
    {
        var old = Historical(structure);
        Assert.False(RecoveryValidator.Validate(old.Document, old.Result).CanAdopt);
        Assert.False(RecoveryValidator.CanReuse(old.Acceptance, old.Document, old.Result));
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.TryRecertify(old));
        Assert.Equal(RecoveryValidator.Fingerprint(old.Document), RecoveryValidator.Fingerprint(current.Document));
        Assert.Equal(RecoveryValidator.Fingerprint(old.Result.HumanCorrections), RecoveryValidator.Fingerprint(current.Result.HumanCorrections));
        Assert.Equal(old.Acceptance.AcceptedAt, current.Acceptance.AcceptedAt);
        if (structure)
        {
            Assert.Equal(RecoveryValidator.Fingerprint(old), RecoveryValidator.Fingerprint(current with { CurrentCertification = null }));
            Assert.Equal(RecoveryValidator.Version, current.CurrentCertification!.ValidatorVersion);
            Assert.False(RecoveryValidator.Validate(current.Document, current.Result).CanAdopt);
            Assert.False(RecoveryValidator.CanReuse(current.Acceptance, current.Document, current.Result));
        }
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.Equal(RecoveryValidator.Fingerprint(current), RecoveryValidator.Fingerprint(RecoveryAuditCertification.Reusable(current)));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("version")]
    [InlineData("scope")]
    [InlineData("result")]
    [InlineData("acceptance")]
    [InlineData("previous")]
    [InlineData("human")]
    [InlineData("enteredAt")]
    [InlineData("capture")]
    [InlineData("confidence")]
    [InlineData("box")]
    [InlineData("text")]
    [InlineData("structure")]
    [InlineData("missing")]
    [InlineData("defaultConsent")]
    public void AlteredCurrentCertificateOrOriginalProofCannotBeRenewed(string mutation)
    {
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.TryRecertify(Historical(true)));
        var certificate = current.CurrentCertification!;
        var damaged = mutation switch {
            "defaultConsent" => current with { Acceptance = current.Acceptance with { AcceptedAt = default } },
            "schema" => current with { CurrentCertification = certificate with { RecoverySchemaVersion = 1 } },
            "version" => current with { CurrentCertification = certificate with { ValidatorVersion = 7 } },
            "scope" => current with { CurrentCertification = certificate with { ScopeHash = new string('b', 64) } },
            "result" => current with { CurrentCertification = certificate with { ResultHash = new string('b', 64) } },
            "acceptance" => current with { CurrentCertification = certificate with { AcceptanceHash = new string('b', 64) } },
            "previous" => current with { CurrentCertification = certificate with { PreviousAcceptanceHash = new string('b', 64) } },
            "human" => current with { Result = current.Result with { HumanCorrections = [current.Result.HumanCorrections![0] with { DocumentSnapshot = new string('b',64) }] } },
            "enteredAt" => current with { Result = current.Result with { HumanCorrections = [current.Result.HumanCorrections![0] with { EnteredAt = default }] } },
            "capture" => current with { Document = current.Document with { Capture = null } },
            "confidence" => current with { Document = current.Document with { Sources = current.Document.Sources.Select((s,i) => i == 0 ? s with { NativeConfidence = .2 } : s).ToArray() } },
            "box" => current with { Document = current.Document with { Sources = current.Document.Sources.Select((s,i) => i == 0 ? s with { Box = s.Box with { X = s.Box.X + 1 } } : s).ToArray() } },
            "text" => current with { Document = current.Document with { Sources = current.Document.Sources.Select((s,i) => i == 0 ? s with { Text = "架空改変" } : s).ToArray() } },
            "structure" => current with { Document = current.Document with { StructureMetadata = current.Document.StructureMetadata! with { ModelId = "changed" } } },
            _ => current with { CurrentCertification = null }
        };
        if (mutation == "missing")
        {
            Assert.False(RecoveryAuditCertification.IsCurrent(damaged));
            // Removing a receipt leaves the original explicit V7 acceptance;
            // it may be recertified only by redoing all original/current checks.
            Assert.NotNull(RecoveryAuditCertification.Reusable(damaged));
        }
        else Assert.Null(RecoveryAuditCertification.Reusable(damaged));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ManualV7CannotInventPreManualConsentFromAnOlderVersion(int previousVersion)
    {
        var old = Historical(true);
        var previous = old.Acceptance with { Metadata = old.Acceptance.Metadata with { ValidatorVersion = previousVersion } };
        Assert.Null(RecoveryAuditCertification.TryRecertify(old with { PreviousAcceptance = previous }));
    }

    [Fact]
    public void RehashedInvalidManualProofStillFailsCurrentSemantics()
    {
        var old = Historical(true);
        var result = old.Result with { HumanCorrections = [old.Result.HumanCorrections![0] with { EnteredAt = default }] };
        old = old with { Result = result, Acceptance = old.Acceptance with { ResultHash = RecoveryValidator.Fingerprint(result) } };
        Assert.Null(RecoveryAuditCertification.TryRecertify(old));
    }
}
