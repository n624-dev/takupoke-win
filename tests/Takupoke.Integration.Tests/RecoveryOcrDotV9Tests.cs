using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

// Declared fictional layouts and pure rules only. No recognizer/model/raw replay.
public sealed class RecoveryOcrDotV9Tests
{
    private static PdfPageLayout Layout(string marker)
    {
        var page = RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        return page with { Glyphs = page.Glyphs.Select(g => g.Text == "架空科目A" ? g with { Text = marker } : g).ToArray() };
    }
    private static RecoveryDocument Build(PdfPageLayout page, bool ocr = false, bool proposal = false) =>
        RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page],
            (_, box) => !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))),
            ocrPages: ocr ? new HashSet<int> { 1 } : null, allowStructureProposal: proposal);
    private static RecoveryAudit Audit(string marker = "架空科目A", bool ocr = false, int version = 8)
    {
        var doc = Build(Layout(marker));
        if (ocr) doc = doc with { Sources = doc.Sources.Select(s => s with { FromOcr = true }).ToArray() };
        var result = new RecoveryResult(doc.PdfHash, doc.Kind, doc.SchoolYear, doc.Term,
            doc.Cells.Select(c => c.ConfirmedEmpty ? new RecoveredCell(c.Id, RecoveryValueState.Empty, []) : RecoveryRules.Recover(doc, c)!).ToArray(), new("rule", "rules", "3", "3", "1", 2, version, "test"));
        return new(doc, result, new(doc.PdfHash, RecoveryValidator.Fingerprint(result),
            RecoveryValidator.Fingerprint(doc), result.Metadata, DateTimeOffset.Parse("2032-04-01T00:00:00Z")));
    }
    private static object? PreviousCertificate(RecoveryAudit audit) =>
        typeof(RecoveryAudit).GetProperty("PreviousCertification")?.GetValue(audit);

    [Theory]
    [InlineData("·", false)]
    [InlineData("架空甲·架空乙", false)]
    [InlineData("架空甲·架空乙", true)]
    [InlineData("架空甲・架空乙·架空丙", true)]
    public void KnownOcrBodyDotRefusesBeforeFixedOrProviderFallback(string marker, bool proposal)
    {
        var page = Layout(marker); var before = JsonSerializer.SerializeToUtf8Bytes(page);
        var error = Assert.Throws<InvalidDataException>(() => Build(page, ocr: true, proposal));
        Assert.Contains("区切り文字", error.Message);
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(page));
    }

    [Theory]
    [InlineData("·")]
    [InlineData("架空甲·架空乙")]
    public void CoreAndCachedOld8CannotBypassKnownOcrDotGate(string marker)
    {
        var old = Audit(marker, ocr: true);
        var currentResult = old.Result with { Metadata = old.Result.Metadata with { ValidatorVersion = RecoveryValidator.Version } };
        Assert.Contains("ocrSeparatorAmbiguity", RecoveryValidator.Validate(old.Document, currentResult).Errors);
        Assert.Null(RecoveryAuditCertification.Reusable(old));
        Assert.Null(RecoveryAuditCertification.TryRecertify(old));
    }

    [Theory]
    [InlineData("架空甲·架空乙")]
    [InlineData("架空甲・架空乙")]
    public void VectorCompoundNameRetainsOriginalEvidence(string marker)
    {
        var old = Audit(marker, version: RecoveryValidator.Version);
        Assert.True(RecoveryValidator.Validate(old.Document, old.Result).CanAdopt);
        var field = Assert.Single(old.Result.Cells.SelectMany(c => c.Lessons)).Subject;
        Assert.Equal(marker, field.Value);
        var sources = old.Document.Sources.ToDictionary(s => s.Id);
        Assert.Equal(marker, string.Concat(field.Evidence.Select(id => sources[id].Text)));
    }

    [Fact]
    public void SafeOld8RevalidatesWithoutRewritingAnyOriginalObject()
    {
        var old = Audit();
        Assert.False(RecoveryValidator.CanReuse(old.Acceptance, old.Document, old.Result));
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.Reusable(old));
        Assert.Equal(9, current.CurrentCertification!.ValidatorVersion);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(old.Document), JsonSerializer.SerializeToUtf8Bytes(current.Document));
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(old.Result), JsonSerializer.SerializeToUtf8Bytes(current.Result));
        Assert.Equal(old.Acceptance, current.Acceptance);
        Assert.Equal(old.PreviousAcceptance, current.PreviousAcceptance);
        Assert.Equal(RecoveryValidator.Fingerprint(old), RecoveryValidator.Fingerprint(current with { CurrentCertification = null }));
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.Equal(RecoveryValidator.Fingerprint(current), RecoveryValidator.Fingerprint(RecoveryAuditCertification.Reusable(current)));
    }

    [Fact]
    public void GenuineManual7Certificate8SurvivesCurrent9Certification()
    {
        var old = RecoveryManualCertificationTests.Historical(true);
        var certificate8 = new RecoverySemanticCertification(2, 8, old.Acceptance.ScopeHash, old.Acceptance.ResultHash,
            RecoveryValidator.Fingerprint(old.Acceptance), RecoveryValidator.Fingerprint(old.PreviousAcceptance));
        var certified8 = old with { CurrentCertification = certificate8 };
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.Reusable(certified8));
        Assert.Equal(9, current.CurrentCertification!.ValidatorVersion);
        Assert.Equal(certificate8, PreviousCertificate(current));
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(old.Document), JsonSerializer.SerializeToUtf8Bytes(current.Document));
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(old.Result), JsonSerializer.SerializeToUtf8Bytes(current.Result));
        Assert.Equal(old.Acceptance, current.Acceptance);
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("result")]
    [InlineData("acceptance")]
    [InlineData("previous")]
    [InlineData("version")]
    public void ForgedManual7Certificate8CannotBePromoted(string mutation)
    {
        var old = RecoveryManualCertificationTests.Historical(false);
        var c = new RecoverySemanticCertification(2, 8, old.Acceptance.ScopeHash, old.Acceptance.ResultHash,
            RecoveryValidator.Fingerprint(old.Acceptance), RecoveryValidator.Fingerprint(old.PreviousAcceptance));
        c = mutation switch { "scope" => c with { ScopeHash = "bad" }, "result" => c with { ResultHash = "bad" },
            "acceptance" => c with { AcceptanceHash = "bad" }, "previous" => c with { PreviousAcceptanceHash = "bad" },
            _ => c with { ValidatorVersion = 7 } };
        Assert.Null(RecoveryAuditCertification.Reusable(old with { CurrentCertification = c }));
    }

    [Fact]
    public void GoldenLegacyCertificateAndAuditOmitNewNullFields()
    {
        var certificate = new RecoverySemanticCertification(2, 8, "s", "r", "a", "p");
        Assert.Equal("{\"RecoverySchemaVersion\":2,\"ValidatorVersion\":8,\"ScopeHash\":\"s\",\"ResultHash\":\"r\",\"AcceptanceHash\":\"a\",\"PreviousAcceptanceHash\":\"p\"}", JsonSerializer.Serialize(certificate));
        var old = Audit();
        Assert.DoesNotContain("PreviousCertification", JsonSerializer.Serialize(old));
        Assert.Equal("4412a6c89b8b8e09512674bbf51e568deb392cfb8b094f31c68068fd61712499", RecoveryValidator.Fingerprint(old));
    }
}
