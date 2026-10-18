using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Takupoke.Core.Recovery;

/// The task instruction is shared byte-for-byte across OS providers. Native
/// output types and state spelling remain each application's existing contract.
public static class RecoveryPromptInstructions
{
    public const string FieldExtractionVersion = "4";
    public const string FieldExtractionSha256 = "c24039ae4317a433a14f01697d77813424a3a1c20a70327189964b2fc60bb188";
    private static readonly Lazy<string> FieldInstruction = new(ReadFieldInstruction);
    public static string FieldExtraction => FieldInstruction.Value;

    private static string ReadFieldInstruction()
    {
        using var resource = typeof(RecoveryPromptInstructions).Assembly.GetManifestResourceStream("Takupoke.Core.Recovery.Prompts.field-extraction-v4.txt")
            ?? throw new InvalidDataException("The shared field extraction instruction is missing.");
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        var content = bytes.ToArray();
        if (Convert.ToHexStringLower(SHA256.HashData(content)) != FieldExtractionSha256)
            throw new InvalidDataException("The shared field extraction instruction has changed.");
        return new UTF8Encoding(false, true).GetString(content);
    }

    public static string Input(RecoveryPromptCell cell, JsonSerializerOptions options)
    {
        if (cell.Mode != "fieldExtraction") return JsonSerializer.Serialize(cell, options);
        var input = JsonSerializer.SerializeToNode(cell, options) as JsonObject
            ?? throw new InvalidDataException("The recovery cell could not be serialized.");
        // Foundry's existing production call has no native response schema.
        // Supply only its existing public lowercase enum spellings as data.
        input["responseStateTokens"] = JsonSerializer.SerializeToNode(new[] { "present", "empty", "unreadable", "missing", "ambiguous" });
        return input.ToJsonString(options);
    }
}
