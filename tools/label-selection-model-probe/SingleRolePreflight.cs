using System.Text.Json;
using Takupoke.Core.Recovery;

internal static class SingleRolePreflight
{
    internal static object Run(IReadOnlyList<QualificationCase> corpus)
    {
        var decoded = 0; var rejected = 0; var certificates = 0;
        foreach (var c in corpus.Where(c => c.ShouldAdopt))
        {
            var cell = c.Document.Cells.Single(v => v.Id == c.CellId); var prompt = RecoveryStructure.Prompt(c.Document, cell);
            var oracle = QualificationCorpus.Oracle(prompt);
            var expected = new Dictionary<string, string[]> { ["subject"] = oracle.Subject.Evidence.SkipLast(3).ToArray(),
                ["teacher"] = oracle.Teacher.Evidence.SkipLast(3).ToArray(), ["room"] = oracle.Room.Evidence.SkipLast(3).ToArray() };
            var selected = new Dictionary<string, string[]>();
            foreach (var (role, ids) in expected)
            {
                var json = JsonSerializer.Serialize(new { ids });
                selected.Add(role, SingleRoleProtocol.Decode(json, prompt)); decoded++;
                using var input = JsonDocument.Parse(SingleRoleProtocol.Input(prompt, role));
                using var original = JsonDocument.Parse(LabelSelectionProtocol.Input(prompt));
                if (input.RootElement.GetProperty("targetRole").GetString() != role ||
                    input.RootElement.GetProperty("cellData").GetRawText() != original.RootElement.GetRawText())
                    throw new InvalidDataException("Single-role input changed frozen sources or public labels.");
                var bad = new[] { "[\"" + ids[0] + "\"]", "```json\n" + json + "\n```", "{\"ids\":[],\"ids\":[]}",
                    "{\"ids\":[],\"state\":\"EMPTY\"}", "{\"ids\":null}", "{\"ids\":[0]}", "{\"ids\":[\"foreign\"]}",
                    JsonSerializer.Serialize(new { ids = ids.Concat(ids) }) };
                foreach (var text in bad)
                {
                    try { SingleRoleProtocol.Decode(text, prompt); throw new InvalidDataException("Malformed single-role output accepted."); }
                    catch (InvalidRecoveryOutputException) { rejected++; }
                }
                if (ids.Length > 1)
                {
                    try { SingleRoleProtocol.Decode(JsonSerializer.Serialize(new { ids = ids.Reverse() }), prompt); throw new InvalidDataException("Reordered IDs accepted."); }
                    catch (InvalidRecoveryOutputException) { rejected++; }
                }
                if (SingleRoleProtocol.Decode("{\"ids\":[]}", prompt).Length != 0) throw new InvalidDataException("Empty selection changed.");
            }
            var merged = LabelSelectionProtocol.Decode(JsonSerializer.Serialize(selected), prompt);
            var proposed = LabelSelectionProtocol.Proposal(prompt, merged);
            RecoveryStructure.Verify(c.Document, cell, prompt, [proposed]); certificates++;
            var grammar = SingleRoleProtocol.Format(prompt).LarkGrammar;
            var remainder = string.Join('\n', LabelSelectionProtocol.Grammar(prompt).Split('\n').Skip(1));
            if (grammar != "start: \"{\" \"\\\"ids\\\"\" \":\" ids \"}\"\n" + remainder)
                throw new InvalidDataException("Single-role grammar changed shared all-ID/count productions.");
        }
        return new { recipe = SingleRoleProtocol.Recipe, decodedRoles = decoded, malformedOrReorderedRejected = rejected, mergedCertificates = certificates,
            nativeCalls = 0, scope = "Deterministic scorer/input/grammar/certificate controls only; no native support or model quality claim" };
    }
}
