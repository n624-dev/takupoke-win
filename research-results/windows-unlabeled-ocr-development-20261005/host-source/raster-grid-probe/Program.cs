using System.Text.Json;
using System.Security.Cryptography;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using var input=JsonDocument.Parse(File.ReadAllBytes(args[0]));var row=input.RootElement.GetProperty("images")[0];
var pixels=File.ReadAllBytes(row.GetProperty("bgraPath").GetString()!);var hash=Convert.ToHexStringLower(SHA256.HashData(pixels));if(hash!=row.GetProperty("bgraSHA256").GetString())throw new InvalidDataException("Original pixel pin mismatch");
using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(2));var token=cancel.Token;var raster=new RecoveryRaster(row.GetProperty("width").GetInt32(),row.GetProperty("height").GetInt32(),pixels);
var rules=raster.Rules(token);var mask=raster.RuleMask(rules,token).Select(v=>v?(byte)1:(byte)0).ToArray();File.WriteAllBytes(args[1],mask);
var grid=new PdfGrid(new(raster.Width,raster.Height,[],rules),token);var xs=rules.Where(r=>r.Vertical).Select(r=>r.X1).Distinct().Order().ToArray();var ys=rules.Where(r=>r.Horizontal).Select(r=>r.Y1).Distinct().Order().ToArray();
if((long)xs.Length*ys.Length>5000)throw new InvalidDataException("Candidate capacity exceeded");var boxes=new HashSet<PdfBox>();
foreach(var x in xs.Zip(xs.Skip(1)))foreach(var y in ys.Zip(ys.Skip(1))){token.ThrowIfCancellationRequested();try{boxes.Add(grid.Box((x.First+x.Second)/2,(y.First+y.Second)/2));}catch(PdfParseException e)when(e.Stage!="limit"){} }
bool Covered(IEnumerable<(double Start,double End)> ranges,double start,double end){var covered=start;foreach(var r in ranges.OrderBy(r=>r.Start)){token.ThrowIfCancellationRequested();if(r.End<covered)continue;if(r.Start>covered)return false;covered=Math.Max(covered,r.End);if(covered>=end)return true;}return false;}
bool Closed(PdfBox b)=>Covered(rules.Where(r=>r.Horizontal&&r.Y1==b.Top).Select(r=>(r.X1,r.X2)),b.Left,b.Right)&&Covered(rules.Where(r=>r.Horizontal&&r.Y1==b.Bottom).Select(r=>(r.X1,r.X2)),b.Left,b.Right)&&Covered(rules.Where(r=>r.Vertical&&r.X1==b.Left).Select(r=>(r.Y1,r.Y2)),b.Top,b.Bottom)&&Covered(rules.Where(r=>r.Vertical&&r.X1==b.Right).Select(r=>(r.Y1,r.Y2)),b.Top,b.Bottom);
Console.WriteLine(JsonSerializer.Serialize(new{scope="Original pixels only. Measured boxes and independently closed continuous physical rails; no glyphs, aliases, oracle or role assignments",width=raster.Width,height=raster.Height,bgraSHA256=hash,rules,maskPath=Path.GetFullPath(args[1]),maskBytes=mask.Length,maskSHA256=Convert.ToHexStringLower(SHA256.HashData(mask)),boxes=boxes.OrderBy(b=>b.Top).ThenBy(b=>b.Left).Select(b=>new{b.Left,b.Top,b.Right,b.Bottom,closedRails=Closed(b)})},new JsonSerializerOptions{WriteIndented=true}));
