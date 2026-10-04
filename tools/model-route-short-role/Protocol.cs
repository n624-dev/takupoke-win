using System.Text.Json;

internal sealed record ProbeCase(string Id, string Task, string System, string Input, string? Grammar, string Expected);
internal static class Protocol
{
    internal const string Recipe = "windows-route-short-role-diagnostic-v1";
    internal const string RoleInstruction = "見出しの役割を分類してください。科目名の見出しはsubject、担当教員の見出しはteacher、教室の見出しはroom、どれでもない見出しはunknown。JSONオブジェクトのroleだけを返してください。説明は禁止。";
    internal const string IdInstruction = "入力の見出しから担当教員を示す見出しを1つ選んでください。元のidをJSONオブジェクトのids配列で返してください。説明は禁止。";
    private const string IdInput = "{\"sources\":[{\"id\":\"u0\",\"text\":\"科目：\"},{\"id\":\"u1\",\"text\":\"担当教員：\"}]}";
    internal static string IdGrammar(bool reverse, bool bounded)
    {
        var alternatives = reverse ? new[] { "u1", "u0" } : new[] { "u0", "u1" };
        var result = "start: \"{\" \"\\\"ids\\\"\" \":\" ids \"}\"\nid: " + string.Join(" | ", alternatives.Select(id => JsonSerializer.Serialize(JsonSerializer.Serialize(id)))) + "\n";
        if (bounded)
        {
            result += "ids: \"[\" [items_1] \"]\"\n";
            for (var n = 1; n <= 48; n++) result += $"items_{n}: id" + (n < 48 ? $" [\",\" items_{n + 1}]" : "") + "\n";
        }
        else result += "ids: \"[\" id \"]\"\n";
        return result + "%import common.WS\n%ignore WS\n";
    }
    internal static readonly string RoleGrammar = "start: \"{\" \"\\\"role\\\"\" \":\" role \"}\"\nrole: \"\\\"subject\\\"\" | \"\\\"teacher\\\"\" | \"\\\"room\\\"\" | \"\\\"unknown\\\"\"\n%import common.WS\n%ignore WS\n";
    internal static ProbeCase[] Cases()
    {
        var cases = new List<ProbeCase> { new("literal", "routing", "", "Return exactly the word unrestricted.", "start: " + JsonSerializer.Serialize("{\"marker\":\"route-sentinel-z9\"}") + "\n", "route-sentinel-z9") };
        foreach (var bounded in new[] { true, false }) foreach (var reverse in new[] { false, true })
            cases.Add(new($"ids-{(bounded ? "bounded48" : "single")}-{(reverse ? "reverse" : "forward")}", "id-selection", IdInstruction, IdInput, IdGrammar(reverse, bounded), "u1"));
        foreach (var (input, role) in new[] { ("科目：", "subject"), ("担当教員：", "teacher"), ("教室：", "room"), ("備考：", "unknown") })
            cases.Add(new("role-" + role, "role-classification", RoleInstruction, input, RoleGrammar, role));
        cases.Add(new("role-teacher-free", "role-classification-free", RoleInstruction, "担当教員：", null, "teacher"));
        return cases.ToArray();
    }
    // No fences, repair, sorting or first-ID deletion. Original input order u0,u1.
    internal static string? Decode(string? raw, string task)
    {
        if (raw is null || raw.Length > 2048) return null;
        try
        {
            using var parsed = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 4 });
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1) return null;
            var key = task == "routing" ? "marker" : task == "id-selection" ? "ids" : "role";
            if (!root.TryGetProperty(key, out var value)) return null;
            if (task == "id-selection")
            {
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 48) return null;
                var ids = value.EnumerateArray().ToArray(); var previous = -1;
                foreach (var id in ids)
                {
                    if (id.ValueKind != JsonValueKind.String) return null;
                    var index = id.GetString() switch { "u0" => 0, "u1" => 1, _ => -1 };
                    if (index <= previous) return null;
                    previous = index;
                }
                return string.Join(',', ids.Select(id => id.GetString()));
            }
            if (value.ValueKind != JsonValueKind.String) return null;
            var text = value.GetString();
            return task == "routing" || text is "subject" or "teacher" or "room" or "unknown" ? text : null;
        }
        catch (JsonException) { return null; }
    }
    internal static object Preflight()
    {
        var checks = new (string Raw, string Task, string? Expected)[] {
            ("{\"ids\":[\"u1\"]}","id-selection","u1"), ("{\"ids\":[\"u0\",\"u1\"]}","id-selection","u0,u1"),
            ("{\"ids\":[]}","id-selection",""), ("{\"ids\":[\"u1\",\"u0\"]}","id-selection",null),
            ("{\"ids\":[\"u0\",\"u0\"]}","id-selection",null), ("{\"ids\":[\"foreign\"]}","id-selection",null),
            ("{\"ids\":\"u1\"}","id-selection",null), ("{\"ids\":[1]}","id-selection",null),
            ("{\"ids\":[],\"ids\":[\"u1\"]}","id-selection",null), ("{\"role\":\"teacher\"}","role-classification","teacher"),
            ("{\"role\":\"teacher\",\"extra\":0}","role-classification",null), ("{\"role\":\"teacher\",\"role\":\"teacher\"}","role-classification",null),
            ("{\"role\":\"Teacher\"}","role-classification",null), ("```json\n{\"role\":\"teacher\"}\n```","role-classification",null),
            (new string(' ',2049)+"{\"role\":\"teacher\"}","role-classification",null),
            ("{\"marker\":\"route-sentinel-z9\"}","routing","route-sentinel-z9"),
            ("{\"marker\":\"wrong\"}","routing","wrong"),
            ("{\"marker\":\"x\",\"marker\":\"x\"}","routing",null),
            ("[\"route-sentinel-z9\"]","routing",null)
        };
        if (checks.Any(c => Decode(c.Raw,c.Task) != c.Expected) || Cases().Length != 10) throw new InvalidDataException("Scorer preflight failed.");
        return new { recipe = Recipe, checks = checks.Length, planned = 10, nativeCalls = 0, cases = Cases(), scope = "Diagnostic scorer, not native support or recovery quality" };
    }
}
