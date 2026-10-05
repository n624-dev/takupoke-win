using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryManualAssistanceTests
{
    internal static RecoveryDocument Captured(int count, string mode = "wholeformal")
    {
        var doc = RecoveryParallelProofTests.Build(RecoveryParallelProofTests.TwoPages(mode));
        var printed = doc.Cells.Where(c => c.LessonBindings.Count > 0).GroupBy(c => c.Page).Select(g => g.First()).ToArray();
        var ids = printed.SelectMany(c => c.LessonBindings.SelectMany(b => new[] { b.Subject, b.Teacher, b.Room })).Take(count).Select(a => a[0]).ToHashSet();
        var sources = doc.Sources.Select(s => s with { FromOcr = true, NativeConfidence = ids.Contains(s.Id) ? .4 : .95 }).ToArray();
        doc = doc with { Sources = sources, Capture = new(doc.PdfHash, 2, RecoveryValidator.Fingerprint(sources),
            Enumerable.Range(1, 2).Select(p => new RecoveryCapturedPage(p, 3740, 800, new string('c', 64), sources.Count(s => s.Page == p), true)).ToArray()) };
        var targets = RecoveryManualAssistance.CaptureTargets(doc);
        if (targets is { Count: > 0 and <= 3 })
        {
            // Independently fictional pixels: this tests receipt integrity, not OCR/capture quality.
            var crops = targets.Select(t => {
                var x = (int)Math.Floor(t.Crop.X); var y = (int)Math.Floor(t.Crop.Y);
                var w = (int)Math.Ceiling(t.Crop.X + t.Crop.Width) - x; var h = (int)Math.Ceiling(t.Crop.Y + t.Crop.Height) - y;
                var bytes = Enumerable.Repeat((byte)73, w * h * 4).ToArray();
                return new RecoveryOriginalCrop(t.Target, t.Page, x, y, w, h, new string('c', 64), bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            }).ToArray();
            doc = doc with { Capture = doc.Capture! with { OriginalCrops = crops } };
        }
        return doc;
    }
    internal static RecoveryHumanCorrection Correction(RecoveryManualPlan plan, RecoveryManualTarget target, string value) =>
        new(target.Target, plan.Document.PdfHash, RecoveryValidator.Fingerprint(plan.Document.Capture), plan.DocumentSnapshot,
            target.OriginalParentIds, target.Page, target.Crop, value, false, DateTimeOffset.Parse("2032-04-01T00:00:00Z"));

    [Fact]
    public void IndependentOriginalV6ResultJsonAndRootHashesStayExactWithoutManualFields()
    {
        // Frozen using the unchanged 5c V6 Core/Infrastructure binaries before this assertion.
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "recovery-historical-v6-result.json"));
        var result = JsonSerializer.Deserialize<RecoveryResult>(bytes)!;
        Assert.Null(result.HumanCorrections); Assert.Equal(6, result.Metadata.ValidatorVersion);
        Assert.Equal(bytes, JsonSerializer.SerializeToUtf8Bytes(result));
        Assert.Equal("9d3af29958fbb68f1bd59c7b71240d597edc4e91825809fbfc364b4dfde3ac42", RecoveryValidator.Fingerprint(result));
        Assert.Equal("f2341e11365a139896dff965bfb3f00779a74984d3cd41d7956d29d4ea42dd91", Convert.ToHexStringLower(SHA256.HashData(DataCodec.Encode(result))));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void OneAndThreeSourceOwnedFieldsRequireHumanProvenanceAndPreserveOriginalOcr(int count)
    {
        var doc = Captured(count); var before = JsonSerializer.SerializeToUtf8Bytes(doc);
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc));
        Assert.Equal(count, plan.Targets.Count);
        var corrections = plan.Targets.Select((t, i) => Correction(plan, t, "架空手入力" + i)).ToArray();
        var result = RecoveryManualAssistance.Complete(plan, corrections, "host-control");
        Assert.True(RecoveryValidator.Validate(doc, result).CanAdopt);
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(doc));
        Assert.Equal(corrections, result.HumanCorrections);
        Assert.All(result.HumanCorrections!, c => Assert.Equal("user", c.Provenance));
        var raw = result with { HumanCorrections = null };
        Assert.False(RecoveryValidator.Validate(doc, raw).CanAdopt);
        var now = DateTimeOffset.Parse("2032-04-01T00:00:00Z");
        var source = new SourceRecord("fictional", Takupoke.Core.MaterialKind.Timetable, "fictional.pdf", "fictional", "fictional", doc.PdfHash, 0, now, now, null);
        var formal = RecoveryAnalysisConverter.Convert(source, doc, result, now);
        Assert.Contains(formal.Timetable!.Lessons, l => l.Names.Subject == "架空手入力0");
    }
    [Fact]
    public void FourFieldsAcrossTwoPagesFailBeforeQuestionsAndCannotBeGrouped()
    {
        var doc = Captured(4);
        Assert.Equal(2, doc.Sources.Where(s => s.NativeConfidence < .8).Select(s => s.Page).Distinct().Count());
        Assert.Null(RecoveryManualAssistance.Prepare(doc));
    }
    [Fact]
    public void ParallelPositionsAreSeparateTargetsAndMergedSlotsRetainBothTuples()
    {
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(Captured(3, "parallelMerged")));
        Assert.Equal(3, plan.Targets.Count); Assert.Single(plan.Targets.Select(t => t.Target.CellId).Distinct());
        var result = RecoveryManualAssistance.Complete(plan, plan.Targets.Select(t => Correction(plan,t,"架空訂正" + t.Target.Role)).ToArray(), "host-control");
        var cell = plan.Document.Cells.Single(c => c.Id == plan.Targets[0].Target.CellId);
        Assert.Equal(new[]{1,2}, cell.Slots.Select(s => s.Period));
        var recovered = result.Cells.Single(c => c.CellId == cell.Id);
        Assert.Equal(2, recovered.Lessons.Count); Assert.Equal("架空科D",recovered.Lessons[1].Subject.Value);
        var now = DateTimeOffset.Parse("2032-04-01T00:00:00Z");
        var source = new SourceRecord("fictional", Takupoke.Core.MaterialKind.Timetable,"fictional.pdf","fictional","fictional",plan.Document.PdfHash,0,now,now,null);
        var formal = RecoveryAnalysisConverter.Convert(source,plan.Document,result,now);
        foreach(var period in new[]{1,2}) Assert.Equal(2,formal.Timetable!.Lessons.Count(l => l.ClassName == "1_2" && l.Weekday == 1 && l.Period == period));
        var four = Captured(4,"parallel");
        Assert.Single(four.Sources.Where(s => s.NativeConfidence < .8).Select(s => s.CellId).Distinct());
        Assert.Null(RecoveryManualAssistance.Prepare(four)); // One cell may contain six independent semantic targets.
    }
    [Theory]
    [InlineData("capture")]
    [InlineData("page")]
    [InlineData("coverage")]
    [InlineData("incomplete")]
    [InlineData("confidence")]
    [InlineData("header")]
    [InlineData("snapshot")]
    public void IncompleteUnknownOrStructurallyUnresolvedAcquisitionCannotExposeFewQuestions(string fault)
    {
        var doc = Captured(1);
        doc = fault switch {
            "capture" => doc with { Capture = null },
            "page" => doc with { Capture = doc.Capture! with { Pages = doc.Capture.Pages.Take(1).ToArray() } },
            "coverage" => doc with { Capture = doc.Capture! with { Pages = doc.Capture.Pages.Select(p => p with { InkCoverageVerified = false }).ToArray() } },
            "incomplete" => doc with { Complete = false },
            "snapshot" => doc with { Capture = doc.Capture! with { SourceSnapshot = new string('b', 64) } },
            _ => doc with { Sources = doc.Sources.Select((s, i) => i == 0 ? s with { NativeConfidence = fault == "header" ? .2 : null } : s).ToArray() }
        };
        if (fault is "header" or "confidence") doc = doc with { Capture = doc.Capture! with { SourceSnapshot = RecoveryValidator.Fingerprint(doc.Sources) } };
        Assert.Null(RecoveryManualAssistance.Prepare(doc));
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("hash")]
    [InlineData("outside")]
    [InlineData("raster")]
    [InlineData("target")]
    public void OriginalPixelCropsMustMatchCapturedGeometryAndImmutableRasterIdentity(string fault)
    {
        var doc = Captured(1); var crop = doc.Capture!.OriginalCrops![0];
        var broken = fault switch {
            "hash" => crop with { Bgra = crop.Bgra.Select(b => (byte)(b + 1)).ToArray() },
            "outside" => crop with { PixelX = -1 },
            "raster" => crop with { OriginalRasterHash = new string('a', 64) },
            _ => crop with { Target = crop.Target with { Role = RecoveryFieldRole.Room } }
        };
        doc = doc with { Capture = doc.Capture with { OriginalCrops = fault == "missing" ? null : [broken] } };
        Assert.Null(RecoveryManualAssistance.Prepare(doc));
    }
    [Fact]
    public void MissingPixelPayloadInMalformedPersistedCaptureRefusesInsteadOfCrashing()
    {
        var original = Captured(1); var plan = RecoveryManualAssistance.Prepare(original)!;
        var crop = original.Capture!.OriginalCrops![0] with { Bgra = null! };
        var broken = original with { Capture = original.Capture with { OriginalCrops = [crop] } };
        Assert.Null(RecoveryManualAssistance.Prepare(broken));
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan with { Document = broken }, [Correction(plan,plan.Targets[0],"架空訂正文")],"host-control"));
    }
    [Theory]
    [InlineData("box")]
    [InlineData("role")]
    [InlineData("pdf")]
    [InlineData("snapshot")]
    [InlineData("acquisition")]
    [InlineData("parents")]
    [InlineData("value")]
    [InlineData("blank")]
    [InlineData("provenance")]
    public void CorrectionsCannotChangeCropRoleOriginalEvidenceOrConsentIdentity(string fault)
    {
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(Captured(1)));
        var c = Correction(plan, plan.Targets[0], "架空手入力");
        c = fault switch {
            "box" => c with { Crop = c.Crop with { X = c.Crop.X + 1 } },
            "role" => c with { Target = c.Target with { Role = RecoveryFieldRole.Room } },
            "pdf" => c with { PdfHash = new string('b', 64) },
            "snapshot" => c with { DocumentSnapshot = new string('b', 64) },
            "acquisition" => c with { AcquisitionHash = new string('b', 64) },
            "parents" => c with { OriginalParentIds = [] },
            "value" => c with { CorrectedText = "" },
            "blank" => c with { CorrectedText = "", ExplicitBlank = true },
            _ => c with { Provenance = "ai" }
        };
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan, [c], "host-control"));
    }
    [Fact]
    public void EveryUnresolvedFieldMustBeCorrectedAndCancellationNeverProducesAResult()
    {
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(Captured(3)));
        var corrections = plan.Targets.Select(t => Correction(plan, t, "架空手入力")).ToArray();
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan, corrections.Take(2).ToArray(), "host-control"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => RecoveryManualAssistance.Prepare(plan.Document, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => RecoveryManualAssistance.Complete(plan, corrections, "host-control", cancellation.Token));
    }
}
