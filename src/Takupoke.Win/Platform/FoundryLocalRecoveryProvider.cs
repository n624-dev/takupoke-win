using System.Text.Json;
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
    private bool _loaded;
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
        if (await AvailabilityAsync(token) != LocalProviderState.Ready) throw new InvalidDataException("検証済みの端末内モデルが必要です。");
        if (!_loaded) { await model.LoadAsync(token); _loaded = true; }
        var client = await model.GetChatClientAsync(token);
        var instruction = RecoveryStructure.Instruction(cell);
        var completion = await client.CompleteChatAsync(new[] { new ChatMessage { Role = "system", Content = instruction }, new ChatMessage { Role = "user", Content = JsonSerializer.Serialize(cell, DataCodec.Options) } }, token);
        token.ThrowIfCancellationRequested();
        if (completion.Choices.Count != 1 || completion.Choices[0].Message is null) throw new InvalidRecoveryOutputException();
        var text = completion.Choices[0].Message.Content;
        if (text is null || text.Length > 16384) throw new InvalidRecoveryOutputException();
        return RecoveryOutputDecoder.Decode(text);
    }
    public async ValueTask DisposeAsync() { if (_loaded) { await model.UnloadAsync(); _loaded = false; } }
}
