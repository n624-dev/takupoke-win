using System.Text.Json;
using Microsoft.AI.Foundry.Local.OpenAI;
using Takupoke.Core.Recovery;

// One public target role per call. No expected ID, body value, state or geometry
// is supplied by this protocol; all original IDs remain possible outputs.
internal static class SingleRoleProtocol
{
    internal const string Recipe = "single-header-role-original-ids-native-v1";
    internal const string InstructionSha256 = "4827fa46956a14d375792000e9bdba62a7c0bc153a19ceb4c6202063877debc7";
    private static readonly Lazy<string> SharedInstruction = new(() =>
    {
        using var stream = typeof(SingleRoleProtocol).Assembly.GetManifestResourceStream("RecoveryPrompts.SingleHeaderRoleJaV1")
            ?? throw new InvalidDataException("Shared HEAD instruction resource is missing.");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        var data = bytes.ToArray();
        if (data.Length != 1422 || Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data)) != InstructionSha256)
            throw new InvalidDataException("Shared HEAD instruction differs from the frozen native recipe.");
        return new System.Text.UTF8Encoding(false, true).GetString(data);
    });
    internal static string Instruction => SharedInstruction.Value;
    internal static string Input(RecoveryPromptCell cell, string role)
    {
        if (role is not ("subject" or "teacher" or "room")) throw new ArgumentException("Unknown public role.");
        using var original = JsonDocument.Parse(LabelSelectionProtocol.Input(cell));
        return JsonSerializer.Serialize(new { targetRole = role, cellData = original.RootElement }, LabelSelectionProtocol.ReadableOptions);
    }
    internal static ResponseFormatExtended Format(RecoveryPromptCell cell) => new()
    {
        Type = "lark_grammar",
        LarkGrammar = "start: \"{\" \"\\\"ids\\\"\" \":\" ids \"}\"\n" +
            string.Join('\n', LabelSelectionProtocol.Grammar(cell).Split('\n').Skip(1))
    };
    internal static string[] Decode(string text, RecoveryPromptCell cell)
    {
        if (text.Length > 16384) throw new InvalidRecoveryOutputException();
        try
        {
            using var parsed = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidRecoveryOutputException();
            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != 1 || properties[0].Name != "ids") throw new InvalidRecoveryOutputException();
            // Reuse the original strict type/count/ID/order/duplicate decoder.
            // Empty other-role arrays are only a decoder wrapper, never proposals.
            var wrapper = "{\"subject\":" + properties[0].Value.GetRawText() + ",\"teacher\":[],\"room\":[]}";
            return LabelSelectionProtocol.Decode(wrapper, cell)["subject"];
        }
        catch (JsonException) { throw new InvalidRecoveryOutputException(); }
    }
}
