using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Takupoke.Core.Recovery;

internal static class SingleRoleBaseline
{
    private static JsonDocument Read() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "single-role-baseline.json")));
    internal static void VerifyCorpus(IReadOnlyList<QualificationCase> corpus)
    {
        using var baseline = Read(); var root = baseline.RootElement;
        var entries = root.GetProperty("corpusManifest").EnumerateArray().ToArray();
        if (entries.Length != corpus.Count || entries.Select(e => e.GetProperty("Id").GetString()).Distinct().Count() != corpus.Count)
            throw new InvalidDataException("Historical corpus manifest mismatch.");
        foreach (var c in corpus)
        {
            var entry = entries.Single(e => e.GetProperty("Id").GetString() == c.Id);
            if (entry.GetProperty("ShouldAdopt").GetBoolean() != c.ShouldAdopt || entry.GetProperty("sourceHash").GetString() != RecoveryValidator.Fingerprint(c.Document))
                throw new InvalidDataException("Single-role comparison changed historical inputs.");
        }
        if (root.GetProperty("systemInstructionSha256").GetString() != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(LabelSelectionProtocol.ClearInstruction))))
            throw new InvalidDataException("Historical shared-JP instruction changed.");
    }
    internal static void VerifyArtifacts(string modelId, IReadOnlyList<Artifact> artifacts)
    {
        using var baseline = Read(); var root = baseline.RootElement;
        var expected = root.GetProperty("artifacts").Deserialize<Artifact[]>()!;
        if (root.GetProperty("modelId").GetString() != modelId || !artifacts.SequenceEqual(expected))
            throw new InvalidDataException("Downloaded artifacts differ from the historical pinned baseline.");
    }
}
