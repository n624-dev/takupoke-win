using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;

internal static class Replay
{
    internal static async Task Run(string manifestPath, string rawPath)
    {
        using var input = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var raw = File.ReadAllBytes(rawPath); var rawHash = Convert.ToHexStringLower(SHA256.HashData(raw));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2)); var token = lifetime.Token;
        foreach (var line in System.Text.Encoding.UTF8.GetString(raw).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var observation = JsonDocument.Parse(line); var o = observation.RootElement;
            var id = o.GetProperty("id").GetString()!; var condition = o.GetProperty("condition").GetString()!;
            var source = input.RootElement.GetProperty("documents").EnumerateArray().Single(d => d.GetProperty("id").GetString() == id);
            var hash = source.GetProperty("originalPDFSHA256").GetString()!;
            var pages = new List<PdfPageLayout>(); var rasters = new List<RecoveryRaster>();
            var scores = new Dictionary<string, double>(); object? formal = null; string outcome = "unassessed", stage = "Pixels"; string? failure = null;
            RecoveryDocument? document = null;
            try
            {
                foreach (var page in o.GetProperty("pageReports").EnumerateArray())
                {
                    var number = page.GetProperty("number").GetInt32();
                    var original = source.GetProperty("pages").EnumerateArray().Single(p => p.GetProperty("page").GetInt32() == number);
                    var asset = original.GetProperty("pixels"); var relative = asset.GetProperty("file").GetString()!;
                    if (Path.IsPathRooted(relative) || relative.Contains("..")) throw new InvalidDataException("Pixel path changed.");
                    var pixels = File.ReadAllBytes(Path.Combine(root, relative));
                    if (pixels.Length != asset.GetProperty("bytes").GetInt64() || Convert.ToHexStringLower(SHA256.HashData(pixels)) != asset.GetProperty("sha256").GetString())
                        throw new InvalidDataException("Original pixel receipt changed.");
                    var raster = new RecoveryRaster(page.GetProperty("Width").GetInt32(), page.GetProperty("Height").GetInt32(), pixels); rasters.Add(raster);
                    var glyphs = JsonSerializer.Deserialize<PdfGlyph[]>(page.GetProperty("glyphs").GetRawText())!;
                    var rules = JsonSerializer.Deserialize<PdfRule[]>(page.GetProperty("rules").GetRawText())!;
                    var support = page.GetProperty("observations").EnumerateArray().Select(r =>
                        JsonSerializer.Deserialize<RecoveryBox>(r.GetProperty(condition == "production" ? "RecognitionCrop" : "framed").GetRawText())
                            ?? throw new InvalidDataException("Original recognition crop is missing.")).ToArray();
                    if (raster.HasUnrecognizedInk(support, rules, token)) throw new InvalidDataException("Original ink coverage failed.");
                    var confidence = page.GetProperty("scores").EnumerateArray().Select(s => s.GetDouble()).ToArray();
                    if (confidence.Length != glyphs.Length) throw new InvalidDataException("Native confidence/source correspondence changed.");
                    for (var i = 0; i < glyphs.Length; i++) scores.Add($"p{number}s{i}", confidence[i]);
                    pages.Add(new(raster.Width, raster.Height, glyphs, rules));
                }
                if (pages.Count != source.GetProperty("pages").GetArrayLength()) throw new InvalidDataException("Source acquisition was incomplete.");
                stage = "Builder";
                var scanners = rasters.Select((r, i) => r.InkFreeScanner(r.RuleMask(pages[i].Lines, token), token)).ToArray();
                document = RecoveryDocumentBuilder.Build(hash, MaterialKind.Timetable, pages, (p, b) => scanners[p - 1](b),
                    Enumerable.Range(1, pages.Count).ToHashSet(), token, allowStructureProposal: true);
                var sources = document.Sources.Select(s => s with { NativeConfidence = scores[s.Id] }).ToArray();
                document = document with { Sources = sources, Capture = new(hash, pages.Count, RecoveryValidator.Fingerprint(sources),
                    rasters.Select((r, i) => new RecoveryCapturedPage(i + 1, r.Width, r.Height,
                        Convert.ToHexStringLower(SHA256.HashData(r.Bgra)), sources.Count(s => s.Page == i + 1), true)).ToArray()) };
                stage = "Engine";
                var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null, token);
                if (run.Result is null) { outcome = "engine-refused"; failure = string.Join(',', run.Errors); }
                else
                {
                    stage = "Validator"; var validation = RecoveryValidator.Validate(document, run.Result, token);
                    if (!validation.CanAdopt) { outcome = "validator-refused"; failure = string.Join(',', validation.Errors); }
                    else
                    {
                        stage = "Formal";
                        var record = new SourceRecord(id, MaterialKind.Timetable, "fictional.pdf", "fictional", "fictional.pdf", hash,
                            1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
                        formal = RecoveryAnalysisConverter.Convert(record, document, run.Result, DateTimeOffset.UnixEpoch, token).Timetable;
                        outcome = "formal-returned-unscored";
                    }
                }
            }
            catch (InvalidDataException e) { outcome = "pipeline-refused"; failure = e.Message; }
            catch (Exception e) { outcome = "execution-error-unassessed"; failure = e.GetType().Name + ":" + e.Message; }
            finally
            {
                Console.WriteLine(JsonSerializer.Serialize(new { recipe = "physical-frame-no-inference-builder-replay-v1", rawSourceSHA = rawHash,
                    id, condition, stage, outcome, failure, formal, document, nativeCalls = 0, manualCorrections = 0,
                    scope = "Posthoc consumed-data source-contract diagnosis; unchanged original text/boxes/scores, no heldout qualification" }));
                foreach (var raster in rasters) CryptographicOperations.ZeroMemory(raster.Bgra);
            }
        }
    }
}
