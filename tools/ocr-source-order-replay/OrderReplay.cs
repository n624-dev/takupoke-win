using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
if(args.Length!=3)throw new ArgumentException("closed-actual-candidate new-output expectedSHA256");
if(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(args[0])))!=args[2])throw new InvalidDataException("closed input SHA");
using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));using var output=new StreamWriter(new FileStream(args[1],FileMode.CreateNew));
foreach(var line in File.ReadLines(args[0])){
 using var row=JsonDocument.Parse(line);var r=row.RootElement;var node=JsonNode.Parse(line)!.AsObject();var watch=System.Diagnostics.Stopwatch.StartNew();
 if(r.GetProperty("document").ValueKind!=JsonValueKind.Null){
 var original=r.GetProperty("document").Deserialize<RecoveryDocument>()!;
 if(original.Capture is null||original.Capture.SourceSnapshot!=RecoveryValidator.Fingerprint(original.Sources)||!original.Sources.All(s=>s.FromOcr))throw new InvalidDataException("original all-OCR capture proof absent");
 var pages=r.GetProperty("pageReports").EnumerateArray().Select(p=>new PdfPageLayout(p.GetProperty("Width").GetInt32(),p.GetProperty("Height").GetInt32(),p.GetProperty("glyphs").Deserialize<PdfGlyph[]>()!,p.GetProperty("rules").Deserialize<PdfRule[]>()!)).ToArray();
 var ids=RecoveryDocumentBuilder.OriginalOcrSourceOrder(pages,timeout.Token);var sources=original.Sources.ToDictionary(s=>s.Id);
 if(ids.Count!=sources.Count||ids.Distinct().Count()!=sources.Count||ids.Any(id=>!sources.ContainsKey(id)))throw new InvalidDataException("original source identity differs");
 var ordered=ids.Select(id=>sources[id]).ToArray();var document=original with{Sources=ordered,Capture=original.Capture with{SourceSnapshot=RecoveryValidator.Fingerprint(ordered)}};
 var run=await RecoveryEngine.RunAsync(document,"windows",10,true,[],_=>null,timeout.Token);
 node["document"]=JsonSerializer.SerializeToNode(document);node["formal"]=JsonSerializer.SerializeToNode(run.Result);
 node["outcome"]=run.Result is not null?"validated-preview":RecoveryManualAssistance.Prepare(document,timeout.Token) is not null?"original-review-required":"refused";
 node["failure"]=run.Result is null?string.Join(',',run.Errors):null;
 }
 node["historicalMilliseconds"]=node["milliseconds"]?.DeepClone();node["milliseconds"]=watch.ElapsedMilliseconds;
 node["historicalProcessPeakWorkingSetBytes"]=node["processPeakWorkingSetBytes"]?.DeepClone();node["processPeakWorkingSetBytes"]=System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64;
 node["historicalRecognizerCalls"]=node["recognizerCalls"]?.DeepClone();node["historicalAsciiRecognizerCalls"]=node["asciiRecognizerCalls"]?.DeepClone();node["historicalDetectorCalls"]=node["detectorCalls"]?.DeepClone();
 node["recognizerCalls"]=0;node["asciiRecognizerCalls"]=0;node["detectorCalls"]=0;node["newModelCalls"]=0;node["sourceOnlyOrderingDevelopment"]=true;
 node["scope"]="Consumed source-only ordering diagnosis. SAME actual glyph literals/native probabilities/boxes/source metadata/original crops; only sequence of independently proved complete OCR role rows changes. Original raw remains immutable. No OCR/oracle/new raster/human acknowledgement/default activation. Timings here exclude acquisition.";
 output.WriteLine(node.ToJsonString(new JsonSerializerOptions{WriteIndented=false}));output.Flush();
}
