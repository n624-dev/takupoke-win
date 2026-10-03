using Takupoke.Core.Recovery;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Takupoke.Core.Tests;
public class RecoveryTests
{
    private static (RecoveryDocument Doc, RecoveryResult Result) Fixture()
    {
        var slots = (from d in Enumerable.Range(1, 5) from p in Enumerable.Range(1, 8) select new RecoverySlot("3_CN", d.ToString(), p)).ToArray();
        var box = new RecoveryBox(110, 110, 60, 10);
        var sources = new List<RecoverySource> { new RecoverySource("heading", "header", 1, "2026年度", new(0, 0, 90, 10)),
            new RecoverySource("subject", "c0", 1, "架空科目A", box), new RecoverySource("teacher", "c0", 1, "架空教員A", box), new RecoverySource("room", "c0", 1, "架空教室A", box) };
        sources.Add(new("term", "header", 1, "前期", new(0, 20, 40, 10)));
        sources.Add(new("class", "header", 1, "3_CN", new(10, 110, 20, 10)));
        sources.AddRange(Enumerable.Range(1, 5).Select(d => new RecoverySource("day" + d, "header", 1, new[] { "月", "火", "水", "木", "金" }[d - 1], new(d * 100 + 10, 20, 60, 10))));
        sources.AddRange(Enumerable.Range(1, 8).Select(p => new RecoverySource("period" + p, "header", 1, p.ToString(), new(50, p * 100 + 10, 20, 10))));
        var cells = slots.Select((slot, i) => new RecoveryCell("c" + i, 1, new(int.Parse(slot.Day) * 100, slot.Period * 100, 100, 100), RecoveryInputState.Complete, [slot], i == 0 ? ["subject", "teacher", "room"] : [], [], i != 0) { ClassHeaderIds = ["class"], DayHeaderIds = ["day" + slot.Day], PeriodHeaderIds = ["period" + slot.Period], LessonBindings = i == 0 ? [new(["subject"], ["teacher"], ["room"])] : [], ClassRegion = new(1, new(0, 100, 40, 800), RecoveryHeaderAxis.Left), DayRegion = new(1, new(int.Parse(slot.Day) * 100, 0, 100, 50), RecoveryHeaderAxis.Above), PeriodRegions = new Dictionary<string, RecoveryHeaderRegion> { [slot.Period.ToString()] = new(1, new(40, slot.Period * 100, 40, 100), RecoveryHeaderAxis.Left) } }).ToArray();
        var doc = new RecoveryDocument(new string('a', 64), RecoveryDocumentKind.Timetable, 2026, "前期", ["3_CN"], ["1", "2", "3", "4", "5"], slots, cells, sources, true, ["heading"], ["term"],
            Enumerable.Range(1, 5).ToDictionary(i => i.ToString(), i => (IReadOnlyList<string>)new[] { "day" + i }), new Dictionary<string, IReadOnlyList<string>> { ["3_CN"] = ["class"] },
            Enumerable.Range(1, 8).ToDictionary(i => i.ToString(), i => (IReadOnlyList<string>)new[] { "period" + i }), new Dictionary<string, string>(), [], []);
        var lesson = new RecoveryLesson(new(RecoveryValueState.Present, "架空科目A", ["subject"]), new(RecoveryValueState.Present, "架空教員A", ["teacher"]), new(RecoveryValueState.Present, "架空教室A", ["room"]), ["day1"], ["period1"]);
        var result = new RecoveryResult(doc.PdfHash, doc.Kind, 2026, "前期", cells.Select((c, i) => new RecoveredCell(c.Id, i == 0 ? RecoveryValueState.Present : RecoveryValueState.Empty, i == 0 ? [lesson] : [])).ToArray(),
            new("rule", "rules", "1", "1", "1", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, "test"));
        return (doc, result);
    }
    private sealed class ProbeProvider(string id, RecoveryMetadata metadata) : ILocalRecoveryProvider
    {
        public string Id => id; public bool LocalOnly => true; public RecoveryMetadata Metadata => metadata with { Provider = id };
        public LocalProviderState State = LocalProviderState.Ready; public int AvailabilityCalls, RecoveryCalls;
        public Task<LocalProviderState> AvailabilityAsync(CancellationToken token) { AvailabilityCalls++; return Task.FromResult(State); }
        public Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell, CancellationToken token) { RecoveryCalls++; throw new InvalidRecoveryOutputException(); }
    }
    private static RecoveryDocument Uncertain() { var (d, _) = Fixture(); return d with { Sources = d.Sources.Where(s => s.Id != "teacher").ToArray(), Cells = d.Cells.Select((c, i) => i == 0 ? c with { SourceIds = c.SourceIds.Where(id => id != "teacher").ToArray(), LessonBindings = [c.LessonBindings[0] with { Teacher = [] }] } : c).ToArray() }; }
    [Fact] public async Task GroundedRulesDoNotLoadLanguageModel() { var (d, r) = Fixture(); var p = new ProbeProvider("windowsLanguageModel", r.Metadata); var run = await RecoveryEngine.RunAsync(d, "windows", 10, true, [p], _ => null); Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State); Assert.Equal("rule", run.Result?.Metadata.Provider); Assert.Equal(0, p.AvailabilityCalls); }
    [Fact] public async Task ModelNotReadyWaitsWithoutDownloadingFallback() { var (_, r) = Fixture(); var p = new ProbeProvider("systemLanguageModel", r.Metadata) { State = LocalProviderState.NotReady }; var fallback = new ProbeProvider("coreAI", r.Metadata); var run = await RecoveryEngine.RunAsync(Uncertain(), "ios", 27, true, [p, fallback], _ => null); Assert.Equal(RecoveryJobState.AwaitingModel, run.State); Assert.Equal(0, fallback.AvailabilityCalls); }
    [Fact] public async Task MalformedOutputIsTerminalBeforeNextProvider() { var (_, r) = Fixture(); var p = new ProbeProvider("windowsLanguageModel", r.Metadata); var fallback = new ProbeProvider("foundryLocal", r.Metadata); var run = await RecoveryEngine.RunAsync(Uncertain(), "windows", 10, true, [p, fallback], _ => null); Assert.Equal(new[] { "invalidOutput" }, run.Errors); Assert.Null(run.Result); Assert.Equal(0, fallback.AvailabilityCalls); }
    [Fact] public void CompleteGroundedResultCanBePreviewed() { var (d, r) = Fixture(); Assert.Empty(RecoveryValidator.Validate(d, r).Errors); }
    [Theory] [InlineData(RecoveryValueState.Unreadable)] [InlineData(RecoveryValueState.Missing)] [InlineData(RecoveryValueState.Ambiguous)]
    public void UnknownIsNeverFreePeriod(RecoveryValueState state) { var (d, r) = Fixture(); Assert.Contains("cellState", RecoveryValidator.Validate(d, r with { Cells = r.Cells.Select((c, i) => i == 0 ? c with { State = state } : c).ToArray() }).Errors); }
    [Fact] public void IncompleteReaderIsRejected() { var (d, r) = Fixture(); Assert.Contains("incompleteDocument", RecoveryValidator.Validate(d with { Complete = false }, r).Errors); }
    [Fact] public void PartialCellIsRejected() { var (d, r) = Fixture(); Assert.Contains("incompleteCell", RecoveryValidator.Validate(d with { Cells = d.Cells.Select((c, i) => i == 0 ? c with { InputState = RecoveryInputState.Partial } : c).ToArray() }, r).Errors); }
    [Fact] public void MissingSlotIsRejected() { var (d, r) = Fixture(); Assert.Contains("coverage", RecoveryValidator.Validate(d with { Cells = d.Cells.Skip(1).ToArray() }, r).Errors); }
    [Fact] public void MissingResultCellIsRejected() { var (d, r) = Fixture(); Assert.Contains("resultCoverage", RecoveryValidator.Validate(d, r with { Cells = r.Cells.Skip(1).ToArray() }).Errors); }
    [Fact] public void DuplicateIdsRejectWithoutThrowing() { var (d, r) = Fixture(); Assert.Contains("duplicateIds", RecoveryValidator.Validate(d with { Sources = d.Sources.Concat([d.Sources[0]]).ToArray() }, r).Errors); }
    [Fact] public void NeighbourEvidenceIsRejected() { var (d, r) = Fixture(); Assert.Contains("sourcePosition", RecoveryValidator.Validate(d with { Sources = d.Sources.Select(s => s.Id == "room" ? s with { CellId = "c1" } : s).ToArray() }, r).Errors); }
    [Fact] public void InventedValueIsRejected() { var (d, r) = Fixture(); var c = r.Cells[0]; var l = c.Lessons[0]; Assert.Contains("fieldEvidence", RecoveryValidator.Validate(d, r with { Cells = new[] { c with { Lessons = [l with { Room = l.Room with { Value = "架空教室B" } }] } }.Concat(r.Cells.Skip(1)).ToArray() }).Errors); }
    [Fact] public void ContentCannotBecomeEmpty() { var (d, r) = Fixture(); Assert.Contains("falseEmpty", RecoveryValidator.Validate(d, r with { Cells = r.Cells.Select((c, i) => i == 0 ? c with { State = RecoveryValueState.Empty, Lessons = [] } : c).ToArray() }).Errors); }
    [Fact] public void ChangedYearIsRejected() { var (d, r) = Fixture(); Assert.Contains("documentIdentity", RecoveryValidator.Validate(d, r with { SchoolYear = 2025 }).Errors); }
    [Fact] public void ChangedHashIsRejected() { var (d, r) = Fixture(); Assert.Contains("sourceHash", RecoveryValidator.Validate(d, r with { PdfHash = new string('b', 64) }).Errors); }
    [Fact] public void ApprovalOnlyReusesExactValidatedResult() { var (d, r) = Fixture(); var a = new RecoveryAcceptance(d.PdfHash, RecoveryValidator.Fingerprint(r), RecoveryValidator.Fingerprint(d), r.Metadata, DateTimeOffset.UtcNow); Assert.True(RecoveryValidator.CanReuse(a, d, r)); Assert.False(RecoveryValidator.CanReuse(a, d, r with { Metadata = r.Metadata with { ModelVersion = "2" } })); }
    [Fact] public void WrongSchemaRejects() { var (d, r) = Fixture(); Assert.Contains("versions", RecoveryValidator.Validate(d, r with { Metadata = r.Metadata with { RecoverySchemaVersion = 99 } }).Errors); }
    [Fact] public void XlsxAndTemporaryNotReadyNeverTriggerModelFallback() { Assert.Null(RecoveryPolicy.Kind(Takupoke.Core.MaterialKind.Changes)); Assert.False(RecoveryPolicy.MayTryNext(LocalProviderState.NotReady)); Assert.False(RecoveryPolicy.MayTryNext(LocalProviderState.Disabled)); Assert.Equal(new[] { "liteRtLm" }, RecoveryPolicy.Providers("android")); }
    [Fact] public void WrongHeaderTextIsRejected() { var (d, r) = Fixture(); Assert.False(RecoveryValidator.Validate(d with { Sources = d.Sources.Select(s => s.Id == "class" ? s with { Text = "3_ES" } : s).ToArray() }, r).CanAdopt); }
    [Fact] public void FieldsCannotSwapRoles() { var (d, r) = Fixture(); var l = r.Cells[0].Lessons[0]; Assert.Contains("fieldEvidence", RecoveryValidator.Validate(d, r with { Cells = r.Cells.Select((c, i) => i == 0 ? c with { Lessons = [l with { Subject = l.Teacher, Teacher = l.Subject }] } : c).ToArray() }).Errors); }
    [Fact] public void TruncatedNamesAreRejected() { var (d, r) = Fixture(); var l = r.Cells[0].Lessons[0]; Assert.Contains("fieldEvidence", RecoveryValidator.Validate(d, r with { Cells = r.Cells.Select((c, i) => i == 0 ? c with { Lessons = [l with { Teacher = l.Teacher with { Value = "架空教" } }] } : c).ToArray() }).Errors); }
    [Fact] public void KnownTextCannotBecomeBlank() { var (d, r) = Fixture(); var l = r.Cells[0].Lessons[0]; Assert.Contains("falseBlankField", RecoveryValidator.Validate(d with { Cells = d.Cells.Select((c, i) => i == 0 ? c with { BlankFields = ["teacher"] } : c).ToArray() }, r with { Cells = r.Cells.Select((c, i) => i == 0 ? c with { Lessons = [l with { Teacher = new(RecoveryValueState.Empty, "", []) }] } : c).ToArray() }).Errors); }
    [Fact] public void OverlappingCellsAreRejected() { var (d, r) = Fixture(); Assert.Contains("cellOverlap", RecoveryValidator.Validate(d with { Cells = d.Cells.Select((c, i) => i == 1 ? c with { Box = d.Cells[0].Box } : c).ToArray() }, r).Errors); }
    [Fact] public void WrongHeadingAxisIsRejected() { var (d, r) = Fixture(); Assert.Contains("dayBinding", RecoveryValidator.Validate(d with { Cells = d.Cells.Select((c, i) => i == 0 ? c with { DayRegion = c.DayRegion! with { Axis = RecoveryHeaderAxis.Left } } : c).ToArray() }, r).Errors); }
    [Fact] public void OverflowingBoundsReject() => Assert.False(new RecoveryBox(double.MaxValue, 1, double.MaxValue, 1).Valid);
    [Fact] public async Task CancelledRuleRecoveryDoesNotReturnPreview() { var (d, r) = Fixture(); using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecoveryEngine.RunAsync(d, "windows", 10, true, [], c => r.Cells.Single(l => l.CellId == c.Id), cancel.Token)); }
    [Fact] public void ManifestRequiresKnownBackendAndMinimumOS() { var m = new RecoveryModelManifest("synthetic", "1", "https://models.example.invalid/model", 1, new string('a', 64), "foundryLocal", "10.0.26100", 1, "CPU", "test-only", true); Assert.True(m.IsUsable("foundryLocal", 1)); Assert.False(m.SupportsOs("10.0.22631")); Assert.True(m.SupportsOs("10.0.26100")); Assert.False((m with { RecommendedBackend = "BOGUS" }).IsUsable("foundryLocal", 1)); }
    [Fact] public void NullProviderFieldCannotCrashValidator() { var (d, r) = Fixture(); var l = r.Cells[0].Lessons[0]; Assert.False(RecoveryValidator.Validate(d, r with { Cells = r.Cells.Select((c, i) => i == 0 ? c with { Lessons = [l with { Teacher = l.Teacher with { Evidence = null! } }] } : c).ToArray() }).CanAdopt); }

    private sealed record SpecialFixture(RecoveryDocument Document, RecoveryResult Result);
    private static (RecoveryDocument Doc, RecoveryResult Result) Special(string kind = "exam") { var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }; var f = JsonSerializer.Deserialize<SpecialFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "recovery-" + kind + ".json")), options)!; return (f.Document, f.Result); }
    [Theory] [InlineData("exam")] [InlineData("return")] public void SpecialSchedulesWithFullScopeAndExplicitSpanTimesPass(string kind) { var (d, r) = Special(kind); Assert.Empty(RecoveryValidator.Validate(d, r).Errors); }
    [Fact] public void InventoryCannotDiscardTextToClaimEmpty() { var (d, r) = Fixture(); Assert.Contains("sourceInventory", RecoveryValidator.Validate(d with { Cells = d.Cells.Select((c, i) => i == 0 ? c with { SourceIds = [], LessonBindings = [], ConfirmedEmpty = true } : c).ToArray() }, r with { Cells = r.Cells.Select((c, i) => i == 0 ? c with { State = RecoveryValueState.Empty, Lessons = [] } : c).ToArray() }).Errors); }
    [Fact] public void UnassignedTextInBlankCellRejects() { var (d, r) = Fixture(); Assert.Contains("unassignedCellText", RecoveryValidator.Validate(d with { Sources = d.Sources.Append(new RecoverySource("unassigned", "unassigned", d.Cells[1].Page, "架空の未割当文字", d.Cells[1].Box)).ToArray() }, r).Errors); }
    [Fact] public void UnboundLiteralCannotBeClassifiedAsPeriodHeader() { var (d, r) = Fixture(); var evidence = d.PeriodEvidence.ToDictionary(); evidence["1"] = evidence["1"].Append("orphan").ToArray(); Assert.Contains("periodHeaderCoverage", RecoveryValidator.Validate(d with { Sources = d.Sources.Append(new RecoverySource("orphan", "unassigned", 1, "1", new(650, 200, 10, 10))).ToArray(), PeriodEvidence = evidence }, r).Errors); }
    [Fact] public void UnusedSpanCannotClassifyOrphanAsClock() { var (d, r) = Special(); var spans = d.SpanTimes.ToDictionary(); spans["2026-10-02:3-4"] = "架空の授業漏れ"; var evidence = d.ClockEvidence.ToDictionary(); evidence["2026-10-02:3-4"] = ["orphan"]; Assert.Contains("clockScope", RecoveryValidator.Validate(d with { Sources = d.Sources.Append(new RecoverySource("orphan", "unassigned", 1, "架空の授業漏れ", new(360, 200, 10, 10))).ToArray(), SpanTimes = spans, ClockEvidence = evidence }, r).Errors); }
    [Fact] public void OrphanCannotBeHiddenInYearOrClassEvidence() { var (d, r) = Fixture(); d = d with { Sources = d.Sources.Append(new RecoverySource("orphan", "unassigned", 1, "架空の授業漏れ", new(650, 200, 10, 10))).ToArray() }; Assert.Contains("yearEvidenceText", RecoveryValidator.Validate(d with { YearEvidence = d.YearEvidence.Append("orphan").ToArray() }, r).Errors); var evidence = d.ClassEvidence.ToDictionary(); evidence["3_CN"] = evidence["3_CN"].Append("orphan").ToArray(); Assert.Contains("classEvidence", RecoveryValidator.Validate(d with { ClassEvidence = evidence }, r).Errors); }
    [Fact] public void OrphanCannotBeHiddenInReturnNote() { var (d, r) = Special("return"); Assert.Contains("normalTimeNote", RecoveryValidator.Validate(d with { Sources = d.Sources.Append(new RecoverySource("orphan", "unassigned", 1, "架空の授業漏れ", new(360, 200, 10, 10))).ToArray(), NormalTimeNoteEvidence = d.NormalTimeNoteEvidence.Append("orphan").ToArray() }, r).Errors); }
    [Fact] public void OrphanTextOutsideCellsRejects() { var (d, r) = Fixture(); Assert.Contains("unclassifiedSource", RecoveryValidator.Validate(d with { Sources = d.Sources.Append(new RecoverySource("orphan", "unassigned", 1, "架空の授業漏れ", new(650, 200, 10, 10))).ToArray() }, r).Errors); }
    [Fact] public void CommonClockCannotOverrideDateSpecificTime() { var (d, r) = Special(); var first = "2026-10-01:1"; var second = "2026-10-02:1"; var times = d.Times.ToDictionary(); times[second] = times[first]; var evidence = d.ClockEvidence.ToDictionary(); evidence[second] = evidence[first]; var bindings = d.ClockBindings.ToDictionary(); bindings[second] = bindings[first] with { Day = "*" }; Assert.Contains("clockEvidence", RecoveryValidator.Validate(d with { Times = times, ClockEvidence = evidence, ClockBindings = bindings }, r).Errors); }
    [Fact] public void AnotherDayClockCannotBeQuoted() { var (d, r) = Special(); var first = "2026-10-01:1"; var second = "2026-10-02:1"; var times = d.Times.ToDictionary(); times[first] = times[second]; var evidence = d.ClockEvidence.ToDictionary(); evidence[first] = evidence[second]; Assert.Contains("clockEvidence", RecoveryValidator.Validate(d with { Times = times, ClockEvidence = evidence }, r).Errors); }
    [Fact] public void ReversedSpanClockCannotPassEvenWhenTextExists() { var (d, r) = Special(); var spans = d.SpanTimes.ToDictionary(); spans["2026-10-01:1-2"] = "18:00〜08:00"; Assert.Contains("spanTimeEvidence", RecoveryValidator.Validate(d with { SpanTimes = spans, Sources = d.Sources.Select(s => s.Id == "span-clock" ? s with { Text = "18:00〜08:00" } : s).ToArray() }, r).Errors); }
    [Fact] public void ReturnNormalTimeRequiresActualApplicableNote() { var (d, r) = Special("return"); Assert.Contains("normalTimeNote", RecoveryValidator.Validate(d with { Sources = d.Sources.Select(s => s.Id == "normal-note" ? s with { Text = "架空の無関係な注記" } : s).ToArray() }, r).Errors); }
    [Fact] public void SeventeenClassesCannotIncludeAnUnexpectedReplacement() { var (d, r) = Special(); Assert.Contains("specialScope", RecoveryValidator.Validate(d with { Classes = new[] { "1_CN" }.Concat(d.Classes.Skip(1)).ToArray() }, r).Errors); }

    private static (RecoveryDocument Doc, RecoveryResult Result) Proposal()
    {
        var (d, r) = Fixture();
        var scopes = Enum.GetValues<RecoveryFieldRole>().Select((role, i) => new RecoveryRoleScope(0, role, 1, new(130, 110 + i * 25, 60, 20), ["label" + i], new(1, new(105, 110 + i * 25, 20, 10), RecoveryHeaderAxis.Left), RecoveryRoleProof.InlineLabel, false)).ToArray();
        var names = new[] { "科目", "教員", "教室" };
        var sources = d.Sources.Select(src => src.Id is "subject" or "teacher" or "room" ? src with { Box = new(130, 110 + Array.IndexOf(new[] { "subject", "teacher", "room" }, src.Id) * 25, 50, 10) } : src).Concat(names.Select((name, i) => new RecoverySource("label" + i, "c0", 1, name, new(105, 110 + i * 25, 20, 10)))).ToArray();
        var cell = d.Cells[0] with { BindingMode = RecoveryBindingMode.RoleProposal, LessonBindings = [], RoleScopes = scopes, SourceIds = ["subject", "teacher", "room", "label0", "label1", "label2"] };
        return (d with { Sources = sources, Cells = new[] { cell }.Concat(d.Cells.Skip(1)).ToArray() }, r);
    }
    private sealed class AssignmentProvider(RecoveryMetadata metadata, RecoveryLesson lesson) : ILocalRecoveryProvider
    {
        public string Id => "windowsLanguageModel"; public bool LocalOnly => true; public RecoveryMetadata Metadata => metadata with { Provider = Id };
        public int Calls;
        public Task<LocalProviderState> AvailabilityAsync(CancellationToken token) => Task.FromResult(LocalProviderState.Ready);
        public Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell, CancellationToken token) { Calls++; return Task.FromResult<IReadOnlyList<RecoveryLesson>>([lesson with { Subject = lesson.Subject with { Value = "model-generated-value-is-ignored" } }]); }
    }
    [Fact] public async Task RoleProposalReachesModelAndRebuildsValuesFromOriginalAtoms()
    { var (d, r) = Proposal(); Assert.Empty(RecoveryValidator.InputErrors(d)); var p = new AssignmentProvider(r.Metadata, r.Cells[0].Lessons[0]); var run = await RecoveryEngine.RunAsync(d, "windows", 10, true, [p], _ => null); Assert.Equal(1, p.Calls); Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State); Assert.Equal("架空科目A", run.Result!.Cells[0].Lessons[0].Subject.Value); }
    [Fact] public void ProposalCannotSwapSubjectAndTeacherEvenWhenEveryAtomIsUsed()
    { var (d, r) = Proposal(); var l = r.Cells[0].Lessons[0]; Assert.Contains("roleFieldEvidence", RecoveryValidator.Validate(d, r with { Cells = new[] { r.Cells[0] with { Lessons = [l with { Subject = l.Teacher, Teacher = l.Subject }] } }.Concat(r.Cells.Skip(1)).ToArray() }).Errors); }
    [Fact] public void FixedCellCannotUseUnusedScopeToHideOrphanText()
    { var (d, r) = Fixture(); var extra = new RecoverySource("orphan", "unassigned", 1, "架空の未割当文字", new(2000, 2000, 20, 10)); var scope = new RecoveryRoleScope(99, RecoveryFieldRole.Subject, 1, extra.Box, [extra.Id], new(1, extra.Box, RecoveryHeaderAxis.Left), RecoveryRoleProof.InlineLabel, false); d = d with { Sources = d.Sources.Append(extra).ToArray(), Cells = new[] { d.Cells[0] with { RoleScopes = [scope] } }.Concat(d.Cells.Skip(1)).ToArray() }; var errors = RecoveryValidator.Validate(d, r).Errors; Assert.Contains("unusedRoleScopes", errors); Assert.Contains("unclassifiedSource", errors); }
    [Fact] public void DuplicateProposalScopesRejectWithoutException()
    { var (d, r) = Proposal(); d = d with { Cells = new[] { d.Cells[0] with { RoleScopes = d.Cells[0].RoleScopes.Append(d.Cells[0].RoleScopes[0]).ToArray() } }.Concat(d.Cells.Skip(1)).ToArray() }; Assert.False(RecoveryValidator.Validate(d, r).CanAdopt); }
    [Fact] public void PinnedFoundryVariantHasSafeDistinctFileKey()
    { var m = new RecoveryModelManifest("qwen3-0.6b-generic-cpu:4", "4", "https://models.example.invalid/model", 1, new string('a', 64), "foundryLocal", "10.0.26100", 1, "CPU", "Apache-2.0", true); Assert.True(m.IsUsable("foundryLocal", 1)); Assert.DoesNotContain(":", m.StorageKey); Assert.NotEqual(m.StorageKey, (m with { ModelId = "qwen3-0.6b-generic-cpu_4" }).StorageKey); }

    [Fact] public void RepeatedReturnNotesAreVerifiedIndependentlyPerPage()
    {
        var (d, r) = Special("return"); var note = d.Sources.Single(s => s.Id == "normal-note");
        d = d with { Sources = d.Sources.Append(note with { Id = "normal-note-copy", Page = 7 }).ToArray(), NormalTimeNoteEvidence = d.NormalTimeNoteEvidence.Append("normal-note-copy").ToArray() };
        Assert.Empty(RecoveryValidator.Validate(d, r).Errors);
        Assert.Contains("normalTimeNote", RecoveryValidator.Validate(d with { Sources = d.Sources.Select(s => s.Id == "normal-note-copy" ? s with { Text = s.Text.Replace("5日", "6日") } : s).ToArray() }, r).Errors);
    }
    [Fact] public void ReturnRestdaySpanUsesTheApplicablePrintedNormalTimeNote()
    {
        var (d, r) = Special("return"); var a = d.Cells.Single(c => c.Id == "c0-1-1"); var b = d.Cells.Single(c => c.Id == "c0-1-2");
        var merged = a with { Box = a.Box with { Height = a.Box.Height + b.Box.Height }, Slots = a.Slots.Concat(b.Slots).ToArray(), PeriodHeaderIds = a.PeriodHeaderIds.Concat(b.PeriodHeaderIds).ToArray(), PeriodRegions = a.PeriodRegions.Concat(b.PeriodRegions).ToDictionary(p => p.Key, p => p.Value) };
        var spans = d.SpanTimes.ToDictionary(); spans["2026-10-02:1-2"] = "08:50〜10:20"; var evidence = d.ClockEvidence.ToDictionary(); evidence["2026-10-02:1-2"] = d.NormalTimeNoteEvidence;
        d = d with { Cells = d.Cells.Where(c => c.Id != b.Id).Select(c => c.Id == a.Id ? merged : c).ToArray(), SpanTimes = spans, ClockEvidence = evidence };
        r = r with { Cells = r.Cells.Where(c => c.CellId != b.Id).ToArray() };
        Assert.Empty(RecoveryValidator.Validate(d, r).Errors);
        spans["2026-10-02:1-2"] = "08:50〜10:25";
        Assert.Contains("spanTimeEvidence", RecoveryValidator.Validate(d, r).Errors);
    }

    [Theory] [InlineData("教員:架空教員A")] [InlineData("架空教員A・教員:架空教員B")] public void FixedBindingCannotTreatOriginalRolePrefixAsAValue(string original)
    {
        var (d, r) = Fixture(); var teacher = r.Cells[0].Lessons[0].Teacher with { Value = original };
        d = d with { Sources = d.Sources.Select(s => s.Id == "teacher" ? s with { Text = teacher.Value } : s).ToArray() };
        r = r with { Cells = r.Cells.Select((c, i) => i == 0 ? c with { Lessons = [c.Lessons[0] with { Teacher = teacher }] } : c).ToArray() };
        Assert.Contains("unboundRoleLabel", RecoveryValidator.Validate(d, r).Errors);
    }

}
