using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

if (args.Length is not (3 or 4 or 5) || args.Length >= 4 && args[3] != "candidate") throw new ArgumentException("native input manifest, verified model directory, new output");
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
using var input = JsonDocument.Parse(File.ReadAllBytes(args[0]));
var m = input.RootElement;
var dictionary = Verify(m.GetProperty("hybridDictionary"));
await RecoveryOcrModels.VerifyDictionaryAsync(dictionary, default);
if (!await RecoveryModelBundleStore.VerifyAsync(RecoveryOcrModels.Bundle,args[1])) throw new InvalidDataException("Baseline model identity differs.");
var candidate = args.Length >= 4;
var expert = args.Length == 5 ? args[4] : null;
if (expert is not null && Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(expert))) != "95bab179e0d6cc75269419078d68696cd4679661f94f6128b858b7a074fe2971")
    throw new InvalidDataException("Fixed exploratory expert differs.");
using var ocr = candidate ? new OnnxJapaneseOcr(Path.Combine(args[1],"det.onnx"),Path.Combine(args[1],"rec.onnx"),dictionary,18710,
    Path.Combine(args[1],"ascii-rec.onnx"),Path.Combine(args[1],"ascii-dictionary.json"),frameOriginalInk:true,manualCodeModel:expert,
    retryUnpaddedInput:true,allowContainedRuleContact:true,retainVerifiedWhitespace:true,retryRoleLabels:true,manualCodeRequiresLetter:true) : RecoveryOcrModels.OpenRuntime(args[1],dictionary);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));var token = timeout.Token;
using var output = new StreamWriter(new FileStream(args[2],FileMode.CreateNew,FileAccess.Write));
foreach (var doc in m.GetProperty("documents").EnumerateArray())
{
 var id = doc.GetProperty("id").GetString()!;
 if (id is not ("fresh-0-positive" or "fresh-1-positive")) continue;
 foreach (var page in doc.GetProperty("pages").EnumerateArray())
 {
  var number = page.GetProperty("page").GetInt32();
  var path = Verify(page.GetProperty("pixels"));
  var raster = new RecoveryRaster(page.GetProperty("width").GetInt32(),page.GetProperty("height").GetInt32(),File.ReadAllBytes(path));
  var observations = new List<OcrRecognitionObservation>();IReadOnlyList<RecoveryBox> detected=[];
  string? failure=null;var data=new Dictionary<string,object?>();object? glyphs=null;object? framedSeedDiagnostic=null;
  ocr.RecognitionObserver = x => observations.Add(x);ocr.DetectionObserver=x=>detected=x.ToArray();
  try {glyphs=ocr.ReadForManualCapture(raster,token);}
  catch(InvalidDataException e) {failure=e.Message;foreach(DictionaryEntry entry in e.Data)data[entry.Key.ToString()!]=entry.Value;}
  // No inference, repair or guard bypass: test whether trimming proven empty
  // margins BEFORE ownership changes this consumed acquisition diagnostic.
  if (failure is not null && detected.Count>0)
  {
   IReadOnlyList<RecoveryBox>? framed=null;IReadOnlyList<RecoveryBox>? completed=null;string? framingFailure=null;var framingData=new Dictionary<string,object?>();
   try {var frame=new OcrInkCropFrame(raster,null,token);framed=detected.Select(frame.Restrict).ToArray();completed=OcrCropCompleteness.Complete(raster,framed,token);}
   catch(InvalidDataException e){framingFailure=e.Message;foreach(DictionaryEntry entry in e.Data)framingData[entry.Key.ToString()!]=entry.Value;}
   framedSeedDiagnostic=new{framed,completed,failure=framingFailure,data=framingData,inferenceCalls=0};
  }
  output.WriteLine(JsonSerializer.Serialize(new{id,page=number,manualCodeExpert=candidate ? expert is not null : RecoveryOcrModels.Bundle.Artifacts.Any(a=>a.Path=="manual-code-rec.onnx"),explicitCandidateConstructor=candidate,explicitManualCodeExpertArgument=expert is not null,route=candidate?"explicit-candidate":"factory-default",asciiRecognizerCalls=ocr.AsciiRecognitionCalls,sourceSha256=Convert.ToHexStringLower(SHA256.HashData(raster.Bgra)),raster.Width,raster.Height,failure,data,detected,observations,glyphs,confidences=ocr.NativeConfidences,recognizedBoxes=ocr.RecognizedBoxes,framedSeedDiagnostic,
   scope="All pages of two consumed wholly fictional positive documents. Actual manual acquisition with explicitly recorded optional repairs/expert, unchanged original pixels. No reference labels read. Preserve failed observations and original exception diagnostics."}));output.Flush();CryptographicOperations.ZeroMemory(raster.Bgra);
 }
}
string Verify(JsonElement asset)
{
 var name=asset.GetProperty("file").GetString()!;
 if(Path.IsPathRooted(name)||name.Contains(".."))throw new InvalidDataException("Owned relative asset path invalid.");
 var path=Path.Combine(root,name);var bytes=File.ReadAllBytes(path);
 if(bytes.Length!=asset.GetProperty("bytes").GetInt64()||Convert.ToHexStringLower(SHA256.HashData(bytes))!=asset.GetProperty("sha256").GetString())throw new InvalidDataException("Pinned input differs.");return path;
}
