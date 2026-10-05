using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryInlineParallelCacheTests
{
    private static void AssertOriginalAuditPreserved(RecoveryAudit original, RecoveryAudit certified)
    {
        Assert.Equal(RecoveryValidator.Fingerprint(original.Document), RecoveryValidator.Fingerprint(certified.Document));
        Assert.Equal(RecoveryValidator.Fingerprint(original.Result), RecoveryValidator.Fingerprint(certified.Result));
        Assert.Equal(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original.Document), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(certified.Document));
        Assert.Equal(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original.Result), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(certified.Result));
        Assert.Equal(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original.Acceptance), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(certified.Acceptance));
        Assert.Equal(original.PreviousAcceptance, certified.PreviousAcceptance);
        Assert.Equal(original.Acceptance.AcceptedAt, certified.Acceptance.AcceptedAt);
        Assert.Equal(RecoveryValidator.Version, certified.CurrentCertification!.ValidatorVersion);
        Assert.Equal(RecoveryValidator.Fingerprint(original.Acceptance), certified.CurrentCertification.AcceptanceHash);
        Assert.Equal(RecoveryValidator.Fingerprint(original.PreviousAcceptance), certified.CurrentCertification.PreviousAcceptanceHash);
        Assert.Equal(RecoveryValidator.Fingerprint(null as RecoverySemanticCertification), certified.CurrentCertification.PreviousCertificationHash);
    }

    [Fact]
    public async Task AlteredPreviousAcceptanceCannotPassCurrentReuseDisplayOrStoreDespiteValidCurrentHashes()
    {
        await using var context = await Context.CreateAsync();
        var first = context.Audit(compound: false, version: 4);
        var promotedMetadata = first.Result.Metadata with { ValidatorVersion = 8 };
        var promotedResult = first.Result with { Metadata = promotedMetadata };
        var old = new RecoveryAudit(first.Document, promotedResult, new(first.Document.PdfHash,
            RecoveryValidator.Fingerprint(promotedResult), RecoveryValidator.Fingerprint(first.Document), promotedMetadata, first.Acceptance.AcceptedAt))
        { PreviousAcceptance = first.Acceptance };
        var certified = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.TryRecertify(old));
        var bad = certified with { PreviousAcceptance = certified.PreviousAcceptance! with { ScopeHash = new string('b', 64) } };
        Assert.False(RecoveryValidator.CanReuse(bad.Acceptance, bad.Document, bad.Result));
        Assert.Null(RecoveryAuditCertification.Reusable(bad));
        var formal = RecoveryAnalysisConverter.ConvertCertified(context.Source, certified, context.Clock.GetUtcNow()) with { Recovery = bad };
        Assert.False(RecoveryAnalysisConverter.MayDisplay(formal));
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, bad);
        await Assert.ThrowsAsync<InvalidDataException>(() => context.Store.SaveRecoveryAsync(context.Lease, context.Source,
            bad, context.Clock.GetUtcNow(), reuseAccepted: true));

        var prepared = await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026);
        Assert.False(prepared.ReusedAcceptance); Assert.Equal(RecoveryJobState.AwaitingConfirmation, prepared.State);
        Assert.Equal(1, context.BuildCalls); await context.AssertPriorFormalPreservedAsync();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task CachedInlineCompoundAuditIsNotReusedAndReachesTheActualBuilder(int version)
    {
        await using var context = await Context.CreateAsync();
        var bad = context.Audit(compound: true, version);
        AssertInlineCompoundInvalid(bad);
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, bad);

        var prepared = await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026);

        Assert.Equal(RecoveryJobState.AwaitingConfirmation, prepared.State);
        Assert.False(prepared.ReusedAcceptance);
        Assert.Equal(1, context.BuildCalls);
        Assert.Equal(1, context.ProviderFactoryCalls); // The factory returns no providers/models.
        Assert.Equal(RecoveryValidator.Version, prepared.Preview!.Result.Metadata.ValidatorVersion);
        Assert.Equal("架空科目A", Assert.Single(prepared.Preview.Result.Cells.SelectMany(c => c.Lessons)).Subject.Value);
        await context.AssertPriorFormalPreservedAsync();
        Assert.Equal(RecoveryValidator.Fingerprint(bad), RecoveryValidator.Fingerprint((await context.Store.ReadAsync<RecoveryAudit>(context.Lease, context.AcceptedKey))!));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task OldPersistedInlineCompoundPreviewCannotBeAdoptedEvenWithCurrentVersion(int version)
    {
        await using var context = await Context.CreateAsync();
        var bad = context.Audit(compound: true, version);
        AssertInlineCompoundInvalid(bad);
        var preview = new RecoveryPreview(context.Source.Id, context.Lease, bad.Document, bad.Result, context.Clock.GetUtcNow());
        // WriteAsync reproduces a preview already persisted by the old app;
        // SaveRecoveryProgress would correctly reject it in the current app.
        await context.Store.WriteAsync(context.Lease, "recovery.preview.Timetable", preview);
        await context.Store.WriteAsync(context.Lease, "recovery.Timetable", new RecoveryJob(context.Source.Digest,
            bad.Document.Kind, RecoveryJobState.AwaitingConfirmation, context.Clock.GetUtcNow(), RecoveryValidator.Fingerprint(bad.Result)));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => context.Coordinator.AdoptAsync(MaterialKind.Timetable, preview));

        Assert.Contains("復旧結果と確認内容", error.Message); // SaveRecovery's current validation gate, not an unrelated job/lease mismatch.
        Assert.Equal(0, context.BuildCalls);
        Assert.Equal(0, context.ProviderFactoryCalls);
        await context.AssertPriorFormalPreservedAsync();
        Assert.NotNull(await context.Store.ReadAsync<RecoveryPreview>(context.Lease, "recovery.preview.Timetable"));
        Assert.Null(await context.Store.ReadAsync<RecoveryAudit>(context.Lease, context.AcceptedKey));
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    [InlineData(false, 7)]
    [InlineData(true, 7)]
    public async Task PreviouslyAcceptedGoodAuditIsRecertifiedAndAutomaticallyReusedWithoutAnotherConfirmation(bool structureMetadata, int version)
    {
        await using var context = await Context.CreateAsync();
        var old = context.Audit(compound: false, version: version);
        if (structureMetadata)
        {
            var originalDocument = old.Document with { StructureMetadata = old.Result.Metadata };
            old = old with { Document = originalDocument, Acceptance = old.Acceptance with { ScopeHash = RecoveryValidator.Fingerprint(originalDocument) } };
        }
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, old);

        var prepared = await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026);

        Assert.Equal(RecoveryJobState.Adopted, prepared.State);
        Assert.True(prepared.ReusedAcceptance);
        Assert.Null(prepared.Preview);
        Assert.Equal(0, context.BuildCalls);
        Assert.Equal(0, context.ProviderFactoryCalls);
        Assert.Null(await context.Store.ReadAsync<RecoveryPreview>(context.Lease, "recovery.preview.Timetable"));
        Assert.Null(await context.Store.ReadAsync<RecoveryJob>(context.Lease, "recovery.Timetable"));
        var current = Assert.IsType<RecoveryAudit>(await context.Store.ReadAsync<RecoveryAudit>(context.Lease, context.AcceptedKey));
        AssertOriginalAuditPreserved(old, current);
        Assert.False(RecoveryValidator.CanReuse(current.Acceptance, current.Document, current.Result));
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.Equal(RecoveryValidator.Fingerprint(old.Document with { StructureMetadata = null }), RecoveryValidator.Fingerprint(current.Document with { StructureMetadata = null }));
        Assert.Equal(RecoveryValidator.Fingerprint(old.Result.Cells), RecoveryValidator.Fingerprint(current.Result.Cells));
        var formal = Assert.IsType<MaterialAnalysis>(await context.Store.ReadAsync<MaterialAnalysis>(context.Lease, "analysis.Timetable"));
        Assert.Equal(25, formal.ParserVersion);
        Assert.Equal("架空科目A", Assert.Single(formal.Timetable!.Lessons).Names.Subject);
        Assert.NotNull(formal.Recovery);
        Assert.Equal(current.Acceptance, formal.Recovery!.Acceptance);
        var reusedAgain = await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026);
        Assert.Equal(RecoveryJobState.Adopted, reusedAgain.State); Assert.True(reusedAgain.ReusedAcceptance); Assert.Null(reusedAgain.Preview);
        Assert.Equal(0, context.BuildCalls); Assert.Equal(0, context.ProviderFactoryCalls);
        Assert.Equal(RecoveryValidator.Fingerprint(current), RecoveryValidator.Fingerprint((await context.Store.ReadAsync<RecoveryAudit>(context.Lease, context.AcceptedKey))!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactV4ToV5ChainPreservesFirstConsentThroughV6AndRepeatedReuse(bool corruptPrevious)
    {
        await using var context = await Context.CreateAsync();
        var first = context.Audit(compound: false, version: 4);
        var metadata = first.Result.Metadata with { ValidatorVersion = 5 };
        var result = first.Result with { Metadata = metadata };
        var v5 = new RecoveryAudit(first.Document, result, new(first.Document.PdfHash,
            RecoveryValidator.Fingerprint(result), RecoveryValidator.Fingerprint(first.Document), metadata, first.Acceptance.AcceptedAt)) {
            PreviousAcceptance = corruptPrevious ? first.Acceptance with { ScopeHash = new string('b',64) } : first.Acceptance
        };
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, v5);
        var prepared = await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026);
        if (corruptPrevious)
        {
            Assert.False(prepared.ReusedAcceptance); Assert.Equal(1,context.BuildCalls);
            await context.AssertPriorFormalPreservedAsync();
            Assert.Equal(RecoveryValidator.Fingerprint(v5),RecoveryValidator.Fingerprint((await context.Store.ReadAsync<RecoveryAudit>(context.Lease,context.AcceptedKey))!));
            return;
        }
        Assert.True(prepared.ReusedAcceptance); Assert.Equal(0,context.BuildCalls);
        var current = (await context.Store.ReadAsync<RecoveryAudit>(context.Lease, context.AcceptedKey))!;
        AssertOriginalAuditPreserved(v5, current);
        Assert.Equal(first.Acceptance,current.PreviousAcceptance); Assert.Equal(first.Acceptance.AcceptedAt,current.Acceptance.AcceptedAt);
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.True((await context.Coordinator.PrepareAsync(MaterialKind.Timetable,2026)).ReusedAcceptance);
        Assert.Equal(0,context.BuildCalls);
    }
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task UnchangedNonparallelOcrAuditNeedsOldHashesAndCurrentSemanticRevalidation(int version)
    {
        await using var context = await Context.CreateAsync();
        var old = context.Audit(compound: false, version);
        var document = old.Document with { Sources = old.Document.Sources.Select(s=>s with { FromOcr = true }).ToArray() };
        old = old with { Document = document, Acceptance = old.Acceptance with { ScopeHash = RecoveryValidator.Fingerprint(document) } };
        await context.Store.WriteAsync(context.Lease,context.AcceptedKey,old);
        var prepared = await context.Coordinator.PrepareAsync(MaterialKind.Timetable,2026);
        Assert.True(prepared.ReusedAcceptance); Assert.Equal(0,context.BuildCalls);
        var current = (await context.Store.ReadAsync<RecoveryAudit>(context.Lease,context.AcceptedKey))!;
        AssertOriginalAuditPreserved(old, current);
        Assert.Equal(RecoveryValidator.Fingerprint(old.Document),RecoveryValidator.Fingerprint(current.Document));
        Assert.All(current.Document.Cells,c=>Assert.Null(c.ParallelSeparators));
        Assert.True(RecoveryAnalysisConverter.MayDisplay((await context.Store.ReadAsync<MaterialAnalysis>(context.Lease,"analysis.Timetable"))!));
    }
    [Fact]
    public async Task ValidV5DistinctFieldAndStructureHistoriesRemainIndependentThroughV6()
    {
        await using var context=await Context.CreateAsync();var old=context.Audit(compound:false,version:5);
        var structure=old.Result.Metadata with { Provider="fictional-structure",ModelId="fictional-structure-model",PromptVersion="3" };
        var document=old.Document with { StructureMetadata=structure };
        old=old with { Document=document,Acceptance=old.Acceptance with { ScopeHash=RecoveryValidator.Fingerprint(document) } };
        await context.Store.WriteAsync(context.Lease,context.AcceptedKey,old);
        Assert.True((await context.Coordinator.PrepareAsync(MaterialKind.Timetable,2026)).ReusedAcceptance);
        var current=(await context.Store.ReadAsync<RecoveryAudit>(context.Lease,context.AcceptedKey))!;
        AssertOriginalAuditPreserved(old, current);
        Assert.Equal(structure,current.Document.StructureMetadata);
        Assert.Equal(old.Result.Metadata,current.Result.Metadata);
        Assert.True(RecoveryAuditCertification.IsCurrent(current));Assert.Equal(0,context.BuildCalls);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ExactV6RecertifiedChainKeepsEarliestConsentThroughCurrentVersion(int earliest)
    {
        await using var context = await Context.CreateAsync();
        var first = context.Audit(compound: false, version: earliest);
        var metadata = first.Result.Metadata with { ValidatorVersion = 6 };
        var document = first.Document with { StructureMetadata = first.Document.StructureMetadata is { } structure ? structure with { ValidatorVersion = 6 } : null };
        var result = first.Result with { Metadata = metadata };
        var v6 = new RecoveryAudit(document, result, new(document.PdfHash, RecoveryValidator.Fingerprint(result),
            RecoveryValidator.Fingerprint(document), metadata, first.Acceptance.AcceptedAt)) { PreviousAcceptance = first.Acceptance };
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, v6);
        Assert.True((await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026)).ReusedAcceptance);
        var current = (await context.Store.ReadAsync<RecoveryAudit>(context.Lease, context.AcceptedKey))!;
        AssertOriginalAuditPreserved(v6, current);
        Assert.Equal(first.Acceptance, current.PreviousAcceptance);
        Assert.Equal(first.Acceptance.AcceptedAt, current.Acceptance.AcceptedAt);
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.True((await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026)).ReusedAcceptance);
        Assert.Equal(0, context.BuildCalls);
        var tampered = current with { PreviousAcceptance = current.PreviousAcceptance! with { ScopeHash = new string('b',64) } };
        Assert.Null(RecoveryAuditCertification.Reusable(tampered));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ExactV7ChainRetainsEarliestConsentAndRejectsChangedAncestry(int earliest)
    {
        await using var context = await Context.CreateAsync();
        var first = context.Audit(compound: false, version: earliest);
        var metadata = first.Result.Metadata with { ValidatorVersion = 7 };
        var result = first.Result with { Metadata = metadata };
        var old = new RecoveryAudit(first.Document, result, new(first.Document.PdfHash,
            RecoveryValidator.Fingerprint(result), RecoveryValidator.Fingerprint(first.Document), metadata, first.Acceptance.AcceptedAt))
            { PreviousAcceptance = first.Acceptance };
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.TryRecertify(old));
        Assert.Equal(first.Acceptance, current.PreviousAcceptance);
        Assert.Equal(first.Acceptance.AcceptedAt, current.Acceptance.AcceptedAt);
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        Assert.Null(RecoveryAuditCertification.Reusable(current with
            { PreviousAcceptance = current.PreviousAcceptance! with { AcceptedAt = first.Acceptance.AcceptedAt.AddMinutes(1) } }));
        Assert.Null(RecoveryAuditCertification.TryRecertify(old with
            { PreviousAcceptance = first.Acceptance with { ScopeHash = new string('b', 64) } }));
    }

    [Fact]
    public void V7ManualAuditPreservesOriginalCaptureSnapshotCorrectionsAndConsent()
    {
        var document = RecoveryManualAssistanceTests.Captured(1);
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(document));
        var correction = RecoveryManualAssistanceTests.Correction(plan, Assert.Single(plan.Targets), "架空訂正科目");
        var result = RecoveryManualAssistance.Complete(plan, [correction], "host-control");
        result = result with { Metadata = result.Metadata with { ValidatorVersion = 7 } };
        var acceptedAt = DateTimeOffset.Parse("2032-04-01T00:00:00Z");
        var old = new RecoveryAudit(document, result, new(document.PdfHash, RecoveryValidator.Fingerprint(result),
            RecoveryValidator.Fingerprint(document), result.Metadata, acceptedAt));
        var current = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.TryRecertify(old));
        Assert.Equal(RecoveryValidator.Fingerprint(old), RecoveryValidator.Fingerprint(current with { CurrentCertification = null }));
        Assert.Equal(acceptedAt, current.Acceptance.AcceptedAt);
        Assert.NotNull(current.CurrentCertification);
        Assert.Equal(RecoveryValidator.Fingerprint(document), RecoveryValidator.Fingerprint(current.Document));
        Assert.Equal(RecoveryValidator.Fingerprint(result.HumanCorrections), RecoveryValidator.Fingerprint(current.Result.HumanCorrections));
        Assert.True(RecoveryAuditCertification.IsCurrent(current));
        var broken = result with { HumanCorrections = [correction with { DocumentSnapshot = new string('b', 64) }] };
        Assert.Null(RecoveryAuditCertification.TryRecertify(old with { Result = broken,
            Acceptance = old.Acceptance with { ResultHash = RecoveryValidator.Fingerprint(broken) } }));
        Assert.Null(RecoveryAuditCertification.TryRecertify(old with { PreviousAcceptance = old.Acceptance with
            { Metadata = old.Acceptance.Metadata with { ValidatorVersion = 6 } } }));
    }

    [Fact]
    public async Task HistoricallyImpossibleNewSeparatorProofCannotUpgradeARehashedOldAudit()
    {
        await using var context = await Context.CreateAsync();
        var old = context.Audit(compound: false, version: 5);
        var document = old.Document with { Cells = old.Document.Cells.Select((c,i)=>i==0?c with { ParallelSeparators = new Dictionary<string,string>() }:c).ToArray() };
        old = old with { Document=document,Acceptance=old.Acceptance with { ScopeHash=RecoveryValidator.Fingerprint(document) } };
        Assert.Null(RecoveryAuditCertification.TryRecertify(old));
        await context.Store.WriteAsync(context.Lease,context.AcceptedKey,old);
        Assert.False((await context.Coordinator.PrepareAsync(MaterialKind.Timetable,2026)).ReusedAcceptance);
        await context.AssertPriorFormalPreservedAsync();
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("confidence")]
    [InlineData("human")]
    public async Task RehashedHistoricalV6CannotInventNewAcquisitionOrHumanProof(string injected)
    {
        await using var context = await Context.CreateAsync();
        var old = context.Audit(compound: false, version: 6);
        var document = old.Document; var result = old.Result;
        if (injected == "capture") document = document with { Capture = new(document.PdfHash, 1, RecoveryValidator.Fingerprint(document.Sources), []) };
        if (injected == "confidence") document = document with { Sources = document.Sources.Select((s,i) => i == 0 ? s with { NativeConfidence = .95 } : s).ToArray() };
        if (injected == "human") result = result with { HumanCorrections = [] };
        old = old with { Document = document, Result = result, Acceptance = old.Acceptance with {
            ScopeHash = RecoveryValidator.Fingerprint(document), ResultHash = RecoveryValidator.Fingerprint(result) } };
        Assert.Null(RecoveryAuditCertification.TryRecertify(old));
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, old);
        Assert.False((await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026)).ReusedAcceptance);
        await context.AssertPriorFormalPreservedAsync();
        Assert.Equal(RecoveryValidator.Fingerprint(old), RecoveryValidator.Fingerprint((await context.Store.ReadAsync<RecoveryAudit>(context.Lease,context.AcceptedKey))!));
    }

    [Fact]
    public async Task HistoricallyImpossibleDistinctVersion4MetadataCannotInventPriorConfirmation()
    {
        await using var context = await Context.CreateAsync();
        var old = context.Audit(compound: false, version: 4);
        var document = old.Document with { StructureMetadata = old.Result.Metadata with {
            Provider = "fictional-structure-provider", ModelId = "fictional-structure-model",
            ModelVersion = "2", RuntimeVersion = "fictional-structure-runtime", PromptVersion = "3"
        } };
        old = old with { Document = document, Acceptance = old.Acceptance with { ScopeHash = RecoveryValidator.Fingerprint(document) } };
        Assert.Null(RecoveryAuditCertification.TryRecertify(old));
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, old);

        var prepared = await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026);

        Assert.False(prepared.ReusedAcceptance);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, prepared.State);
        Assert.Equal(1, context.BuildCalls);
        Assert.Equal(1, context.ProviderFactoryCalls);
        await context.AssertPriorFormalPreservedAsync();
    }

    [Fact]
    public async Task ValidUpgradeCannotReuseAnAcceptanceChangedBeforeTheStorageTransaction()
    {
        await using var context = await Context.CreateAsync();
        var old = context.Audit(compound: false, version: 4);
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, old);
        var candidate = Assert.IsType<RecoveryAudit>(RecoveryAuditCertification.TryRecertify(old));
        Assert.False(RecoveryValidator.CanReuse(candidate.Acceptance, candidate.Document, candidate.Result));
        Assert.True(RecoveryAuditCertification.IsCurrent(candidate));
        // Deterministic handoff: certification already finished, then another
        // writer changes consent before SaveRecovery rereads under its transaction.
        var replaced = old with { Acceptance = old.Acceptance with { AcceptedAt = old.Acceptance.AcceptedAt.AddMinutes(1) } };
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, replaced);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => context.Store.SaveRecoveryAsync(context.Lease,
            context.Source, candidate, context.Clock.GetUtcNow(), reuseAccepted: true));

        Assert.Contains("以前の確認内容と一致", error.Message);
        await context.AssertPriorFormalPreservedAsync();
        Assert.Equal(RecoveryValidator.Fingerprint(replaced), RecoveryValidator.Fingerprint((await context.Store.ReadAsync<RecoveryAudit>(context.Lease, context.AcceptedKey))!));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("result")]
    [InlineData("scope")]
    [InlineData("metadata")]
    [InlineData("document")]
    [InlineData("output")]
    public async Task TamperedOldAcceptanceCannotBeRecertifiedOrBypassRebuilding(string field)
    {
        await using var context = await Context.CreateAsync();
        var old = context.Audit(compound: false, version: 4);
        var broken = field switch {
            "document" => old with { Document = old.Document with { Sources = old.Document.Sources.Select(s => s with { Text = s.Text.Replace("架空科目A", "架空改変科目") }).ToArray() } },
            "output" => old with { Result = old.Result with { Cells = old.Result.Cells.Select(cell => cell with { Lessons = cell.Lessons.Select(lesson => lesson with { Subject = lesson.Subject with { Value = "架空改変科目" } }).ToArray() }).ToArray() } },
            _ => old with { Acceptance = field switch {
            "pdf" => old.Acceptance with { PdfHash = new string('b', 64) },
            "result" => old.Acceptance with { ResultHash = new string('b', 64) },
            "scope" => old.Acceptance with { ScopeHash = new string('b', 64) },
            _ => old.Acceptance with { Metadata = old.Acceptance.Metadata with { ModelId = "fictional-tampered-metadata" } }
            } }
        };
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, broken);

        var prepared = await context.Coordinator.PrepareAsync(MaterialKind.Timetable, 2026);

        Assert.False(prepared.ReusedAcceptance);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, prepared.State);
        Assert.Equal(1, context.BuildCalls);
        Assert.Equal(1, context.ProviderFactoryCalls);
        await context.AssertPriorFormalPreservedAsync();
    }

    [Fact]
    public async Task CurrentValidChangedContentCannotMasqueradeAsTheExactStoredAcceptanceUpgrade()
    {
        await using var context = await Context.CreateAsync();
        var old = context.Audit(compound: false, version: 4);
        await context.Store.WriteAsync(context.Lease, context.AcceptedKey, old);
        var document = old.Document with { Sources = old.Document.Sources.Select(s => s with { Text = s.Text.Replace("架空科目A", "架空科目X") }).ToArray() };
        var result = old.Result with { Metadata = old.Result.Metadata with { ValidatorVersion = RecoveryValidator.Version },
            Cells = old.Result.Cells.Select(cell => cell with { Lessons = cell.Lessons.Select(lesson => lesson with {
                Subject = lesson.Subject with { Value = lesson.Subject.Value.Replace("架空科目A", "架空科目X") }
            }).ToArray() }).ToArray() };
        var candidate = new RecoveryAudit(document, result, new(context.Source.Digest, RecoveryValidator.Fingerprint(result),
            RecoveryValidator.Fingerprint(document), result.Metadata, old.Acceptance.AcceptedAt));
        Assert.True(RecoveryValidator.CanReuse(candidate.Acceptance, candidate.Document, candidate.Result));
        // A standalone valid current audit still cannot replace stored consent
        // through reuseAccepted. Broken ancestry is tested separately above.
        Assert.True(RecoveryAuditCertification.IsCurrent(candidate));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => context.Store.SaveRecoveryAsync(context.Lease,
            context.Source, candidate, context.Clock.GetUtcNow(), reuseAccepted: true));

        Assert.Contains("以前の確認内容と一致", error.Message);
        await context.AssertPriorFormalPreservedAsync();
        Assert.Equal(RecoveryValidator.Fingerprint(old), RecoveryValidator.Fingerprint((await context.Store.ReadAsync<RecoveryAudit>(context.Lease, context.AcceptedKey))!));
    }

    [Fact]
    public async Task SameHashVersion24SuccessCannotSkipAnActualVersion25RefreshParse()
    {
        await using var context = await Context.CreateAsync(seedFailure: false);
        var before = Assert.IsType<MaterialAnalysis>(await context.Store.ReadAsync<MaterialAnalysis>(context.Lease, "analysis.Timetable"));
        Assert.Equal(24, before.ParserVersion);
        Assert.Equal(context.Source.Digest, before.SourceDigest);
        Assert.Null((await context.Store.ReadAsync<MaterialAttempt>(context.Lease, "attempt.Timetable"))!.Failure);

        var refreshed = await context.Materials.RefreshAsync(MaterialKind.Timetable, 2026);

        Assert.False(refreshed.Changed);
        // The actual generated minimal PDF is readable but not a timetable.
        // A stale early return would say Parsed=true and leave the old attempt.
        Assert.False(refreshed.Parsed);
        var attempt = Assert.IsType<MaterialAttempt>(await context.Store.ReadAsync<MaterialAttempt>(context.Lease, "attempt.Timetable"));
        Assert.Equal(25, attempt.ParserVersion);
        Assert.Equal(context.Source.Digest, attempt.SourceDigest);
        Assert.NotNull(attempt.Failure);
        Assert.True(attempt.RecoveryPending);
        await context.AssertPriorFormalPreservedAsync();
        Assert.Equal(context.Bytes, await context.Store.ReadOriginalAsync(context.Lease, context.Source.Id));
    }

    private static void AssertInlineCompoundInvalid(RecoveryAudit audit)
    {
        var validation = RecoveryValidator.Validate(audit.Document, audit.Result);
        Assert.False(validation.CanAdopt);
        Assert.Contains("inlineParallelEvidence", validation.Errors);
        Assert.All(validation.Errors, error => Assert.Contains(error, new[] { "versions", "inlineParallelEvidence" }));
    }

    private sealed class Context : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-inline-cache-" + Guid.NewGuid().ToString("N"));
        private readonly Protector _protector = new();
        public Clock Clock { get; } = new();
        public SchoolDataStore Store { get; private set; } = null!;
        public SchoolLease Lease { get; private set; }
        public SourceRecord Source { get; private set; } = null!;
        public byte[] Bytes { get; } = PdfParsingTests.SyntheticPdf();
        public MaterialCoordinator Materials { get; private set; } = null!;
        public RecoveryCoordinator Coordinator { get; private set; } = null!;
        public int BuildCalls, ProviderFactoryCalls;
        public string AcceptedKey => "recovery.accepted.Timetable." + Source.Digest;
        private RecoveryDocument _good = null!;
        private MaterialAnalysis _prior = null!;

        public static async Task<Context> CreateAsync(bool seedFailure = true)
        {
            var context = new Context();
            try { await context.InitializeAsync(seedFailure); return context; }
            catch { await context.DisposeAsync(); throw; }
        }
        private async Task InitializeAsync(bool seedFailure)
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, "fictional.pdf"); await File.WriteAllBytesAsync(path, Bytes);
            Store = new(Path.Combine(_root, "data"), _protector, Clock); Lease = await Store.BeginAsync();
            var now = Clock.GetUtcNow();
            Source = new("fictional-original", MaterialKind.Timetable, path, "fictional-file-identity", "fictional.pdf", NotificationDiff.Digest(Bytes), Bytes.Length, now, now, null);
            await Store.SaveOriginalAsync(Lease, Source, Bytes);
            _prior = new(Source.Id, Source.Kind, 24, Source.Digest, Source.OriginalName, now.AddDays(-3), 2026,
                Timetable: new(2026, "前期", [new("3_CN", 1, 1, new("架空前回の正常科目"), "架空前回の原文", 1)]));
            await Store.SaveAnalysisAsync(Lease, _prior);
            Materials = new(Store, new(new FakeIdentity()), Clock);
            if (seedFailure)
            {
                var strict = await Materials.RefreshAsync(MaterialKind.Timetable, 2026);
                Assert.False(strict.Parsed);
                Assert.True((await Store.ReadAsync<MaterialAttempt>(Lease, "attempt.Timetable"))!.RecoveryPending);
            }
            var page = RecoveryPipelineTests.Layout(MaterialKind.Timetable);
            _good = RecoveryDocumentBuilder.Build(Source.Digest, Source.Kind, [page], (_, box) => !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))));
            Coordinator = new(Store, Materials, (actual, kind, hash, _, _) => {
                BuildCalls++; Assert.Equal(Bytes, actual); Assert.Equal(Source.Digest, hash);
                // Coordinator lifecycle/cache safety, not PDF extraction quality:
                // use its documented build seam with the real Builder on synthetic geometry.
                return Task.FromResult(RecoveryDocumentBuilder.Build(hash, kind, [page], (_, box) => !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height)))));
            }, _ => { ProviderFactoryCalls++; return Task.FromResult<IReadOnlyList<ILocalRecoveryProvider>>([]); }, Clock);
        }
        public RecoveryAudit Audit(bool compound, int version)
        {
            string Body(string value) => compound ? value.Replace("架空科目A", "架空科目A・架空科目B").Replace("架空教室C", "架空教室C・架空教室D") : value;
            var document = _good with { Sources = _good.Sources.Select(s => s with { Text = Body(s.Text) }).ToArray() };
            var metadata = new RecoveryMetadata("rule", "rules", "3", "3", "4", RecoveryValidator.SchemaVersion, version, "test");
            var cells = _good.Cells.Select(cell => cell.ConfirmedEmpty ? new RecoveredCell(cell.Id, RecoveryValueState.Empty, []) : RecoveryRules.Recover(_good, cell)!).Select(cell => cell with {
                Lessons = cell.Lessons.Select(lesson => lesson with {
                    Subject = lesson.Subject with { Value = Body(lesson.Subject.Value) },
                    Room = lesson.Room with { Value = Body(lesson.Room.Value) }
                }).ToArray()
            }).ToArray();
            var result = new RecoveryResult(Source.Digest, document.Kind, document.SchoolYear, document.Term, cells, metadata);
            return new(document, result, new(Source.Digest, RecoveryValidator.Fingerprint(result), RecoveryValidator.Fingerprint(document), metadata, Clock.GetUtcNow().AddDays(-2)));
        }
        public async Task AssertPriorFormalPreservedAsync()
        {
            var formal = Assert.IsType<MaterialAnalysis>(await Store.ReadAsync<MaterialAnalysis>(Lease, "analysis.Timetable"));
            Assert.Equal(_prior.ParsedAt, formal.ParsedAt); Assert.Equal(_prior.ParserVersion, formal.ParserVersion);
            Assert.Equal("架空前回の正常科目", Assert.Single(formal.Timetable!.Lessons).Names.Subject); Assert.Null(formal.Recovery);
        }
        public async ValueTask DisposeAsync()
        {
            if (Store is not null) await Store.DisposeAsync();
            _protector.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 4, 16, 12, 0, 0, TimeSpan.Zero); }
    private sealed class FakeIdentity : IFileIdentityProvider { public string Identity(FileStream stream) => "fictional-file-identity"; }
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "fictional-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fictional-key");
        public void Dispose() => _cipher.Dispose();
    }
}
