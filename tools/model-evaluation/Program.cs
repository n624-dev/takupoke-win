using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

// Manual, public-model evaluation only. Every input is invented here; no app
// storage, school files, school endpoints, auth, upload or cache restoration.
Environment.SetEnvironmentVariable("ORT_TELEMETRY_DISABLED", "1");
var requested = args.Length == 0 ? new[] { "qwen3-0.6b-generic-cpu:4", "qwen2.5-1.5b-instruct-generic-cpu:4" } : args;
if (requested.Any(id => id is not "qwen3-0.6b-generic-cpu:4" and not "qwen2.5-1.5b-instruct-generic-cpu:4")) throw new ArgumentException("Only pinned public evaluation IDs are accepted.");
var ownedRoot = Path.Combine(Path.GetTempPath(), "takupoke-public-model-evaluation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(ownedRoot);
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(100));
var results = new List<object>();
try
{
    await FoundryLocalManager.CreateAsync(new Configuration { AppName = "takupoke-public-model-evaluation", AppDataDir = Path.Combine(ownedRoot, "app"), ModelCacheDir = Path.Combine(ownedRoot, "models"), LogsDir = Path.Combine(ownedRoot, "logs"), AdditionalSettings = new Dictionary<string, string> { ["DisableNonessentialTelemetry"] = "true" } }, NullLogger.Instance, cancellation.Token);
    var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(cancellation.Token);
    foreach (var id in requested)
    {
        try
        {
            var model = await catalog.GetModelVariantAsync(id, cancellation.Token) ?? throw new InvalidDataException("Pinned variant unavailable.");
            if (model.Id != id) throw new InvalidDataException("Variant ID mismatch.");
            await model.DownloadAsync(ct: cancellation.Token);
            var path = await model.GetPathAsync(cancellation.Token); if (path is null || !Directory.Exists(path)) throw new InvalidDataException("Cached directory missing.");
            var fullPath = Path.GetFullPath(path); var prefix = Path.GetFullPath(ownedRoot) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Cache outside owned evaluation directory.");
            var files = new List<object>(); long bytes = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            { await using var input = File.OpenRead(file); bytes += input.Length; files.Add(new { path = Path.GetRelativePath(path, file).Replace('\\', '/'), size = input.Length, sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellation.Token)) }); }
            await model.LoadAsync(cancellation.Token); var client = await model.GetChatClientAsync(cancellation.Token);
            var cases = new[] { (Text: "科目=架空科目A\n担当=架空教員B\n教室=架空教室C", Subject: "架空科目A", Teacher: "架空教員B", Room: "架空教室C"), (Text: "教室=架空教室D\n科目=架空科目E\n担当=架空教員F", Subject: "架空科目E", Teacher: "架空教員F", Room: "架空教室D"), (Text: "科目=架空科目G\n担当=\n教室=架空教室H", Subject: "架空科目G", Teacher: "", Room: "架空教室H"), (Text: "科目=架空科目I\n担当=架空教員J\n教室=", Subject: "架空科目I", Teacher: "架空教員J", Room: "") };
            var exact = 0; var safeFailure = 0; var incorrect = 0; var watch = Stopwatch.StartNew();
            foreach (var c in cases)
            {
                var response = await client.CompleteChatAsync([new ChatMessage { Role = "system", Content = "Copy only the explicitly labeled synthetic Japanese data. Return exactly JSON {\"subject\":\"...\",\"teacher\":\"...\",\"room\":\"...\"}. Do not infer missing values; use empty string for explicit blank." }, new ChatMessage { Role = "user", Content = c.Text }], cancellation.Token);
                try { if (response.Choices.Count != 1 || response.Choices[0].Message?.Content is not { } output || output.Length > 4096) { safeFailure++; continue; } using var json = JsonDocument.Parse(output); var r = json.RootElement; if (r.GetProperty("subject").GetString() == c.Subject && r.GetProperty("teacher").GetString() == c.Teacher && r.GetProperty("room").GetString() == c.Room) exact++; else incorrect++; }
                catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { safeFailure++; }
            }
            watch.Stop(); await model.UnloadAsync(cancellation.Token);
            results.Add(new { modelId = id, runtimeVersion = "Foundry.Local.WinML:1.2.4", artifactBytes = bytes, artifacts = files, syntheticCases = cases.Length, exact, safeFailure, incorrect, inferenceMilliseconds = watch.ElapsedMilliseconds, privateMemoryBytes = Process.GetCurrentProcess().PrivateMemorySize64 });
        }
        catch (Exception failure) { results.Add(new { modelId = id, errorType = failure.GetType().Name }); }
    }
    // Logs deliberately contain public artifact inventory and aggregate synthetic
    // scores only. Exception messages, native logs and temporary paths are omitted.
    Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    return results.Count == requested.Length && !results.Any(r => r.GetType().GetProperty("errorType") is not null) ? 0 : 1;
}
catch (Exception failure) { Console.WriteLine(JsonSerializer.Serialize(new { errorType = failure.GetType().Name })); return 1; }
finally
{
    if (FoundryLocalManager.IsInitialized) FoundryLocalManager.Instance.Dispose();
    try { Directory.Delete(ownedRoot, true); } catch { /* Hosted runner also removes this job-owned directory. */ }
}
