using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Microsoft.AI.Foundry.Local;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Infrastructure.Recovery;

namespace Takupoke.Win.Platform;

/// The caller supplies a pinned, locally cached variant and verifies all its artifacts.
/// This provider never downloads an alias, updates a catalog or starts a web service.
public sealed class FoundryLocalRecoveryProvider(IModel model, RecoveryModelManifest manifest,
    Func<CancellationToken, Task<bool>> verifyLocalArtifacts, long availableMemory) : ILocalRecoveryProvider, IAsyncDisposable
{
    private static readonly JsonSerializerOptions ReadableOptions = new(DataCodec.Options) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
    private bool _loaded;
    internal bool ClearerPrompt { get; set; }
    internal bool SingleRoleMode { get; set; }
    internal bool AllRoleSelectionsDecoded { get; private set; }
    internal List<SingleRoleObservation> RoleObservations { get; } = [];
    internal Action<SingleRoleObservation>? RoleCompleted { get; set; }
    internal string? LastRawOutput { get; private set; }
    internal int? LastRawOriginalLength { get; private set; }
    internal bool LastRawTruncated { get; private set; }
    internal IReadOnlyDictionary<string, string[]>? LastSelection { get; private set; }
    internal string LastStage { get; private set; } = "not-called";
    internal int NativeCompletionsStarted { get; private set; }
    internal int NativeCompletionsReturned { get; private set; }
    internal void ResetObservation() { LastRawOutput = null; LastRawOriginalLength = null; LastRawTruncated = false; LastSelection = null; LastStage = "not-called"; AllRoleSelectionsDecoded = false; RoleObservations.Clear(); }
    public string Id => "foundryLocal";
    public bool LocalOnly => true;
    public RecoveryMetadata Metadata => new(Id, manifest.ModelId, manifest.Version, "Foundry.Local.WinML:1.2.4", "3", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, Environment.OSVersion.VersionString);
    public async Task<LocalProviderState> AvailabilityAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!manifest.SupportsOs(Environment.OSVersion.Version.ToString())) return LocalProviderState.Unsupported;
        if (!manifest.Validated || manifest.Runtime != Id) return LocalProviderState.DownloadRequired;
        if (!manifest.IsUsable(Id, availableMemory)) return LocalProviderState.InsufficientMemory;
        if (model.Id != manifest.ModelId || !await verifyLocalArtifacts(token)) return LocalProviderState.DownloadRequired;
        return LocalProviderState.Ready;
    }
    public async Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell, CancellationToken token)
    {
        ResetObservation(); LastStage = "readiness";
        if (await AvailabilityAsync(token) != LocalProviderState.Ready) throw new InvalidDataException("検証済みの端末内モデルが必要です。");
        if (!_loaded) { await model.LoadAsync(token); _loaded = true; }
        var client = await model.GetChatClientAsync(token);
        if (SingleRoleMode) return await RecoverSingleRolesAsync(cell, token);
        client.Settings.Temperature = 0;
        client.Settings.RandomSeed = 17;
        client.Settings.MaxTokens = 512;
        client.Settings.ResponseFormat = LabelSelectionProtocol.Format(cell);
        // Foundry's no-tools JSON recommendation; isolate this setting from v1.
        client.Settings.ToolChoice = ToolChoice.None;
        LastStage = "native-generation";
        NativeCompletionsStarted++;
        var completion = await client.CompleteChatAsync(new[] { new ChatMessage { Role = "system", Content = ClearerPrompt ? LabelSelectionProtocol.ClearInstruction : LabelSelectionProtocol.Instruction },
            new ChatMessage { Role = "user", Content = LabelSelectionProtocol.Input(cell) } }, token);
        NativeCompletionsReturned++;
        token.ThrowIfCancellationRequested();
        if (completion.Choices.Count != 1 || completion.Choices[0].Message is null) throw new InvalidRecoveryOutputException();
        var text = completion.Choices[0].Message.Content;
        LastRawOriginalLength = text?.Length; LastRawTruncated = text is { Length: > 16384 };
        LastRawOutput = text is null ? null : text[..Math.Min(text.Length, 16384)];
        if (text is null || text.Length > 16384) throw new InvalidRecoveryOutputException();
        LastStage = "label-decoder";
        LastSelection = LabelSelectionProtocol.Decode(text, cell);
        AllRoleSelectionsDecoded = true;
        LastStage = "measured-cut-reconstruction";
        var proposal = LabelSelectionProtocol.Proposal(cell, LastSelection);
        LastStage = "proposal-constructed-awaiting-production-certificate";
        return [proposal];
    }
    private async Task<IReadOnlyList<RecoveryLesson>> RecoverSingleRolesAsync(RecoveryPromptCell cell, CancellationToken token)
    {
        var selections = new Dictionary<string, string[]>(); Exception? runtimeFailure = null; var invalidOutput = false;
        foreach (var role in new[] { "subject", "teacher", "room" })
        {
            token.ThrowIfCancellationRequested();
            LastRawOutput = null; LastRawOriginalLength = null; LastRawTruncated = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(180));
            var watch = System.Diagnostics.Stopwatch.StartNew(); string? text = null; string[]? selected = null;
            var started = NativeCompletionsStarted; var returned = NativeCompletionsReturned;
            try
            {
                var client = await model.GetChatClientAsync(deadline.Token);
                client.Settings.Temperature = 0; client.Settings.RandomSeed = 17; client.Settings.MaxTokens = 512;
                client.Settings.ResponseFormat = SingleRoleProtocol.Format(cell); client.Settings.ToolChoice = ToolChoice.None;
                LastStage = "single-role-native-generation:" + role; NativeCompletionsStarted++;
                var completion = await client.CompleteChatAsync(new[] { new ChatMessage { Role = "system", Content = SingleRoleProtocol.Instruction },
                    new ChatMessage { Role = "user", Content = SingleRoleProtocol.Input(cell, role) } }, deadline.Token);
                NativeCompletionsReturned++;
                if (completion.Choices.Count != 1 || completion.Choices[0].Message is null) throw new InvalidRecoveryOutputException();
                text = completion.Choices[0].Message.Content;
                LastRawOriginalLength = text?.Length; LastRawTruncated = text is { Length: > 16384 }; LastRawOutput = text is null ? null : text[..Math.Min(text.Length, 16384)];
                deadline.Token.ThrowIfCancellationRequested();
                LastStage = "single-role-id-decoder:" + role;
                if (text is null) throw new InvalidRecoveryOutputException();
                selected = SingleRoleProtocol.Decode(text, cell); selections.Add(role, selected);
                RoleObservations.Add(new(role, LastRawOutput, text.Length, LastRawTruncated, selected, null, null, false,
                    NativeCompletionsStarted - started, NativeCompletionsReturned - returned, watch.ElapsedMilliseconds));
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                var operational = error is not InvalidRecoveryOutputException;
                if (operational) runtimeFailure ??= error; else invalidOutput = true;
                RoleObservations.Add(new(role, text is null ? null : text[..Math.Min(text.Length, 16384)], text?.Length, text is { Length: > 16384 }, selected,
                    error.GetType().Name, error.Message[..Math.Min(error.Message.Length, 1024)], operational,
                    NativeCompletionsStarted - started, NativeCompletionsReturned - returned, watch.ElapsedMilliseconds));
                // No retry or answer substitution. Remaining roles still receive
                // their one scheduled call after a local role failure.
            }
            RoleCompleted?.Invoke(RoleObservations[^1]);
        }
        LastSelection = selections;
        if (runtimeFailure is not null) throw new InvalidDataException("One or more scheduled header calls failed operationally.", runtimeFailure);
        if (invalidOutput) throw new InvalidRecoveryOutputException();
        LastStage = "merged-id-ownership-decoder";
        LastSelection = LabelSelectionProtocol.Decode(JsonSerializer.Serialize(selections), cell); AllRoleSelectionsDecoded = true;
        LastStage = "measured-cut-reconstruction";
        var proposal = LabelSelectionProtocol.Proposal(cell, LastSelection);
        LastStage = "proposal-constructed-awaiting-production-certificate";
        return [proposal];
    }
    public async ValueTask DisposeAsync() { if (_loaded) { await model.UnloadAsync(); _loaded = false; } }
}
internal sealed record SingleRoleObservation(string Role, string? RawOutput, int? OriginalLength, bool Truncated, string[]? SelectedIds,
    string? ErrorType, string? ErrorMessage, bool OperationalError, int NativeStarted, int NativeReturned, long Milliseconds);
