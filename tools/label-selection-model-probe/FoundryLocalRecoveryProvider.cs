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
    internal string? LastRawOutput { get; private set; }
    internal int? LastRawOriginalLength { get; private set; }
    internal bool LastRawTruncated { get; private set; }
    internal IReadOnlyDictionary<string, string[]>? LastSelection { get; private set; }
    internal string LastStage { get; private set; } = "not-called";
    internal int NativeCompletionsStarted { get; private set; }
    internal int NativeCompletionsReturned { get; private set; }
    internal void ResetObservation() { LastRawOutput = null; LastRawOriginalLength = null; LastRawTruncated = false; LastSelection = null; LastStage = "not-called"; }
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
        client.Settings.Temperature = 0;
        client.Settings.RandomSeed = 17;
        client.Settings.MaxTokens = 512;
        client.Settings.ResponseFormat = LabelSelectionProtocol.Format(cell);
        LastStage = "native-generation";
        NativeCompletionsStarted++;
        var completion = await client.CompleteChatAsync(new[] { new ChatMessage { Role = "system", Content = LabelSelectionProtocol.Instruction },
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
        LastStage = "measured-cut-reconstruction";
        var proposal = LabelSelectionProtocol.Proposal(cell, LastSelection);
        LastStage = "proposal-constructed-awaiting-production-certificate";
        return [proposal];
    }
    public async ValueTask DisposeAsync() { if (_loaded) { await model.UnloadAsync(); _loaded = false; } }
}
