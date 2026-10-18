using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Takupoke.Core.Recovery;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class RecoveryPromptInstructionsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private static RecoveryPromptCell Cell() => new("fictional-cell", [],
        [new("z", "架空教員B", new(1, 2, 3, 4), SourceOrder: 9), new("a", "架空室C", new(5, 6, 7, 8), SourceOrder: 1)],
        ["room"], 1, [new([], ["z"], ["a"])],
        [new(0, RecoveryFieldRole.Room, 1, new(5, 6, 7, 8), [], new(1, new(1, 1, 1, 1), RecoveryHeaderAxis.Above), RecoveryRoleProof.ColumnHeader, true)]);

    [Fact] public void FieldInstructionIsPackagedAsTheExactSharedUtf8Asset()
    {
        var instruction = RecoveryStructure.Instruction(Cell());
        Assert.Equal("c24039ae4317a433a14f01697d77813424a3a1c20a70327189964b2fc60bb188",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(instruction))));
        Assert.Equal("4", RecoveryPromptInstructions.FieldExtractionVersion);
        Assert.Contains("emptyVerified=true", instruction);
        Assert.Contains("responseStateTokens", instruction);
        Assert.Contains("For every state other than PRESENT, return value=\"\" and evidence=[]", instruction);
    }
    [Fact] public void FieldInputAddsOnlyPublicStateTokensAndPreservesOriginalDataOrder()
    {
        var cell = Cell();
        var expected = JsonSerializer.SerializeToNode(cell, Options)!.AsObject();
        expected["responseStateTokens"] = JsonSerializer.SerializeToNode(new[] { "present", "empty", "unreadable", "missing", "ambiguous" });
        var actual = JsonNode.Parse(RecoveryPromptInstructions.Input(cell, Options))!.AsObject();
        Assert.True(JsonNode.DeepEquals(expected, actual));
        Assert.Equal(new[] { "z", "a" }, actual["sources"]!.AsArray().Select(s => s!["id"]!.GetValue<string>()));
    }
    [Fact] public void StructureInstructionAndInputKeepTheirExistingContract()
    {
        var cell = Cell() with { Mode = "structureProposal" };
        Assert.StartsWith("Propose only the structure", RecoveryStructure.Instruction(cell));
        Assert.DoesNotContain("responseStateTokens", RecoveryStructure.Instruction(cell));
        Assert.Equal(JsonSerializer.Serialize(cell, Options), RecoveryPromptInstructions.Input(cell, Options));
    }
}
