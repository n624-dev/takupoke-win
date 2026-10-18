using System.Security.Cryptography;
using System.Text;
using Takupoke.Core.Recovery;

// Historical instruction3 recipe pins from immutable f0eca36. A newer Core
// instruction must never silently run under this legacy experiment's metadata.
internal static class LegacyInstructionGuard
{
    private const string FieldSha256 = "701f2db71351ab60921d6974a6abe83ca2ff0b08f75021c2fc2cf81e0db75303";
    private const string StructureSha256 = "2b5d37a40a44d0954982fbf1183294cca8883904b3a1b7f34d5871ba84cc0ca3";
    internal static void EnsureCompatible()
    {
        var cell = new RecoveryPromptCell("legacy-pin", [], [], [], 1, []);
        Verify(cell);
        Verify(cell with { Mode = "structureProposal" });
    }
    internal static void Verify(RecoveryPromptCell cell)
    {
        var expected = cell.Mode == "structureProposal" ? StructureSha256 : FieldSha256;
        var actual = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(RecoveryStructure.Instruction(cell))));
        if (actual != expected) throw new InvalidDataException("Legacy instruction3 recipe mismatch for " + cell.Mode + "; use its immutable historical source. Expected SHA256 " + expected + ", actual " + actual + ".");
    }
}
