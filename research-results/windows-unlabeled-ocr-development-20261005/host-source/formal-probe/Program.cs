using System.Text.Json;
using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
var sourceRoot=args[0]; var inputPath=args[1];
using var input=JsonDocument.Parse(File.ReadAllBytes(inputPath));var row=input.RootElement.GetProperty("images")[0];
var bytes=File.ReadAllBytes(row.GetProperty("bgraPath").GetString()!);
if(Convert.ToHexStringLower(SHA256.HashData(bytes))!=row.GetProperty("bgraSHA256").GetString())throw new InvalidDataException("Pixel pin mismatch");
var hash=row.GetProperty("pngSHA256").GetString()!;
using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3));var token=deadline.Token;
var raster=new RecoveryRaster(row.GetProperty("width").GetInt32(),row.GetProperty("height").GetInt32(),bytes);
var rules=raster.Rules(token);var mask=raster.RuleMask(rules,token);var blank=raster.InkFreeScanner(mask,token);
PdfGlyph[] glyphs=[];
if(args.Length==2){
using var drawing=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(sourceRoot,"drawing-glyph-preflight.json")));
glyphs=drawing.RootElement.GetProperty("glyphs").EnumerateArray().Select(g=>{var b=g.GetProperty("box");return new PdfGlyph(g.GetProperty("text").GetString()!,b[0].GetDouble(),b[1].GetDouble(),b[2].GetDouble()-b[0].GetDouble(),b[3].GetDouble()-b[1].GetDouble(),g.GetProperty("sourceLine").GetInt32(),g.GetProperty("sourceOrder").GetInt32());}).ToArray();
}
JsonElement? nativeRecord=null;
if(args.Length>2){
    var records=File.ReadLines(args[2]).Select(l=>{try{return (JsonElement?)JsonDocument.Parse(l).RootElement.Clone();}catch(JsonException){return null;}}).Where(x=>x.HasValue&&x.Value.TryGetProperty("type",out var t)&&t.GetString()=="result").Select(x=>x!.Value).ToArray();
    if(records.Length!=1||!records[0].GetProperty("returned").GetBoolean())throw new InvalidDataException("Expected exactly one returned native record");
    nativeRecord=records[0];var lines=records[0].GetProperty("result").GetProperty("json_lines");var ids=new HashSet<int>();
    glyphs=lines.EnumerateArray().Select((l,index)=>{var id=l.GetProperty("id").GetInt32();if(id<0||!ids.Add(id))throw new InvalidDataException("Native line provenance duplicate");var b=l.GetProperty("boundingBox");if(b.GetArrayLength()!=4)throw new InvalidDataException("Native rectangle shape");var xs=b.EnumerateArray().Select(v=>v[0].GetDouble()).ToArray();var ys=b.EnumerateArray().Select(v=>v[1].GetDouble()).ToArray();if(xs.Distinct().Count()!=2||ys.Distinct().Count()!=2||b.EnumerateArray().Select(v=>(v[0].GetDouble(),v[1].GetDouble())).Distinct().Count()!=4)throw new InvalidDataException("Native rectangle is not axis aligned");return new PdfGlyph(l.GetProperty("text").GetString()!,xs.Min(),ys.Min(),xs.Max()-xs.Min(),ys.Max()-ys.Min(),id,index);}).ToArray();
}
var page=new PdfPageLayout(raster.Width,raster.Height,glyphs,rules);var results=new Dictionary<string,object?> { ["scope"]=args.Length>2?"Actual OCR whole-line outputs + actual raster host replay; no inference, no invented character boxes; gold after output":"Source drawing geometry preflight; no OCR inference, gold loaded only after output",["rules"]=rules.Count,["glyphs"]=glyphs.Length,["sourceHash"]=hash,["schema"]=RecoveryValidator.SchemaVersion,["validator"]=RecoveryValidator.Version,["operationalError"]=false };
results["unrecognizedInk"]=raster.HasUnrecognizedInk(glyphs.Select(g=>new RecoveryBox(g.X,g.Y,g.Width,g.Height)).ToArray(),rules,token);results["nativeSourceAdoptionEligible"]=nativeRecord is null?null:nativeRecord.Value.TryGetProperty("sourceAdoptionEligible",out var eligible)?eligible.GetBoolean():null;results["detectorConfidenceIsNotCharacterConfidence"]=nativeRecord is not null;results["nativeRawSha"]=args.Length>2?Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(args[2]))):null;results["originalNativeAtoms"]=nativeRecord?.GetProperty("result").GetProperty("json_lines");
var acquisitionEligible=!((bool)results["unrecognizedInk"]!)&&(nativeRecord is null||results["nativeSourceAdoptionEligible"] is true);results["adoptionEligible"]=acquisitionEligible;results["downstreamDiagnosticOnly"]=!acquisitionEligible;
string hostStage="builder";
// These exact source-guard messages come from the pinned ae07 Builder; budget,
// cancellation, unknown exceptions and capacity limits receive no semantic credit.
HashSet<string> sourceRefusals=["原文の文字位置が重複しています。","文字の位置を確認できません。","年度の独立した見出しがありません。","学期の見出しを確認できません。","クラス・日付・時限の見出しを確認できません。","時間割の同じ位置に複数のセル候補があります。","文字を読めなかったセルを空欄として扱えません。","科目・教員・教室の独立した原文ラベルを確認できません。","並記された各授業の独立した原文ラベルを確認できません。","時刻を確認できません。","共通の時刻表の見出しが一意ではありません。","複数のページで時刻が一致しません。","連続時限の範囲を確認できません。","時刻見出しの原文IDを確認できません。"];
TimetableAnalysis? strict=null,formal=null;RecoveryDocument? doc=null;
try { strict=PdfScheduleParser.Timetable([page],token);results["strict"] =strict; } catch(Exception e){results["strictError"]=new{type=e.GetType().Name,e.Message,stage=(e as PdfParseException)?.Stage};if(e is not PdfParseException p||p.Stage=="limit")results["operationalError"]=true;}
try {doc=RecoveryDocumentBuilder.Build(hash,MaterialKind.Timetable,[page],(_,b)=>blank(b),ocrPages:nativeRecord is null?null:new HashSet<int>{1},token:token);results["inputErrors"]=RecoveryValidator.InputErrors(doc);results["cells"]=doc.Cells.Count;results["fixedCells"]=doc.Cells.Count(c=>c.BindingMode==RecoveryBindingMode.Fixed);hostStage="engine";var run=await RecoveryEngine.RunAsync(doc,"windows",10,true,[],_=>null,token);results["runState"]=run.State.ToString();results["run"]=run;if(run.Result is not null){var now=DateTimeOffset.UtcNow;var source=new SourceRecord("fictional",MaterialKind.Timetable,"fake.png","fake.png","generated",hash,bytes.Length,now,now,null);hostStage="converter";formal=RecoveryAnalysisConverter.Convert(source,doc,run.Result,now).Timetable;results["formal"]=formal;}}catch(Exception e){var sourceRefusal=hostStage=="builder"&&e is InvalidDataException&&!e.Data.Contains("RecoveryWorkLimitExceeded")&&sourceRefusals.Contains(e.Message);results["recoveryError"]=new{type=e.GetType().Name,e.Message,stage=hostStage,failureClass=sourceRefusal?"sourceContractRefusal":"operationalOrUnclassifiedFailure"};if(!sourceRefusal)results["operationalError"]=true;}
// The independent literal oracle never reaches parser, Builder, engine, raster or a provider.
using var gold=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(sourceRoot,"literal-formal-oracle.json")));
bool Exact(TimetableAnalysis a){var g=gold.RootElement;if(a.SchoolYear!=g.GetProperty("schoolYear").GetInt32()||a.Term!=g.GetProperty("term").GetString())return false;var expected=g.GetProperty("lessons").EnumerateArray().Select(l=>new{Class=l.GetProperty("className").GetString(),Day=int.Parse(l.GetProperty("day").GetString()!),Period=l.GetProperty("period").GetInt32(),Subject=l.GetProperty("subject").GetString(),Teacher=l.GetProperty("teacher").GetString(),Room=l.GetProperty("room").GetString()}).ToArray();return a.Lessons.Count==expected.Length&&expected.All(l=>a.Lessons.Count(x=>x.ClassName==l.Class&&x.Weekday==l.Day&&x.Period==l.Period&&x.Names.Subject==l.Subject&&x.Names.Teacher==l.Teacher&&x.Names.Room==l.Room)==1);}
results["strictExact"]=strict is not null&&Exact(strict);results["formalExactPossible"]=formal is not null&&Exact(formal);results["recoveryFormalExact"]=acquisitionEligible&&formal is not null&&Exact(formal);results["actualRecoveryAccepted"]=acquisitionEligible&&formal is not null;results["providerCalls"]=0;
Console.WriteLine(JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
