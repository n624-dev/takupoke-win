using System.Diagnostics;
using System.Text.Json;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Betalgo.Ranul.OpenAI.ObjectModels.SharedModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.AI.Foundry.Local.OpenAI;

// Native routing diagnostic only. The arbitrary marker is unrelated to any
// document, recovery answer, label ID or quality denominator.
internal static class NativeFormatRouting
{
    internal const string Recipe = "native-format-routing-literal-v1";
    private const string Marker = "native-format-control";
    internal static readonly string Literal = JsonSerializer.Serialize(new { formatRouting = Marker });
    internal static ResponseFormatExtended Format(string type) => type switch
    {
        "json_schema" => new() { Type = type, JsonSchema = new JsonSchema { Name = "format_routing_control", Strict = true,
            Schema = new PropertyDefinition { Type = "object", AdditionalProperties = false, Required = ["formatRouting"],
                Properties = new Dictionary<string, PropertyDefinition> { ["formatRouting"] = new() { Type = "string", Enum = [Marker] } } } } },
        "lark_grammar" => new() { Type = type, LarkGrammar = "start: " + JsonSerializer.Serialize(Literal) + "\n" },
        _ => throw new ArgumentException("Unsupported diagnostic response format.")
    };
    internal static bool Matches(string? text)
    {
        if (text is null || text.Length > 8192) return false;
        try
        {
            using var parsed = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            return parsed.RootElement.ValueKind == JsonValueKind.Object && parsed.RootElement.EnumerateObject().Count() == 1 &&
                parsed.RootElement.TryGetProperty("formatRouting", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == Marker;
        }
        catch (JsonException) { return false; }
    }
    internal static object Preflight()
    {
        var accepted = new[] { Literal, " { \"formatRouting\" : \"" + Marker + "\" } \n" };
        var rejected = new[] { "unrestricted", "[" + Literal + "]", "{\"formatRouting\":\"other-marker\"}",
            Literal[..^1] + ",\"extra\":true}", Literal[..^1] + ",\"formatRouting\":\"" + Marker + "\"}",
            "```json\n" + Literal + "\n```", new string(' ', 8193) + Literal };
        if (accepted.Any(text => !Matches(text)) || rejected.Any(Matches)) throw new InvalidDataException("Format-routing scorer preflight failed.");
        return new { recipe = Recipe, validMarkersAccepted = accepted.Length, malformedMarkersRejected = rejected.Length, nativeCalls = 0,
            scope = "Diagnostic scorer only; native response-format routing remains unverified" };
    }
    internal static async Task<Result> RunAsync(IModel model, CancellationToken token)
    {
        var observations = new List<object>(); var errors = 0; var started = 0; var returned = 0; var matched = 0;
        await model.LoadAsync(token);
        try
        {
            foreach (var type in new[] { "json_schema", "lark_grammar" })
            {
                var watch = Stopwatch.StartNew();
                string? raw = null;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(90));
                try
                {
                    var client = await model.GetChatClientAsync(deadline.Token);
                    client.Settings.Temperature = 0; client.Settings.RandomSeed = 17; client.Settings.MaxTokens = 32;
                    client.Settings.ToolChoice = ToolChoice.None; client.Settings.ResponseFormat = Format(type);
                    started++;
                    var completion = await client.CompleteChatAsync(new[] { new ChatMessage { Role = "user", Content = "Return exactly the word unrestricted with no punctuation." } }, deadline.Token);
                    returned++;
                    if (completion.Choices.Count != 1 || completion.Choices[0].Message is null) throw new InvalidDataException("Invalid native choice count.");
                    raw = completion.Choices[0].Message.Content;
                    deadline.Token.ThrowIfCancellationRequested();
                    var matches = Matches(raw); if (matches) matched++;
                    observations.Add(new { responseFormat = type, routingMatches = matches, rawOutput = raw is null ? null : raw[..Math.Min(raw.Length, 8192)],
                        rawOriginalLength = raw?.Length, rawTruncated = raw is { Length: > 8192 }, milliseconds = watch.ElapsedMilliseconds });
                }
                catch (Exception error)
                {
                    errors++; observations.Add(new { responseFormat = type, errorType = error.GetType().Name,
                        errorMessage = error.Message[..Math.Min(error.Message.Length, 1024)], innerErrorType = error.InnerException?.GetType().Name,
                        innerErrorMessage = error.InnerException?.Message is { } inner ? inner[..Math.Min(inner.Length, 1024)] : null,
                        rawOutput = raw is null ? null : raw[..Math.Min(raw.Length, 8192)], rawOriginalLength = raw?.Length,
                        rawTruncated = raw is { Length: > 8192 }, milliseconds = watch.ElapsedMilliseconds });
                    token.ThrowIfCancellationRequested();
                }
            }
        }
        finally { await model.UnloadAsync(); }
        return new(errors, started, returned, matched, observations);
    }
    internal sealed record Result(int Errors, int Started, int Returned, int Matched, IReadOnlyList<object> Cases);
}
