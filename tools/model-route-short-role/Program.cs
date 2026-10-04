using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Betalgo.Ranul.OpenAI.ObjectModels.SharedModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.AI.Foundry.Local.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;

Environment.SetEnvironmentVariable("ORT_TELEMETRY_DISABLED", "1");
Console.WriteLine(JsonSerializer.Serialize(Protocol.Preflight()));
if (args.SequenceEqual(new[] { "--preflight" })) return 0;
if (args.Length != 0) throw new ArgumentException("The diagnostic has one frozen model and recipe, no tuning flags.");
if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    Console.WriteLine("Unsupported host: Windows x64 required; nativeCalls=0."); return 1;
}
foreach (var name in new[] { "MSVCP140.dll", "MSVCP140_1.dll", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll" })
{
    if (!NativeLibrary.TryLoad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name), out var handle))
        throw new InvalidDataException("Install official x64 VC runtime before any model download: https://aka.ms/vs/17/release/vc_redist.x64.exe");
    NativeLibrary.Free(handle);
}
const string modelId = "qwen3.5-4b-generic-cpu:3";
var ownedRoot = Path.Combine(Path.GetTempPath(), "takupoke-short-role-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(ownedRoot);
var observations = new List<Observation>(); var artifacts = new List<Artifact>();
string? initializationError = null; string? cleanupError = null; var started = 0; var returned = 0;
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(25));
var process = Process.GetCurrentProcess(); var evaluator = Stopwatch.StartNew();
try
{
    await FoundryLocalManager.CreateAsync(new Configuration { AppName = "takupoke-short-role-diagnostic", AppDataDir = Path.Combine(ownedRoot,"app"),
        ModelCacheDir = Path.Combine(ownedRoot,"models"), LogsDir = Path.Combine(ownedRoot,"logs"),
        AdditionalSettings = new Dictionary<string,string> { ["DisableNonessentialTelemetry"] = "true" } }, NullLogger.Instance, lifetime.Token);
    var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(lifetime.Token);
    var model = await catalog.GetModelVariantAsync(modelId,lifetime.Token) ?? throw new InvalidDataException("Pinned model unavailable.");
    if (model.Id != modelId) throw new InvalidDataException("Model identity mismatch.");
    await model.DownloadAsync(ct:lifetime.Token);
    var path = await model.GetPathAsync(lifetime.Token);
    if (path is null || !Directory.Exists(path) || !Path.GetFullPath(path).StartsWith(Path.GetFullPath(ownedRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Cache is outside job-owned directory.");
    foreach (var file in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        await using var input = File.OpenRead(file);
        artifacts.Add(new(Path.GetRelativePath(path,file).Replace('\\','/'),input.Length,Convert.ToHexStringLower(await SHA256.HashDataAsync(input,lifetime.Token))));
    }
    using var pin = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"artifact-pin.json")));
    var expected = pin.RootElement.GetProperty("artifacts").Deserialize<Artifact[]>()!;
    if (pin.RootElement.GetProperty("modelId").GetString()!=modelId || !artifacts.SequenceEqual(expected) || artifacts.Sum(a=>a.Size)!=pin.RootElement.GetProperty("artifactBytes").GetInt64())
        throw new InvalidDataException("Actual artifacts differ from the historical Q4 exact inventory.");
    await model.LoadAsync(lifetime.Token);
    try
    {
        foreach (var c in Protocol.Cases())
        {
            if (lifetime.IsCancellationRequested) break;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(120));
            var watch = Stopwatch.StartNew(); var beforeStarted = started; var beforeReturned = returned;
            string? raw = null; string? decoded = null; string? errorType = null; string? errorMessage = null;
            try
            {
                var client = await model.GetChatClientAsync(deadline.Token);
                client.Settings.Temperature = 0; client.Settings.RandomSeed = 17; client.Settings.MaxTokens = 32; client.Settings.ToolChoice = ToolChoice.None;
                client.Settings.ResponseFormat = c.Grammar is null ? null : new ResponseFormatExtended { Type = "lark_grammar", LarkGrammar = c.Grammar };
                var messages = new List<ChatMessage>();
                if (c.System.Length>0) messages.Add(new() { Role = "system", Content = c.System });
                messages.Add(new() { Role = "user", Content = c.Input });
                started++;
                var completion = await client.CompleteChatAsync(messages,deadline.Token); returned++;
                if (completion.Choices.Count!=1 || completion.Choices[0].Message is null) throw new InvalidDataException("Unexpected choice count; no single raw response available.");
                raw = completion.Choices[0].Message.Content;
                deadline.Token.ThrowIfCancellationRequested();
                decoded = Protocol.Decode(raw,c.Task);
            }
            catch(Exception error)
            {
                errorType = error.GetType().Name;
                errorMessage = Bounded(error.Message,1024) + (error.InnerException is {} inner ? " | "+inner.GetType().Name+": "+Bounded(inner.Message,1024) : "");
            }
            var assessed = errorType is null && returned>beforeReturned;
            var observation = new Observation(c.Id,c.Task,c.System,c.Input,c.Grammar,c.Expected,Bounded(raw,2048),raw?.Length,raw is { Length:>2048 },
                decoded,assessed,assessed && decoded==c.Expected,errorType,errorMessage,started-beforeStarted,returned-beforeReturned,watch.ElapsedMilliseconds);
            observations.Add(observation);
            // Immediate bounded evidence survives later cancellation/initialization failures.
            Console.WriteLine("SHORT_ROLE_CASE "+JsonSerializer.Serialize(observation));
        }
    }
    finally { await model.UnloadAsync(); }
}
catch(Exception error) { initializationError = error.GetType().Name+": "+Bounded(error.Message,2048); }
finally
{
    try { Directory.Delete(ownedRoot,true); } catch(Exception error) { cleanupError = error.GetType().Name+": "+Bounded(error.Message,1024); }
}
var summary = new { planned = 10, attempted = started, returned, recorded = observations.Count, assessed = observations.Count(o=>o.Assessed),
    operationallyUnassessed = 10-observations.Count(o=>o.Assessed), exact = observations.Count(o=>o.Exact),
    malformedReturned = observations.Count(o=>o.Assessed && o.Decoded is null), initializationError, cleanupError,
    recoveryQualityAssessed = 0, usefulAiAssessed = 0 };
