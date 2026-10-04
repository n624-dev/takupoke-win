using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

// Explicit isolated model correctness probe. It does not claim that these
// rule-resolvable documents need generation in the actual application.
internal static class DirectNativeProposal
{
    internal static readonly JsonSerializerOptions ReadableOptions = new(DataCodec.Options) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
    internal static async Task<RecoveryStructureRun> RunAsync(RecoveryDocument doc, ILocalRecoveryProvider provider, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var preparation = await RecoveryStructure.ResolveAsync(doc, "windows", 10, [], token);
        if (preparation.State == RecoveryJobState.Failed) return preparation;
        try
        {
            foreach (var cell in doc.Cells.Where(RecoveryStructure.Pending))
            {
                var prompt = RecoveryStructure.Prompt(doc, cell);
                var generated = await provider.RecoverCellAsync(prompt, token);
                var scopes = RecoveryStructure.Verify(doc, cell, prompt, generated);
                doc = doc with { Cells = doc.Cells.Select(v => v.Id == cell.Id ? v with { RoleScopes = scopes } : v).ToArray() };
            }
            var metadata = provider.Metadata with { PromptVersion = "3", RecoveryVersion = "2" };
            doc = doc with { StructureMetadata = metadata }; token.ThrowIfCancellationRequested();
            var validated = RecoveryValidator.InputErrors(doc, token);
            return validated.Count == 0 ? new(doc, metadata, RecoveryJobState.Running, []) : new(null, null, RecoveryJobState.Failed, validated);
        }
        catch (InvalidRecoveryOutputException) { return new(null, null, RecoveryJobState.Failed, ["invalidStructureOutput"]); }
    }
}
