using System.Text.Json;

namespace Takupoke.Core.Recovery;

public sealed record RecoveryPromptCell(string CellId, IReadOnlyList<RecoverySlot> Slots, IReadOnlyList<RecoveryPromptSource> Sources,
    IReadOnlyList<string> BlankFields, int ParallelCount, IReadOnlyList<RecoveryLessonBinding> LessonBindings, IReadOnlyList<RecoveryRoleScope>? RoleScopes = null);
public sealed record RecoveryPromptSource(string Id, string Text, RecoveryBox? Box = null, int? Page = null);
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
    public static RecoveredCell? Recover(RecoveryDocument doc, RecoveryCell cell)
    {
        if (cell.BindingMode != RecoveryBindingMode.Fixed || cell.InputState != RecoveryInputState.Complete || cell.ConfirmedEmpty || cell.LessonBindings.Count != cell.ParallelCount) return null;
        var sources = doc.Sources.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        RecoveryField? Field(IReadOnlyList<string> ids, string name)
        {
            if (ids.Count == 0) return name != "subject" && cell.BlankFields.Contains(name) ? new(RecoveryValueState.Empty, "", []) : null;
            if (ids.Any(id => !sources.TryGetValue(id, out var source) || source.CellId != cell.Id)) return null;
            return new(RecoveryValueState.Present, string.Concat(ids.Select(id => sources[id].Text)), ids);
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
    private static RecoveryField Ground(RecoveryField field, RecoveryDocument document)
    {
        var sources = document.Sources.ToDictionary(s => s.Id);
        if (field.Evidence.Any(id => !sources.ContainsKey(id))) throw new InvalidRecoveryOutputException();
        return field with { Value = field.State == RecoveryValueState.Empty ? "" : string.Concat(field.Evidence.Select(id => sources[id].Text)) };
    }
    // Deterministic field bindings are recovered before providers. Providers never receive original PDFs or page images.
    public static async Task<RecoveryRun> RunAsync(RecoveryDocument document, string os, int osMajor, bool foreground,
        IReadOnlyList<ILocalRecoveryProvider> providers, Func<RecoveryCell, RecoveredCell?> rule,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (os is "ios" or "android" && !foreground) return new(RecoveryJobState.Pending, null, []);
        if (!document.Complete || document.Cells.Any(c => c.InputState != RecoveryInputState.Complete)) return new(RecoveryJobState.Failed, null, ["incompleteDocument"]);
        var inputErrors = RecoveryValidator.InputErrors(document);
        if (inputErrors.Count != 0) return new(RecoveryJobState.Failed, null, inputErrors);
        var recovered = document.Cells.Select(c => { token.ThrowIfCancellationRequested(); return c.ConfirmedEmpty && c.SourceIds.Count == 0 ? new RecoveredCell(c.Id, RecoveryValueState.Empty, []) : RecoveryRules.Recover(document, c) ?? rule(c); }).ToArray();
        var missing = document.Cells.Select((cell, i) => (cell, i)).Where(pair => recovered[pair.i] is null).ToArray();
        RecoveryResult Result(RecoveryMetadata metadata) => new(document.PdfHash, document.Kind, document.SchoolYear, document.Term, recovered.Select(c => c!).ToArray(), metadata);
        if (missing.Length == 0)
        {
            var result = Result(new("rule", "rules", "1", "1", "1", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, os + ":" + osMajor));
            var validation = RecoveryValidator.Validate(document, result);
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
                    var prompt = new RecoveryPromptCell(cell.Id, cell.Slots, document.Sources.Where(s => allowed.Contains(s.Id)).Select(s => new RecoveryPromptSource(s.Id, s.Text, s.Box, s.Page)).ToArray(), cell.BlankFields, cell.ParallelCount, cell.LessonBindings, cell.RoleScopes);
                    if (JsonSerializer.SerializeToUtf8Bytes(prompt).Length > 8192) return new(RecoveryJobState.Failed, null, ["promptLimit"]);
                    var generated = await provider.RecoverCellAsync(prompt, token); token.ThrowIfCancellationRequested();
                    recovered[index] = new(cell.Id, RecoveryValueState.Present, generated.Select(l => l with {
                        Subject = cell.BindingMode == RecoveryBindingMode.RoleProposal ? Ground(l.Subject, document) : l.Subject,
                        Teacher = cell.BindingMode == RecoveryBindingMode.RoleProposal ? Ground(l.Teacher, document) : l.Teacher,
                        Room = cell.BindingMode == RecoveryBindingMode.RoleProposal ? Ground(l.Room, document) : l.Room,
                        DateEvidence = cell.DayHeaderIds,
                        PeriodEvidence = cell.PeriodHeaderIds
                    }).ToArray());
                }
                var result = Result(provider.Metadata);
                var validation = RecoveryValidator.Validate(document, result);
                // A validation failure is a terminal rejection, not permission to keep sampling until one model passes.
                token.ThrowIfCancellationRequested();
            return new(validation.CanAdopt ? RecoveryJobState.AwaitingConfirmation : RecoveryJobState.Failed, validation.CanAdopt ? result : null, validation.Errors);
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidRecoveryOutputException) { return new(RecoveryJobState.Failed, null, ["invalidOutput"]); }
            catch { runtimeFailed = true; }
        }
        token.ThrowIfCancellationRequested();
        return new(runtimeFailed ? RecoveryJobState.Failed : RecoveryJobState.AwaitingModel, null, [runtimeFailed ? "runtimeFailure" : "noLocalProvider"]);
    }
}
