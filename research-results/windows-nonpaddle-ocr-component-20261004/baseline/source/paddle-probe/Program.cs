using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Takupoke.Infrastructure.Recovery;
var root=Path.GetFullPath(args[0]);
var inputs=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"native-inputs.json"))).RootElement.GetProperty("images");
string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
Console.WriteLine(JsonSerializer.Serialize(new {runtime=OrtEnv.Instance().GetVersionString(),managed=typeof(OrtEnv).Assembly.GetName().Version?.ToString(),sourceCommit="ae07e47fa68faeef210934b9f3688d6974587094",mode="unchanged actual Read; observer before guard; partial later units unassessed"}));
using var ocr=new OnnxJapaneseOcr("/workspace/recovery-research/artifacts/ocr-det.onnx","/workspace/recovery-research/artifacts/ocr-rec.onnx",Path.Combine(root,"paddle-characters.json"));
foreach(var i in inputs.EnumerateArray()) {
var id=i.GetProperty("id").GetString();var path=i.GetProperty("bgraPath").GetString()!;if(Hash(path)!=i.GetProperty("bgraSHA256").GetString())throw new InvalidDataException("Pixel pin changed");
var raster=new RecoveryRaster(i.GetProperty("width").GetInt32(),i.GetProperty("height").GetInt32(),File.ReadAllBytes(path));
using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(180));var watch=Stopwatch.StartNew();var observations=new List<OcrRecognitionObservation>();
ocr.RecognitionObserver=o=>{observations.Add(o);Console.WriteLine(JsonSerializer.Serialize(new {type="observation",id,observation=o}));Console.Out.Flush();};
try {var glyphs=ocr.Read(raster,stop.Token);Console.WriteLine(JsonSerializer.Serialize(new {type="result",id,readReturned=true,glyphs,recognizedBoxes=ocr.RecognizedBoxes,milliseconds=watch.ElapsedMilliseconds}));}
catch(Exception e){Console.WriteLine(JsonSerializer.Serialize(new {type="result",id,readReturned=false,errorType=e.GetType().Name,error=e.Message,observedRecognitionCrops=observations.Count,milliseconds=watch.ElapsedMilliseconds,remainingUnits="unassessed after early stop"}));}
Console.Out.Flush(); }
Console.WriteLine(JsonSerializer.Serialize(new {nativeModules=Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(m=>m.FileName.Contains("libonnxruntime.so")).Select(m=>new {sha256=Hash(m.FileName),name=Path.GetFileName(m.FileName)})}));
