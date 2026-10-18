using System.Text.Json;

namespace Takupoke.Core.Recovery;

public sealed record RecoveryPromptCell(string CellId, IReadOnlyList<RecoverySlot> Slots, IReadOnlyList<RecoveryPromptSource> Sources,
    IReadOnlyList<string> BlankFields, int ParallelCount, IReadOnlyList<RecoveryLessonBinding> LessonBindings, IReadOnlyList<RecoveryRoleScope>? RoleScopes = null)
{
    public string Mode { get; init; } = "fieldExtraction";
    public IReadOnlyList<RecoveryStructureCut> StructureCuts { get; init; } = [];
}
public sealed record RecoveryStructureCut(string Id, string Axis, double Position);
public sealed record RecoveryPromptSource(string Id, string Text, RecoveryBox? Box = null, int? Page = null, int? SourceLine = null, int? SourceOrder = null);
public interface ILocalRecoveryProvider
{
    string Id { get; }
    bool LocalOnly { get; }
    RecoveryMetadata Metadata { get; }
    Task<LocalProviderState> AvailabilityAsync(CancellationToken token);
    Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell, CancellationToken token);
}
public sealed class InvalidRecoveryOutputException(Exception? inner = null) : Exception("復旧出力を確認できません。", inner);
public sealed record RecoveryRun(RecoveryJobState State, RecoveryResult? Result, IReadOnlyList<string> Errors);
public static class RecoveryRules
{
    public static RecoveredCell? Recover(RecoveryDocument doc, RecoveryCell cell, CancellationToken token = default)
    {
        var work = new RecoveryWorkBudget(token); return Recover(doc, cell, new RecoverySourceIndex(doc, work), work);
    }
    public static IReadOnlyList<RecoveredCell?> RecoverAll(RecoveryDocument doc, CancellationToken token = default)
    {
        var work = new RecoveryWorkBudget(token); var index = new RecoverySourceIndex(doc, work);
        return doc.Cells.Select(cell => Recover(doc, cell, index, work)).ToArray();
    }
    internal static RecoveredCell? Recover(RecoveryDocument doc, RecoveryCell cell, RecoverySourceIndex sourceIndex, RecoveryWorkBudget work)
    {
        if (cell.SourceIds.Count > 100000 || cell.RoleScopes.Count > 12 || cell.LessonBindings.Count > 4 || cell.Slots.Count > 8) throw new RecoveryWorkLimitException();
        work.Step(cell.SourceIds.Count + cell.RoleScopes.Count);
        if (cell.InputState != RecoveryInputState.Complete || cell.ConfirmedEmpty) return null;
        if (cell.BindingMode == RecoveryBindingMode.RoleProposal)
        {
            var inlineLabels = cell.RoleScopes.Where(s => s.Proof == RecoveryRoleProof.InlineLabel).SelectMany(s => s.LabelSourceIds).ToHashSet();
            var bodyIds = cell.SourceIds.Where(id => !inlineLabels.Contains(id)).ToHashSet(); var assigned = new List<string>(); var proposalLessons = new List<RecoveryLesson>();
            RecoveryField? ScopedField(int index, RecoveryFieldRole role)
            {
                var scopes = cell.RoleScopes.Where(s => s.LessonIndex == index && s.Role == role).ToArray(); if (scopes.Length != 1) return null; var scope = scopes[0];
                var atoms = sourceIndex.Cell(cell.Id).Where(s => { work.Step(); return bodyIds.Contains(s.Id) && s.Page == cell.Page && scope.Box.Contains(s.Box); }).ToArray();
                if (atoms.Length == 0) return role != RecoveryFieldRole.Subject && scope.EmptyVerified ? new(RecoveryValueState.Empty, "", []) : null;
                var ids = atoms.Select(s => s.Id).ToArray(); assigned.AddRange(ids); return new(RecoveryValueState.Present, work.Concat(atoms.Select(s => s.Text)), ids);
            }
            if (cell.ParallelCount is < 1 or > 4 || cell.RoleScopes.Count != cell.ParallelCount * 3) return null;
            for (var i = 0; i < cell.ParallelCount; i++)
            {
                var subject = ScopedField(i, RecoveryFieldRole.Subject); var teacher = ScopedField(i, RecoveryFieldRole.Teacher); var room = ScopedField(i, RecoveryFieldRole.Room);
                if (subject is null || teacher is null || room is null) return null;
                proposalLessons.Add(new(subject, teacher, room, cell.DayHeaderIds, cell.PeriodHeaderIds));
            }
            return assigned.Count == bodyIds.Count && assigned.Distinct().Count() == assigned.Count && assigned.ToHashSet().SetEquals(bodyIds) ? new(cell.Id, RecoveryValueState.Present, proposalLessons) : null;
        }
        if (cell.BindingMode != RecoveryBindingMode.Fixed || cell.LessonBindings.Count != cell.ParallelCount) return null;
        var sources = sourceIndex.ById;
        RecoveryField? Field(IReadOnlyList<string> ids, string name)
        {
            work.Step(ids.Count);
            if (ids.Count == 0) return name != "subject" && cell.BlankFields.Contains(name) ? new(RecoveryValueState.Empty, "", []) : null;
            if (ids.Any(id => !sources.TryGetValue(id, out var source) || source.CellId != cell.Id)) return null;
            return new(RecoveryValueState.Present, work.Concat(ids.Select(id => sources[id].Text)), ids);
        }
        var lessons = new List<RecoveryLesson>();
        foreach (var binding in cell.LessonBindings)
        {
            var subject = Field(binding.Subject, "subject"); var teacher = Field(binding.Teacher, "teacher"); var room = Field(binding.Room, "room");
            if (subject is null || teacher is null || room is null) return null;
            lessons.Add(new(subject, teacher, room, cell.DayHeaderIds, cell.PeriodHeaderIds));
        }
        return new(cell.Id, RecoveryValueState.Present, lessons);
    }
}
public static class RecoveryEngine
{
    private static RecoveryField Ground(RecoveryField field, RecoverySourceIndex index, RecoveryWorkBudget work)
    {
        work.Step(field.Evidence.Count); var sources = index.ById;
        if (field.Evidence.Any(id => !sources.ContainsKey(id))) throw new InvalidRecoveryOutputException();
        return field with { Value = field.State == RecoveryValueState.Empty ? "" : work.Concat(field.Evidence.Select(id => sources[id].Text)) };
    }
    // Deterministic field bindings are recovered before providers. Providers never receive original PDFs or page images.
    public static async Task<RecoveryRun> RunAsync(RecoveryDocument document, string os, int osMajor, bool foreground,
        IReadOnlyList<ILocalRecoveryProvider> providers, Func<RecoveryCell, RecoveredCell?> rule,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (os is "ios" or "android" && !foreground) return new(RecoveryJobState.Pending, null, []);
        if (!document.Complete || document.Cells.Any(c => c.InputState != RecoveryInputState.Complete)) return new(RecoveryJobState.Failed, null, ["incompleteDocument"]);
        var inputErrors = RecoveryValidator.InputErrors(document, token);
        if (inputErrors.Count != 0) return new(RecoveryJobState.Failed, null, inputErrors);
        var work = new RecoveryWorkBudget(token); var sourceIndex = new RecoverySourceIndex(document, work);
        RecoveredCell?[] recovered;
        try { recovered = document.Cells.Select(c => { token.ThrowIfCancellationRequested(); return c.ConfirmedEmpty && c.SourceIds.Count == 0 ? new RecoveredCell(c.Id, RecoveryValueState.Empty, []) : RecoveryRules.Recover(document, c, sourceIndex, work) ?? rule(c); }).ToArray(); }
        catch (RecoveryWorkLimitException) { return new(RecoveryJobState.Failed, null, ["validationLimit"]); }
        var missing = document.Cells.Select((cell, i) => (cell, i)).Where(pair => recovered[pair.i] is null).ToArray();
        RecoveryResult Result(RecoveryMetadata metadata) => new(document.PdfHash, document.Kind, document.SchoolYear, document.Term, recovered.Select(c => c!).ToArray(), metadata);
        if (missing.Length == 0)
        {
            var result = Result(document.StructureMetadata ?? new("rule", "rules", "3", "3", "1", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, os + ":" + osMajor));
            var validation = RecoveryValidator.Validate(document, result, token);
            token.ThrowIfCancellationRequested();
            return new(validation.CanAdopt ? RecoveryJobState.AwaitingConfirmation : RecoveryJobState.Failed, validation.CanAdopt ? result : null, validation.Errors);
        }
        var runtimeFailed = false;
        foreach (var id in RecoveryPolicy.Providers(os, osMajor))
        {
            token.ThrowIfCancellationRequested();
            var matching = providers.Where(p => p.Id == id && p.LocalOnly).ToArray();
            if (matching.Length > 1) return new(RecoveryJobState.Failed, null, ["duplicateProviders"]);
            var provider = matching.FirstOrDefault();
            if (provider is null) continue;
            LocalProviderState availability;
            try { availability = await provider.AvailabilityAsync(token); token.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException) { throw; }
            catch { runtimeFailed = true; continue; }
            if (availability != LocalProviderState.Ready)
            {
                if (RecoveryPolicy.MayTryNext(availability) || os == "windows" && availability == LocalProviderState.NotReady) continue;
                return new(RecoveryJobState.AwaitingModel, null, [availability.ToString()]);
            }
            try
            {
                foreach (var (cell, index) in missing)
                {
                    token.ThrowIfCancellationRequested();
                    if (cell.InputState != RecoveryInputState.Complete) return new(RecoveryJobState.Failed, null, ["incompleteCell"]);
                    var allowed = cell.SourceIds.ToHashSet();
                    var prompt = new RecoveryPromptCell(cell.Id, cell.Slots, sourceIndex.Cell(cell.Id).Where(s => allowed.Contains(s.Id)).Select(s => new RecoveryPromptSource(s.Id, s.Text, s.Box, s.Page)).ToArray(), cell.BlankFields, cell.ParallelCount, cell.LessonBindings, cell.RoleScopes);
                    if (JsonSerializer.SerializeToUtf8Bytes(prompt).Length > 8192) return new(RecoveryJobState.Failed, null, ["promptLimit"]);
                    var generated = await provider.RecoverCellAsync(prompt, token); token.ThrowIfCancellationRequested();
                    recovered[index] = new(cell.Id, RecoveryValueState.Present, generated.Select(l => l with {
                        Subject = cell.BindingMode == RecoveryBindingMode.RoleProposal ? Ground(l.Subject, sourceIndex, work) : l.Subject,
                        Teacher = cell.BindingMode == RecoveryBindingMode.RoleProposal ? Ground(l.Teacher, sourceIndex, work) : l.Teacher,
                        Room = cell.BindingMode == RecoveryBindingMode.RoleProposal ? Ground(l.Room, sourceIndex, work) : l.Room,
                        DateEvidence = cell.DayHeaderIds,
                        PeriodEvidence = cell.PeriodHeaderIds
                    }).ToArray());
                }
                var result = Result(provider.Metadata);
                var validation = RecoveryValidator.Validate(document, result, token);
                // A validation failure is a terminal rejection, not permission to keep sampling until one model passes.
                token.ThrowIfCancellationRequested();
            return new(validation.CanAdopt ? RecoveryJobState.AwaitingConfirmation : RecoveryJobState.Failed, validation.CanAdopt ? result : null, validation.Errors);
            }
            catch (OperationCanceledException) { throw; }
            catch (RecoveryWorkLimitException) { return new(RecoveryJobState.Failed, null, ["validationLimit"]); }
            catch (InvalidRecoveryOutputException) { return new(RecoveryJobState.Failed, null, ["invalidOutput"]); }
            catch { runtimeFailed = true; }
        }
        token.ThrowIfCancellationRequested();
        return new(runtimeFailed ? RecoveryJobState.Failed : RecoveryJobState.AwaitingModel, null, [runtimeFailed ? "runtimeFailure" : "noLocalProvider"]);
    }
}
