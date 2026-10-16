using System.Text.Json;
using System.Security.Cryptography;
using Takupoke.Core.Recovery;

// Closed-output inspection only: no model, gold, dictionary or networking.
if (args.Length != 3) throw new ArgumentException("Expected closed raw, pixel manifest and new inspection output.");
using var manifest = JsonDocument.Parse(File.ReadAllBytes(args[1]));
var docs = manifest.RootElement.GetProperty("documents").EnumerateArray().ToDictionary(d => d.GetProperty("id").GetString()!);
var root = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
using var writer = new StreamWriter(new FileStream(args[2], FileMode.CreateNew, FileAccess.Write));
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
foreach (var line in File.ReadLines(args[0]))
{
 using var row = JsonDocument.Parse(line); var r = row.RootElement; var id = r.GetProperty("id").GetString()!;
 var document = r.GetProperty("document").ValueKind == JsonValueKind.Null ? null : r.GetProperty("document").Deserialize<RecoveryDocument>();
 if (document is null) { writer.WriteLine(JsonSerializer.Serialize(new { id, condition = r.GetProperty("condition").GetString(), fields = Array.Empty<object>(), reviewPlanEligible = false, acquisitionFailed = true })); continue; }
 var sources = document.Sources.ToDictionary(s => s.Id);
 var recovered = RecoveryRules.RecoverAll(document, timeout.Token);
 var fields = new List<object>();
 for (var i = 0; i < document.Cells.Count; i++)
 {
  var cell = document.Cells[i]; var result = recovered[i];
  if (result is null) continue;
  for (var n = 0; n < result.Lessons.Count; n++)
  foreach (var (role,field) in new[] { ("subject", result.Lessons[n].Subject), ("teacher", result.Lessons[n].Teacher), ("room", result.Lessons[n].Room) })
  {
   var score = field.Evidence.Select(id => sources[id].NativeConfidence).ToArray();
   foreach (var slot in cell.Slots) fields.Add(new { slot, lessonIndex = n, role, field.Value, field.Evidence,
    minimumConfidence = score.Length == 0 || score.Any(s => s is null) ? null : score.Min(), cellId = cell.Id });
  }
 }
 var reviewed = document with { Capture = document.Capture! with { ManualSchemaVersion = RecoveryManualAssistance.CurrentManualSchemaVersion } };
 var targets = RecoveryManualAssistance.CaptureTargets(reviewed, timeout.Token);
 var eligible = false;
 if (targets is { Count: > 0 })
 {
  var crops = new List<RecoveryOriginalCrop>();
  foreach (var page in docs[id].GetProperty("pages").EnumerateArray())
  {
   var number = page.GetProperty("page").GetInt32(); var asset = page.GetProperty("pixels");
   var bytes = File.ReadAllBytes(Path.Combine(root, asset.GetProperty("file").GetString()!));
   var width = page.GetProperty("width").GetInt32(); var height = page.GetProperty("height").GetInt32();
   var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
   if (bytes.Length != width * height * 4 || digest != asset.GetProperty("sha256").GetString() ||
       digest != document.Capture!.Pages.Single(p => p.Page == number).RasterHash) throw new InvalidDataException("Original pixel identity failed.");
   foreach (var target in targets.Where(t => t.Page == number))
   {
    var x = (int)Math.Floor(target.Crop.X); var y = (int)Math.Floor(target.Crop.Y);
    var w = (int)Math.Ceiling(target.Crop.X + target.Crop.Width) - x; var h = (int)Math.Ceiling(target.Crop.Y + target.Crop.Height) - y;
    var crop = new byte[w * h * 4];
    for (var lane = 0; lane < h; lane++) Array.Copy(bytes, ((y + lane) * width + x) * 4, crop, lane * w * 4, w * 4);
    crops.Add(new(target.Target, number, x, y, w, h, digest, crop, Convert.ToHexStringLower(SHA256.HashData(crop))));
   }
  }
  reviewed = reviewed with { Capture = reviewed.Capture! with { OriginalCrops = crops } };
  eligible = RecoveryManualAssistance.Prepare(reviewed, timeout.Token) is not null;
 }
 writer.WriteLine(JsonSerializer.Serialize(new { id, condition = r.GetProperty("condition").GetString(), fields,
  reviewPlanEligible = eligible, actualManualSchemaVersion = reviewed.Capture!.ManualSchemaVersion, reviewTargets = targets?.Select(t => t.Target),
  inputErrors = RecoveryValidator.InputErrors(document, timeout.Token),
  scope = "Post-worker deterministic raw field/proof inspection; zero inference, zero user acknowledgements, no typed values or confidence changes" }));
 writer.Flush();
}