var report = new { recipe = Protocol.Recipe, modelId, sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local-uncommitted",
    runtimeVersion = "Foundry.Local.WinML:1.2.4", backend = "Windows CPU", os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    artifactBytes = artifacts.Sum(a=>a.Size), artifacts, sampler = new { temperature = 0, randomSeed = 17, maxTokens = 32, toolChoice = "none" },
    bounds = new { perCallSeconds = 120, evaluatorMinutes = 25, rawPrefixCharacters = 2048, noRetries = true }, summary,
    tasks = observations.GroupBy(o=>o.Task).Select(g=>new { task = g.Key, recorded = g.Count(), assessed = g.Count(o=>o.Assessed), exact = g.Count(o=>o.Exact) }),
    observations, elapsedMilliseconds = evaluator.ElapsedMilliseconds, evaluatorPeakWorkingSetBytes = process.PeakWorkingSet64,
    scope = "Consumed diagnostic controls only; no recovery document, certificate, Validator/formal, useful-AI qualification or catalog activation. Literal agreement is not proof of hard token enforcement. Fixed sequential order and cumulative process memory; child runtime memory excluded." };
var bytes = JsonSerializer.SerializeToUtf8Bytes(report); var base64 = Convert.ToBase64String(bytes); const int chunkSize = 6000;
Console.WriteLine("SHORT_ROLE_REPORT_META "+JsonSerializer.Serialize(new { bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), chunks = (base64.Length+chunkSize-1)/chunkSize }));
for(var i=0;i<base64.Length;i+=chunkSize) Console.WriteLine("SHORT_ROLE_REPORT_CHUNK "+(i/chunkSize)+" "+base64.Substring(i,Math.Min(chunkSize,base64.Length-i)));
Console.WriteLine("SHORT_ROLE_SUMMARY "+JsonSerializer.Serialize(summary));
return initializationError is null && cleanupError is null && observations.Count==10 && observations.All(o=>o.Assessed) ? 0 : 1;

static string? Bounded(string? value,int limit)=>value is null ? null : value[..Math.Min(value.Length,limit)];
internal sealed record Artifact(string Path,long Size,string Sha256);
internal sealed record Observation(string Id,string Task,string System,string Input,string? Grammar,string Expected,string? Raw,int? RawLength,bool RawTruncated,
    string? Decoded,bool Assessed,bool Exact,string? ErrorType,string? ErrorMessage,int Started,int Returned,long Milliseconds);
