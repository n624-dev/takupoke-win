using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Infrastructure.Recovery;

namespace Takupoke.Win.Platform;

/// Uses the OS language-model abstraction rather than a fixed Phi/Aion model name.
public sealed class WindowsLanguageRecoveryProvider : ILocalRecoveryProvider
{
    public string Id => "windowsLanguageModel";
    public bool LocalOnly => true;
    public RecoveryMetadata Metadata => new(Id, "Windows.LanguageModel", "os-managed", "WindowsAppSDK:2.5.1", RecoveryPromptInstructions.FieldExtractionVersion, RecoveryValidator.SchemaVersion, RecoveryValidator.Version, Environment.OSVersion.VersionString);
    public Task<LocalProviderState> AvailabilityAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(LanguageModel.GetReadyState() switch
            { AIFeatureReadyState.Ready => LocalProviderState.Ready, AIFeatureReadyState.NotReady => LocalProviderState.NotReady, _ => LocalProviderState.Unsupported });
        }
        catch { return Task.FromResult(LocalProviderState.Unsupported); }
    }
    public async Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // EnsureReadyAsync may download gigabytes. This provider never calls it implicitly.
        using var model = await LanguageModel.CreateAsync().AsTask(token);
        var prompt = RecoveryStructure.Instruction(cell) + "\n" + RecoveryPromptInstructions.Input(cell, DataCodec.Options);
        var response = await model.GenerateResponseAsync(prompt).AsTask(token);
        token.ThrowIfCancellationRequested();
        if (response.Text.Length > 16384) throw new InvalidRecoveryOutputException();
        return RecoveryOutputDecoder.Decode(response.Text);
    }
}
