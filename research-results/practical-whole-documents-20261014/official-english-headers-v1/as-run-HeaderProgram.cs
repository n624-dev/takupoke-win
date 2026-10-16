using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

// New English inference on physically bounded ASCII class-header lines only.
// Japanese/primary detections and glyphs are consumed immutable observations.
// No gold, class replacement table, typed values or score raising enter here.
if (args.Length != 2) throw new ArgumentException("Expected verified local manifest and new raw output.");
using var manifest = JsonDocument.Parse(File.ReadAllBytes(args[0])); var m = manifest.RootElement;
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
if (m.GetProperty("recipe").GetString() != "consumed-primary-english-header-v1") throw new InvalidDataException("Recipe mismatch.");
var primaryPath = Verify(m.GetProperty("primaryRaw"));
var inputPath = Verify(m.GetProperty("primaryManifest"));
using var primaryManifest = JsonDocument.Parse(File.ReadAllBytes(inputPath));
var inputDocs = primaryManifest.RootElement.GetProperty("documents").EnumerateArray().ToDictionary(d => d.GetProperty("id").GetString()!);
var model = Verify(m.GetProperty("model")); var dictionary = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(Verify(m.GetProperty("dictionary"))))!;
if (dictionary.Length != 438 || dictionary[0] != "" || dictionary[^1] != " ") throw new InvalidDataException("English alphabet identity failed.");
OrtEnv.Instance().DisableTelemetryEvents();
using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
using var session = new InferenceSession(model, options);
if (session.InputMetadata.Count != 1 || !session.InputMetadata.TryGetValue("x", out var shape) || shape.Dimensions.Length != 4 || shape.Dimensions[1] != 3 || shape.Dimensions[2] != 48) throw new InvalidDataException("English input shape failed.");
using var timer = new CancellationTokenSource(TimeSpan.FromMinutes(3)); var token = timer.Token;
using var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write));
foreach (var line in File.ReadLines(primaryPath))
{
 using var json = JsonDocument.Parse(line); var r = json.RootElement;
 if (r.GetProperty("condition").GetString() != "ink-input-frame") continue;
 var id = r.GetProperty("id").GetString()!; var input = inputDocs[id]; var watch = Stopwatch.StartNew();
 var pages = new List<PdfPageLayout>(); var rasters = new List<RecoveryRaster>(); var scores = new Dictionary<string,double>();
 var pageReports = new List<object>(); var decisions = new List<object>(); var lineage = new List<object>();
 RecoveryDocument? document = null; object? formal = null; var additionalCalls = 0;
 string stage = "CachedRead", outcome = "unassessed"; string? failure = null;
 try
 {
  foreach (var page in r.GetProperty("pageReports").EnumerateArray())
  {
   token.ThrowIfCancellationRequested(); var number = page.GetProperty("number").GetInt32();
   var original = input.GetProperty("pages").EnumerateArray().Single(p => p.GetProperty("page").GetInt32() == number);
   var raster = new RecoveryRaster(original.GetProperty("width").GetInt32(), original.GetProperty("height").GetInt32(), File.ReadAllBytes(Verify(original.GetProperty("pixels"))));
   if (!raster.Valid) throw new InvalidDataException("Original raster failed."); rasters.Add(raster);
   var rules = page.GetProperty("rules").Deserialize<PdfRule[]>()!;
   var originals = page.GetProperty("glyphs").Deserialize<PdfGlyph[]>()!; var originalScores = page.GetProperty("scores").Deserialize<double[]>()!;
   if (originals.Length != originalScores.Length || !page.GetProperty("complete").GetBoolean()) throw new InvalidDataException("Cached complete source ownership failed.");
   var originalObservations = page.GetProperty("observations").EnumerateArray().ToArray();
   var classRight = rules.Where(l => l.Vertical && l.X1 > 2).Select(l => l.X1).DefaultIfEmpty(double.NaN).Min();
   var gridTop = rules.Where(l => l.Vertical && l.X1 > 2).Select(l => Math.Min(l.Y1,l.Y2)).DefaultIfEmpty(double.NaN).Min();
   var glyphs = new List<PdfGlyph>(); var nativeScores = new List<double>();
   foreach (var group in originals.Select((g,i)=>(g,i)).GroupBy(x => x.g.SourceLine).OrderBy(g => g.Key))
   {
    if (group.Key is not int lineIndex || lineIndex < 0 || lineIndex >= originalObservations.Length)
        throw new InvalidDataException("Cached native line identity failed.");
    var native = group.OrderBy(x => x.g.SourceOrder).ToArray(); var crop = originalObservations[lineIndex].GetProperty("framed").Deserialize<RecoveryBox>()!;
    var primary = string.Concat(native.Select(x => x.g.Text));
    var candidate = double.IsFinite(classRight) && double.IsFinite(gridTop) && crop.X + crop.Width < classRight && crop.Y >= gridTop &&
        primary.Length > 0 && primary.All(c => c <= 127);
    var chosen = false;
    if (candidate)
    {
     var transformed = OcrInputTransform.Recognition(raster, crop, token);
     using var result = session.Run([NamedOnnxValue.CreateFromTensor("x", transformed.Tensor)]); additionalCalls++; token.ThrowIfCancellationRequested();
     var tensor = result.Single().AsTensor<float>(); var pieces = OcrCtcDecoder.Decode(tensor,dictionary,token);
     var text = string.Concat(pieces.Select(p=>p.Text)); var paddingSupported = true; var replacement = new List<PdfGlyph>();
     foreach (var piece in pieces)
     {
      try { replacement.Add(transformed.SourceGlyph(piece.Text,piece.Start,piece.End,tensor.Dimensions[1],lineIndex,glyphs.Count + replacement.Count)); }
      catch (InvalidDataException) { paddingSupported = false; }
     }
     chosen = pieces.Count > 0 && pieces.All(p=>p.Confidence>=.8f) && paddingSupported &&
        text.All(c=>c<=127) && ClassSelection.Candidates.Contains(text);
     decisions.Add(new { page = number, line = group.Key, crop, primaryText = primary,
      primaryPieces = originalObservations[lineIndex].GetProperty("pieces").Clone(), englishText = text, englishPieces = pieces,
      paddingSupported, chosen, transformed.ValidWidth, transformed.InputWidth,
      decision = "Fixed physical left-column/ASCII routing; literal canonical English output with native minimum .8; otherwise retain primary" });
     if (chosen)
     {
      for (var n=0;n<replacement.Count;n++) { glyphs.Add(replacement[n]); nativeScores.Add(pieces[n].Confidence);
       lineage.Add(new { page=number,newGlyphIndex=glyphs.Count-1,model="english-v5",primaryGlyphIndices=native.Select(x=>x.i).ToArray() }); }
     }
    }
    if (!chosen) foreach (var (glyph,index) in native)
    { glyphs.Add(glyph with { SourceOrder=glyphs.Count });nativeScores.Add(originalScores[index]);
      lineage.Add(new { page=number,newGlyphIndex=glyphs.Count-1,model="cached-v6-medium",primaryGlyphIndices=new[]{index} }); }
   }
   var supports = originalObservations.Select(o=>o.GetProperty("framed").Deserialize<RecoveryBox>()!).ToArray();
   if (raster.HasUnrecognizedInk(supports,rules,token)) throw new InvalidDataException("Original full ink coverage failed.");
   for(var n=0;n<glyphs.Count;n++) scores.Add($"p{number}s{n}",nativeScores[n]);
   pages.Add(new(raster.Width,raster.Height,glyphs,rules));
   pageReports.Add(new { number,raster.Width,raster.Height,complete=true,glyphs,scores=nativeScores,rules });
  }
  if (pages.Count != input.GetProperty("pages").GetArrayLength()) throw new InvalidDataException("Cached pages missing.");
  stage="Builder"; var scanners=rasters.Select((s,i)=>s.InkFreeScanner(s.RuleMask(pages[i].Lines,token),token)).ToArray();
  var hash=input.GetProperty("originalPDFSHA256").GetString()!;
  document=RecoveryDocumentBuilder.Build(hash,MaterialKind.Timetable,pages,(p,b)=>scanners[p-1](b),Enumerable.Range(1,pages.Count).ToHashSet(),token,allowStructureProposal:true);
  var sources=document.Sources.Select(s=>s with {NativeConfidence=scores[s.Id]}).ToArray();
  document=document with {Sources=sources,Capture=new(hash,pages.Count,RecoveryValidator.Fingerprint(sources),
   rasters.Select((s,i)=>new RecoveryCapturedPage(i+1,s.Width,s.Height,Sha(s.Bgra),sources.Count(x=>x.Page==i+1),true)).ToArray(),2)};
  stage="Engine";var run=await RecoveryEngine.RunAsync(document,"windows",10,true,[],_=>null,token);
  if(run.Result is null){outcome="engine-refused";failure=string.Join(',',run.Errors);}
  else {outcome="validated-preview-returned-unscored";formal=run.Result;}
 }
 catch(Exception e) when(e is InvalidDataException or PdfParseException){outcome="pipeline-refused";failure=e.Message;}
 finally
 {
  writer.WriteLine(JsonSerializer.Serialize(new {id,condition="english-header",stage,outcome,failure,document,formal,pageReports,decisions,lineage,
   requiredSlots=document?.RequiredSlots.Count,cells=document?.Cells.Count,
   detectorCalls=0,recognizerCalls=additionalCalls,cachedPrimaryRecognizerCalls=r.GetProperty("recognizerCalls").GetInt32(),
   milliseconds=watch.ElapsedMilliseconds,processPeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,
   primaryRawSHA256=m.GetProperty("primaryRaw").GetProperty("sha256").GetString(),
   scope="Consumed primary page observations; new native English header calls only. No new primary OCR or independent/final document credit. No human acknowledgement or source/score repair." }));
  writer.Flush();foreach(var raster in rasters)CryptographicOperations.ZeroMemory(raster.Bgra);
 }
}
string Verify(JsonElement asset)
{
 var name=asset.GetProperty("file").GetString()!;
 if(Path.IsPathRooted(name)||name.Contains(".."))throw new InvalidDataException("Owned asset path failed.");
 var path=Path.Combine(root,name);var bytes=File.ReadAllBytes(path);
 if(bytes.Length!=asset.GetProperty("bytes").GetInt64()||Sha(bytes)!=asset.GetProperty("sha256").GetString())throw new InvalidDataException("Asset SHA failed.");return path;
}
static string Sha(byte[] data)=>Convert.ToHexStringLower(SHA256.HashData(data));
