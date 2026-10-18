using System.Text.Json;
using System.Security.Cryptography;
using Takupoke.Core.Recovery;
if(args.Length!=2)throw new ArgumentException("closed-actual-raw new-output");
using var output=new StreamWriter(new FileStream(args[1],FileMode.CreateNew));
using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
foreach(var line in File.ReadLines(args[0])){
 using var row=JsonDocument.Parse(line);var r=row.RootElement;var id=r.GetProperty("id").GetString();
 if(r.GetProperty("document").ValueKind==JsonValueKind.Null){output.WriteLine(JsonSerializer.Serialize(new{id,actualManualSchemaVersion=(int?)null,fields=Array.Empty<object>(),reviewPlanEligible=false,documentNotAcquired=true}));continue;}
 var original=r.GetProperty("document").Deserialize<RecoveryDocument>()!;
 if(original.Capture is null)throw new InvalidDataException("actual capture absent");
 var current=original with{Capture=original.Capture with{ManualSchemaVersion=RecoveryManualAssistance.CurrentManualSchemaVersion}};
 var before=RecoveryManualAssistance.CaptureTargets(original,timeout.Token);var targets=RecoveryManualAssistance.CaptureTargets(current,timeout.Token);
 var same=JsonSerializer.Serialize(before)==JsonSerializer.Serialize(targets);
 var plan=RecoveryManualAssistance.Prepare(current,timeout.Token);
 var run=await RecoveryEngine.RunAsync(current,"windows",10,true,[],_=>null,timeout.Token);
 var recovered=RecoveryRules.RecoverAll(current,timeout.Token);var sources=current.Sources.ToDictionary(s=>s.Id);var fields=new List<object>();
 for(var i=0;i<current.Cells.Count;i++){
 var cell=current.Cells[i];var value=recovered[i];if(value is null)continue;
 for(var n=0;n<value.Lessons.Count;n++)foreach(var(role,f)in new[]{("subject",value.Lessons[n].Subject),("teacher",value.Lessons[n].Teacher),("room",value.Lessons[n].Room)}){
 var scores=f.Evidence.Select(id=>sources[id].NativeConfidence).ToArray();foreach(var slot in cell.Slots)fields.Add(new{slot,lessonIndex=n,role,f.Value,f.Evidence,minimumConfidence=scores.Length==0||scores.Any(s=>s is null)?null:scores.Min(),cellId=cell.Id});
 }}
 output.WriteLine(JsonSerializer.Serialize(new{id,actualManualSchemaVersion=current.Capture!.ManualSchemaVersion,allSourcesFromOcr=current.Sources.All(s=>s.FromOcr),fields,reviewPlanEligible=plan is not null,reviewTargets=targets?.Select(t=>t.Target),sameTargetsIncludingOriginalCropGeometry=same,currentFormalPreview=run.Result is not null,currentErrors=run.Errors,sameSourceFingerprint=RecoveryValidator.Fingerprint(original.Sources)==RecoveryValidator.Fingerprint(current.Sources),rawLineSHA256=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(line))),scope="Post-close actual app schema4 validation of original captured sources/crops/metadata; zero inference, zero oracle/raster substitution/human acknowledgement. Original record remains unchanged; schema4 revalidation has no independent accuracy credit."}));output.Flush();
}
