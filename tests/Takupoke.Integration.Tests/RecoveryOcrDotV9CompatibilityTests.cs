using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryOcrDotV9CompatibilityTests
{
    private static RecoveryAudit Rule8(bool unsafeDot = false)
    {
        var doc = RecoveryParallelProofTests.Build(RecoveryParallelProofTests.TwoPages("wholeformal"));
        if (unsafeDot) doc = doc with { Sources = doc.Sources.Select(s => s.Text == "架空師B" ? s with { Text = "架空師·", FromOcr = true } : s).ToArray() };
        var cells = doc.Cells.Select(c => c.ConfirmedEmpty ? new RecoveredCell(c.Id, RecoveryValueState.Empty, []) : RecoveryRules.Recover(doc, c)!).ToArray();
        var result = new RecoveryResult(doc.PdfHash, doc.Kind, doc.SchoolYear, doc.Term, cells, new("rule", "rules", "3", "3", "1", 2, 8, "test"));
        return Accepted(doc, result);
    }
    private static RecoveryAudit Accepted(RecoveryDocument doc, RecoveryResult result) => new(doc, result,
        new(doc.PdfHash, RecoveryValidator.Fingerprint(result), RecoveryValidator.Fingerprint(doc), result.Metadata, DateTimeOffset.Parse("2032-04-01T00:00:00Z")));
    private static RecoverySemanticCertification Certificate(RecoveryAudit audit, int version = RecoveryValidator.Version, RecoverySemanticCertification? predecessor = null) =>
        new(2, version, audit.Acceptance.ScopeHash, audit.Acceptance.ResultHash, RecoveryValidator.Fingerprint(audit.Acceptance),
            RecoveryValidator.Fingerprint(audit.PreviousAcceptance)) { PreviousCertificationHash = version == RecoveryValidator.Version ? RecoveryValidator.Fingerprint(predecessor) : null };
    private static RecoveryAudit Promoted8(int earliest, bool unsafeDot = false)
    {
        var current = Rule8(unsafeDot);
        var old = Accepted(current.Document, current.Result with { Metadata = current.Result.Metadata with { ValidatorVersion = earliest } });
        return current with { PreviousAcceptance = old.Acceptance };
    }

    [Theory]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void ExactOriginal8PromotionPreservesOriginalObjectsAndEarliestConsent(int earliest)
    {
        var old = Promoted8(earliest); var before = JsonSerializer.SerializeToUtf8Bytes(old);
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.Reusable(old));
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(current with { CurrentCertification = null }));
        Assert.Equal(old.Acceptance, current.Acceptance); Assert.Equal(old.PreviousAcceptance, current.PreviousAcceptance);
        Assert.Equal(old.Acceptance.AcceptedAt, current.Acceptance.AcceptedAt);
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.False(RecoveryValidator.CanReuse(current.Acceptance, current.Document, current.Result));
    }

    [Theory]
    [InlineData("hash")] [InlineData("consent")] [InlineData("version")] [InlineData("dot")]
    public void InvalidOrAmbiguousOriginal8PromotionCannotRevalidate(string mutation)
    {
        var old = Promoted8(4, unsafeDot: mutation == "dot");
        old = mutation switch {
            "hash" => old with { PreviousAcceptance = old.PreviousAcceptance! with { ResultHash = "bad" } },
            "consent" => old with { PreviousAcceptance = old.PreviousAcceptance! with { AcceptedAt = old.Acceptance.AcceptedAt.AddMinutes(1) } },
            "version" => old with { PreviousAcceptance = old.PreviousAcceptance! with { Metadata = old.PreviousAcceptance.Metadata with { ValidatorVersion = 8 } } },
            _ => old };
        var before = JsonSerializer.SerializeToUtf8Bytes(old);
        Assert.Null(RecoveryAuditCertification.Reusable(old));
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(old));
        Assert.False(RecoveryValidator.ValidateCertifiedAudit(old.Document, old.Result, old.Acceptance, old.PreviousAcceptance, Certificate(old)).CanAdopt);
    }

    [Theory]
    [InlineData("orphan")] [InlineData("stacked")] [InlineData("hash")] [InlineData("priorHash")] [InlineData("ordinary9")]
    public void CertificatesCannotStackOrMasqueradeAsOrdinaryCurrentAcceptance(string mutation)
    {
        var old = RecoveryManualCertificationTests.Historical(true);
        var prior = Certificate(old, 8);
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.Reusable(old with { CurrentCertification = prior }));
        var bad = mutation switch {
            "orphan" => current with { CurrentCertification = null },
            "stacked" => current with { PreviousCertification = current.CurrentCertification },
            "hash" => current with { CurrentCertification = current.CurrentCertification! with { PreviousCertificationHash = "bad" } },
            "priorHash" => current with { PreviousCertification = prior with { PreviousCertificationHash = RecoveryValidator.Fingerprint(null as RecoverySemanticCertification) } },
            _ => Accepted(Rule8().Document, Rule8().Result with { Metadata = Rule8().Result.Metadata with { ValidatorVersion = 9 } }) with { CurrentCertification = prior } };
        Assert.False(RecoveryAuditCertification.IsCurrent(bad));
        Assert.Null(RecoveryAuditCertification.Reusable(bad));
    }

    [Fact]
    public void OldManual7DotCannotBeCertifiedEvenWithMatchingOriginalReceiptAndCertificate8()
    {
        // Independent fictional historical construction, not mutation of a real audit.
        var original = RecoveryManualCertificationTests.Historical(true);
        var sources = original.Document.Sources.Select(s => s.Text == "架空師B" ? s with { Text = "架空師·" } : s).ToArray();
        Assert.Contains(sources, s => s.FromOcr && s.Text.Contains('·'));
        var doc = original.Document with { Sources = sources, Capture = original.Document.Capture! with { SourceSnapshot = RecoveryValidator.Fingerprint(sources) } };
        var correction = original.Result.HumanCorrections![0] with { DocumentSnapshot = RecoveryValidator.Fingerprint(doc), AcquisitionHash = RecoveryValidator.Fingerprint(doc.Capture) };
        var result = original.Result with { HumanCorrections = [correction], Cells = original.Result.Cells.Select(c => c with {
            Lessons = c.Lessons.Select(l => l with { Teacher = l.Teacher with { Value = string.Concat(l.Teacher.Evidence.Select(id => sources.Single(s => s.Id == id).Text)) } }).ToArray()
        }).ToArray() };
        var old = Accepted(doc, result);
        var cert8 = Certificate(old, 8);
        Assert.Contains("ocrSeparatorAmbiguity", RecoveryValidator.ValidateCertifiedAudit(doc, result, old.Acceptance, null, Certificate(old)).Errors);
        Assert.Null(RecoveryAuditCertification.Reusable(old));
        Assert.Null(RecoveryAuditCertification.Reusable(old with { CurrentCertification = cert8 }));
        Assert.Null(RecoveryManualAssistance.Prepare(doc));
        Assert.All(result.Cells.SelectMany(c => c.Lessons).Where(l => l.Teacher.Value.Contains('·')), l => Assert.Equal(RecoveryValueState.Present, l.Teacher.State));
    }

    [Fact]
    public void OrdinaryValidationCannotUseHistoricalEnvelopeToAdmitOldMetadata()
    {
        var old = Rule8();
        Assert.True(RecoveryValidator.HistoricalAcceptanceEnvelopeValid(old.Document, old.Result, old.Acceptance, null));
        Assert.False(RecoveryValidator.Validate(old.Document, old.Result).CanAdopt);
        Assert.False(RecoveryValidator.CanReuse(old.Acceptance, old.Document, old.Result));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => RecoveryAuditCertification.Reusable(old, cancellation.Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SafeManual8PreservesOriginalCaptureCorrectionsAndMetadata(bool structure)
    {
        // Define a separately fictional original8 receipt before certification.
        var seed = RecoveryManualCertificationTests.Historical(structure);
        var doc = seed.Document with { StructureMetadata = seed.Document.StructureMetadata is { } m ? m with { ValidatorVersion = 8 } : null };
        var result = seed.Result with { Metadata = seed.Result.Metadata with { ValidatorVersion = 8 },
            HumanCorrections = seed.Result.HumanCorrections!.Select(c => c with { DocumentSnapshot = RecoveryValidator.Fingerprint(doc) }).ToArray() };
        var old = Accepted(doc, result); var before = JsonSerializer.SerializeToUtf8Bytes(old);
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.Reusable(old));
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(current with { CurrentCertification = null }));
        Assert.Null(current.PreviousCertification);
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.False(RecoveryValidator.Validate(current.Document, current.Result).CanAdopt);
    }
}
