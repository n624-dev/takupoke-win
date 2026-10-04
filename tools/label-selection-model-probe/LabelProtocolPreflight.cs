using System.Text.Json;
using Takupoke.Core.Recovery;

// Fictional deterministic adapter checks only; these never count as model calls.
internal static class LabelProtocolPreflight
{
    internal static async Task<object> RunAsync(IReadOnlyList<QualificationCase> corpus)
    {
        var completed = 0; var strictRejections = 0; var certificatesRejected = 0;
        foreach (var c in corpus.Where(c => c.ShouldAdopt))
        {
            var cell = c.Document.Cells.Single(v => v.Id == c.CellId); var prompt = RecoveryStructure.Prompt(c.Document, cell);
            var oracle = QualificationCorpus.Oracle(prompt);
            var gold = new Dictionary<string, string[]> { ["subject"] = oracle.Subject.Evidence.SkipLast(3).ToArray(),
                ["teacher"] = oracle.Teacher.Evidence.SkipLast(3).ToArray(), ["room"] = oracle.Room.Evidence.SkipLast(3).ToArray() };
            var json = JsonSerializer.Serialize(gold);
            var selected = LabelSelectionProtocol.Decode(json, prompt);
            var proposed = LabelSelectionProtocol.Proposal(prompt, selected);
            var scopes = RecoveryStructure.Verify(c.Document, cell, prompt, [proposed]);
            var rebuilt = c.Document with { Cells = c.Document.Cells.Select(v => v.Id == cell.Id ? v with { RoleScopes = scopes } : v).ToArray() };
            var result = await RecoveryEngine.RunAsync(rebuilt, "windows", 10, true, [], _ => null);
            if (result.Result is null || !RecoveryValidator.Validate(rebuilt, result.Result).CanAdopt) throw new InvalidDataException("Adapter positive certificate preflight failed.");
            var format = LabelSelectionProtocol.Format(prompt);
            var idRule = "id: " + string.Join(" | ", prompt.Sources.Select(s => JsonSerializer.Serialize(JsonSerializer.Serialize(s.Id))));
            if (format.Type != "lark_grammar" || format.JsonSchema is not null || format.LarkGrammar is not { } grammar ||
                !grammar.Split('\n').Contains(idRule) || !grammar.Contains("ids: \"[\" [id (\",\" id)~0..47] \"]\"", StringComparison.Ordinal))
                throw new InvalidDataException("The shared grammar failed the complete same-source-ID requirement.");
            var malformed = new[] { "```json\n" + json + "\n```", json[..^1] + ",\"subject\":[]}",
                json[..^1] + ",\"other\":[]}", "{\"subject\":null,\"teacher\":[],\"room\":[]}",
                JsonSerializer.Serialize(gold.ToDictionary(p => p.Key, p => p.Key == "subject" ? new[] { "unknown-source" } : p.Value)),
                JsonSerializer.Serialize(gold.ToDictionary(p => p.Key, p => p.Key == "teacher" ? p.Value.Reverse().ToArray() : p.Value)),
                JsonSerializer.Serialize(gold.ToDictionary(p => p.Key, p => p.Key == "subject" ? p.Value.Concat(p.Value).ToArray() : p.Value)),
                JsonSerializer.Serialize(gold.ToDictionary(p => p.Key, p => p.Key == "teacher" ? gold["subject"] : p.Value)) };
            foreach (var bad in malformed)
            {
                try { LabelSelectionProtocol.Decode(bad, prompt); throw new InvalidDataException("Malformed model label selection was accepted."); }
                catch (InvalidRecoveryOutputException) { strictRejections++; }
            }
            // Correct vocabulary for the other role is still a wrong assignment;
            // generic schema admits it, and the unchanged certificate rejects it.
            var swapped = new Dictionary<string, string[]> { ["subject"] = gold["room"], ["teacher"] = gold["teacher"], ["room"] = gold["subject"] };
            var wrong = LabelSelectionProtocol.Proposal(prompt, LabelSelectionProtocol.Decode(JsonSerializer.Serialize(swapped), prompt));
            try { RecoveryStructure.Verify(c.Document, cell, prompt, [wrong]); throw new InvalidDataException("Swapped role labels passed the production certificate."); }
            catch (InvalidRecoveryOutputException) { certificatesRejected++; }
            completed++;
        }
        return new { recipe = LabelSelectionProtocol.Recipe, positiveAdapterCertificates = completed, malformedSelectionsRejected = strictRejections,
            swappedRolesCertificateRejected = certificatesRejected, modelCalls = 0, grammarScope = "Every role array permits the same complete supplied source-ID enum; no expected answer constraints",
            scope = "Deterministic fictional adapter preflight only; native structured-generation support remains unverified" };
    }
}
