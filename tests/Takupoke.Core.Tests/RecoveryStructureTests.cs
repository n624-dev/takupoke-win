using Takupoke.Core.Recovery;
using Xunit;

namespace Takupoke.Core.Tests;
public partial class RecoveryTests
{
    private static RecoveryDocument Folded(bool adjacent = false, bool twoCells = false)
    {
        var (doc, _) = Fixture();
        var sources = doc.Sources.Where(s => s.CellId != "c0").ToList();
        var cells = doc.Cells.ToArray();
        foreach (var index in twoCells ? new[] { 0, 1 } : new[] { 0 })
        {
            var cell = cells[index]; var x = cell.Box.X; var y = cell.Box.Y;
            var atoms = new[] { ("科目:", 2d, 5d, 6d), ("架空科目A", 18d, 5d, 10d),
                ("担当教", 2d, 17d, 6d), ("架空担当B", 18d, adjacent ? 23d : 23d, 10d),
                ("員:", 2d, adjacent ? 23d : 29d, 4d), ("教室:", 2d, 47d, 6d), ("架空室C", 18d, 47d, 8d) };
            var added = atoms.Select((a, i) => new RecoverySource(cell.Id + "atom" + i, cell.Id, 1, a.Item1, new(x + a.Item2, y + a.Item3, a.Item4, 6), false, i, i)).ToArray();
            sources.AddRange(added);
            cells[index] = cell with { ConfirmedEmpty = false, BindingMode = RecoveryBindingMode.RoleProposal,
                SourceIds = added.Select(s => s.Id).ToArray(), LessonBindings = [], RoleScopes = [] };
        }
        return doc with { Sources = sources, Cells = cells };
    }
    private static RecoveryLesson StructureProposal(RecoveryPromptCell prompt)
    {
        RecoveryField Field(string[] names)
        {
            var labels = names.Select(n => prompt.Sources.Single(s => s.Text == n)).ToArray();
            var x = labels.Max(s => s.Box!.X + s.Box.Width); var top = labels.Min(s => s.Box!.Y); var bottom = labels.Max(s => s.Box!.Y + s.Box.Height);
            var cuts = prompt.StructureCuts;
            return new(RecoveryValueState.Present, "", labels.Select(s => s.Id).Concat(new[] {
                cuts.Last(c => c.Axis == "horizontal" && c.Position <= top).Id,
                cuts.First(c => c.Axis == "horizontal" && c.Position >= bottom).Id,
                cuts.First(c => c.Axis == "vertical" && c.Position >= x).Id }).ToArray());
        }
        return new(Field(["科目:"]), Field(["担当教", "員:"]), Field(["教室:"]), [], []);
    }
    private sealed class StructureProvider(string id = "windowsLanguageModel") : ILocalRecoveryProvider
    {
        public string Id => id; public bool LocalOnly => true;
        public RecoveryMetadata Metadata => new(id, "fictional-probe", "1", "test", "3", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, "test");
        public int Calls, AvailabilityCalls; public int FailOnCall; public bool Invalid; public bool FailAvailability; public bool Cancel;
        public Task<LocalProviderState> AvailabilityAsync(CancellationToken token) { AvailabilityCalls++; if (FailAvailability) throw new IOException("fictional runtime failure"); return Task.FromResult(LocalProviderState.Ready); }
        public Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell prompt, CancellationToken token)
        {
            Calls++; if (Cancel) throw new OperationCanceledException(token); if (Calls == FailOnCall) throw new IOException("fictional runtime failure");
            var lesson = StructureProposal(prompt);
            if (Invalid) lesson = lesson with { Teacher = lesson.Teacher with { Evidence = ["invented-cut"] } };
            return Task.FromResult<IReadOnlyList<RecoveryLesson>>([lesson]);
        }
    }
    [Fact] public async Task NonAdjacentLabelCanBeProposedThenOriginalValuesAreValidated()
    {
        var doc = Folded(); Assert.Contains("roleScopeCount", RecoveryValidator.InputErrors(doc));
        var p = new StructureProvider(); var structure = await RecoveryStructure.ResolveAsync(doc, "windows", 10, [p], default);
        Assert.Equal(1, p.Calls); Assert.NotNull(structure.Document); Assert.Empty(RecoveryValidator.InputErrors(structure.Document!));
        var run = await RecoveryEngine.RunAsync(structure.Document!, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State); Assert.Equal(p.Metadata, run.Result!.Metadata);
        Assert.Equal("架空科目A", run.Result.Cells[0].Lessons[0].Subject.Value); Assert.Equal("架空担当B", run.Result.Cells[0].Lessons[0].Teacher.Value);
        Assert.True(RecoveryValidator.Validate(structure.Document!, run.Result).CanAdopt);
        Assert.Contains("structureMetadata", RecoveryValidator.Validate(structure.Document!, run.Result with { Metadata = run.Result.Metadata with { Provider = "rule" } }).Errors);
        Assert.Contains("versions", RecoveryValidator.Validate(structure.Document!, run.Result with { Metadata = run.Result.Metadata with { ValidatorVersion = 2 } }).Errors);
    }
    [Fact] public async Task AdjacentLabelsResolveWithoutPreparingOrLoadingModel()
    {
        var p = new StructureProvider(); var run = await RecoveryStructure.ResolveAsync(Folded(adjacent: true), "windows", 10, [p], default);
        Assert.NotNull(run.Document); Assert.Null(run.Document!.StructureMetadata); Assert.Equal(0, p.Calls); Assert.Equal(0, p.AvailabilityCalls);
    }
    [Fact] public async Task InvalidProposalIsTerminalBeforeFallback()
    {
        var p = new StructureProvider { Invalid = true }; var fallback = new StructureProvider("foundryLocal");
        var run = await RecoveryStructure.ResolveAsync(Folded(), "windows", 10, [p, fallback], default);
        Assert.Null(run.Document); Assert.Equal(RecoveryJobState.Failed, run.State); Assert.Equal(0, fallback.AvailabilityCalls);
    }
    [Fact] public async Task RuntimeFallbackReproposesEveryCellWithOneProviderMetadata()
    {
        var p = new StructureProvider { FailOnCall = 2 }; var fallback = new StructureProvider("foundryLocal");
        var run = await RecoveryStructure.ResolveAsync(Folded(twoCells: true), "windows", 10, [p, fallback], default);
        Assert.NotNull(run.Document); Assert.Equal(2, p.Calls); Assert.Equal(2, fallback.Calls); Assert.Equal(fallback.Metadata, run.Document!.StructureMetadata);
    }
    [Fact] public async Task AvailabilityFailureCanUseNextLocalProvider()
    {
        var p = new StructureProvider { FailAvailability = true }; var fallback = new StructureProvider("foundryLocal");
        var run = await RecoveryStructure.ResolveAsync(Folded(), "windows", 10, [p, fallback], default);
        Assert.NotNull(run.Document); Assert.Equal(0, p.Calls); Assert.Equal(1, fallback.Calls);
    }
    [Fact] public async Task CancellationCannotProduceStructureOrPreview()
    {
        var p = new StructureProvider { Cancel = true }; var fallback = new StructureProvider("foundryLocal");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecoveryStructure.ResolveAsync(Folded(), "windows", 10, [p, fallback], default)); Assert.Equal(0, fallback.Calls);
    }
    [Fact] public async Task MissingCoverageAndBrokenKnownScopeRejectBeforeModelAvailability()
    {
        var doc = Folded(twoCells: true); var other = doc.Cells[1];
        doc = doc with { Cells = doc.Cells.Select(c => c.Id == other.Id ? c with { RoleScopes = [new(0, RecoveryFieldRole.Teacher, 1, other.Box, [], new(1, other.Box, RecoveryHeaderAxis.Left), RecoveryRoleProof.InlineLabel, false)] } : c).ToArray() };
        var p = new StructureProvider(); Assert.Null((await RecoveryStructure.ResolveAsync(doc, "windows", 10, [p], default)).Document); Assert.Equal(0, p.AvailabilityCalls);
        doc = Folded(); doc = doc with { Cells = doc.Cells.SkipLast(1).ToArray() };
        Assert.Null((await RecoveryStructure.ResolveAsync(doc, "windows", 10, [p], default)).Document); Assert.Equal(0, p.AvailabilityCalls);
    }
    [Fact] public void InventedCutAndSwappedRoleCannotPassPhysicalCertificate()
    {
        var doc = Folded(); var cell = doc.Cells[0]; var prompt = RecoveryStructure.Prompt(doc, cell); var valid = StructureProposal(prompt);
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryStructure.Verify(doc, cell, prompt, [valid with { Subject = valid.Teacher, Teacher = valid.Subject }]));
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryStructure.Verify(doc, cell, prompt with { StructureCuts = prompt.StructureCuts.Select((c, i) => i == 0 ? c with { Position = c.Position + 1 } : c).ToArray() }, [valid]));
        var altered = doc with { Sources = doc.Sources.Select(s => s.Text == "架空担当B" ? s with { Box = s.Box with { Y = 140 } } : s).ToArray() };
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryStructure.Verify(altered, cell, RecoveryStructure.Prompt(altered, cell), [valid]));
    }
}
