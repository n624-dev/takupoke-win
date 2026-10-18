using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
if(args.Length!=4)throw new ArgumentException("closed manifest models new-output dictionary");
var root=Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
using var manifest=JsonDocument.Parse(File.ReadAllBytes(args[0]));var m=manifest.RootElement;
var capture=m.GetProperty("capturedObservations");var raw=File.ReadAllBytes(Path.Combine(root,capture.GetProperty("file").GetString()!));
if(Convert.ToHexStringLower(SHA256.HashData(raw))!=capture.GetProperty("sha256").GetString())throw new InvalidDataException("capture hash");
var dictionary=JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(args[3]))!;
using var options=new SessionOptions{IntraOpNumThreads=2,InterOpNumThreads=1,LogSeverityLevel=OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR};
using var jp=new InferenceSession(Path.Combine(args[1],"rec.onnx"),options);
using var ascii=new OnnxAsciiOcr(Path.Combine(args[1],"ascii-rec.onnx"),Path.Combine(args[1],"ascii-dictionary.json"));
using var writer=new StreamWriter(new FileStream(args[2],FileMode.CreateNew));
foreach(var line in System.Text.Encoding.UTF8.GetString(raw).Split('\n',StringSplitOptions.RemoveEmptyEntries)){
 using var record=JsonDocument.Parse(line);var r=record.RootElement;var id=r.GetProperty("id").GetString();var page=r.GetProperty("page").GetInt32();
 var descriptor=m.GetProperty("documents").EnumerateArray().Single(d=>d.GetProperty("id").GetString()==id).GetProperty("pages").EnumerateArray().Single(p=>p.GetProperty("page").GetInt32()==page);
 var pixels=File.ReadAllBytes(Path.Combine(root,descriptor.GetProperty("pixels").GetProperty("file").GetString()!));if(Convert.ToHexStringLower(SHA256.HashData(pixels))!=r.GetProperty("sourceSha256").GetString())throw new InvalidDataException("pixels hash");
 var raster=new RecoveryRaster(r.GetProperty("Width").GetInt32(),r.GetProperty("Height").GetInt32(),pixels);
 foreach(var o in r.GetProperty("observations").EnumerateArray()){
 var pieces=o.GetProperty("Pieces").Deserialize<OcrRecognitionPiece[]>()!;var crop=o.GetProperty("RecognitionCrop").Deserialize<RecoveryBox>()!;
 var times=o.GetProperty("TimeCount").GetInt32();var valid=o.GetProperty("ValidWidth").GetInt32();var width=o.GetProperty("InputWidth").GetInt32();
 double X(double t)=>crop.X+crop.Width*t*width/(times*(double)valid);
 for(var i=1;i<pieces.Length-1;i++){
 if(pieces[i].Text is not (":" or "："))continue;
 var left=OcrWhiteGapPartition.Tail(raster,crop,X((pieces[i-1].Start+pieces[i-1].End)/2d),X((pieces[i].Start+pieces[i].End)/2d));
 var right=OcrWhiteGapPartition.Tail(raster,crop,X((pieces[i].Start+pieces[i].End)/2d),X((pieces[i+1].Start+pieces[i+1].End)/2d));
 foreach(var policy in new[]{"single","prefix"}){
 RecoveryBox? selected=right is null || policy=="single"&&left is null ? null : new RecoveryBox(policy=="single"?left!.X:crop.X,crop.Y,right!.X-(policy=="single"?left!.X:crop.X),crop.Height);
 if(selected is null || !selected.Valid){writer.WriteLine(JsonSerializer.Serialize(new{id,page,crop,index=i,policy,failure="no source white partition",original=pieces[i]}));continue;}
 var framed=new OcrInkCropFrame(raster,null,default,2).Restrict(selected);
 var input=OcrInputTransform.Recognition(raster,framed);using var output=jp.Run([NamedOnnxValue.CreateFromTensor("x",input.Tensor)]);var probs=output.First().AsTensor<float>();var decoded=OcrCtcDecoder.Decode(probs,dictionary).ToArray();var supported=true;
 foreach(var p in decoded)try{_=input.SourceGlyph(p.Text,p.Start,p.End,probs.Dimensions[1],0,0);}catch(InvalidDataException){supported=false;}
 var second=ascii.Read(raster,framed,0,0);
 writer.WriteLine(JsonSerializer.Serialize(new{id,page,crop,index=i,policy,selected,framed,original=pieces[i],originalPrefix=string.Concat(pieces.Take(i+1).Select(p=>p.Text)),jp=new{text=string.Concat(decoded.Select(p=>p.Text)),pieces=decoded,supported,input.ValidWidth,input.InputWidth},ascii=second}));writer.Flush();
 }
 }
 }
 CryptographicOperations.ZeroMemory(pixels);
}
