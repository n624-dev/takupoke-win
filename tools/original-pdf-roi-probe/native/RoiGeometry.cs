using System.Security.Cryptography;
using System.Text;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
namespace AcquisitionResearch;
internal static class RoiGeometry
{
 internal static (int W,int H) ProductShape(double w,double h)
 {Check.That(double.IsFinite(w)&&double.IsFinite(h)&&w>0&&h>0,"native page size");int dw=(int)Math.Clamp(Math.Round(w*2),640,2400),dh=(int)Math.Round(h/w*dw);if(dh>3200){dw=(int)Math.Round(dw*3200d/dh);dh=3200;}Check.That(dw>0&&dw<=2400&&dh>0&&dh<=3200,"product render shape");return(dw,dh);}
 internal static RecoveryBox NativeRect(RecoveryBox roi,int w,int h,double nw,double nh)
 {Check.That(new RecoveryBox(0,0,w,h).Contains(roi)&&double.IsFinite(nw)&&double.IsFinite(nh)&&nw>0&&nh>0,"native coordinate source");return new(roi.X*nw/w,roi.Y*nh/h,roi.Width*nw/w,roi.Height*nh/h);}
 internal static RecoveryBox MapBack(RecoveryBox box,RecoveryBox roi,int w,int h)
 {Check.That(new RecoveryBox(0,0,w,h).Contains(box),"local actual raster coordinates");var mapped=new RecoveryBox(roi.X+box.X*roi.Width/w,roi.Y+box.Y*roi.Height/h,box.Width*roi.Width/w,box.Height*roi.Height/h);Check.That(roi.Contains(mapped),"mapped ROI bounds");return mapped;}
 internal static (RecoveryBox ROI,int Cells,int Work) Select(IReadOnlyList<PdfRule> rules,int w,int h)
 {
  Check.That(rules.Count<=2048&&w>0&&h>0,"rule bound");var horizontal=new Dictionary<double,List<(double A,double B)>>();var vertical=new Dictionary<double,List<(double A,double B)>>();
  foreach(var r in rules){Check.That(new[]{r.X1,r.Y1,r.X2,r.Y2}.All(double.IsFinite)&&r.X1>=0&&r.Y1>=0&&r.X2<w&&r.Y2<h&&r.X1<=r.X2&&r.Y1<=r.Y2,"native raster rule bounds");if(r.Y1==r.Y2&&r.X1<r.X2){if(!horizontal.ContainsKey(r.Y1))horizontal[r.Y1]=[];horizontal[r.Y1].Add((r.X1,r.X2));}else if(r.X1==r.X2&&r.Y1<r.Y2){if(!vertical.ContainsKey(r.X1))vertical[r.X1]=[];vertical[r.X1].Add((r.Y1,r.Y2));}else throw new InvalidDataException("Non-axis observed rule");}
  var xs=vertical.Keys.Order().ToArray();var ys=horizontal.Keys.Order().ToArray();Check.That(xs.Length<=128&&ys.Length<=128,"rail cap");int work=0;
  bool Supports(List<(double A,double B)> spans,double a,double b){int matches=0;foreach(var s in spans){Check.That(++work<=2_000_000,"selection work cap");if(s.A<=a&&b<=s.B)matches++;}return matches==1;}
  var cells=new List<RecoveryBox>();for(int yi=0;yi+1<ys.Length;yi++)for(int xi=0;xi+1<xs.Length;xi++){double l=xs[xi],r=xs[xi+1],t=ys[yi],b=ys[yi+1];if(r-l<16||r-l>Math.Min(640,w/3d)||b-t<16||b-t>Math.Min(320,h/3d))continue;if(Supports(horizontal[t],l,r)&&Supports(horizontal[b],l,r)&&Supports(vertical[l],t,b)&&Supports(vertical[r],t,b)){cells.Add(new(l,t,r-l,b-t));Check.That(cells.Count<=4096,"cell cap");}}
  Check.That(cells.Count>0,"No bounded observed closed cell; no fallback/retry");string Key(RecoveryBox b)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes("windows-original-pdf-roi-v1:"+string.Join(',',new[]{b.X,b.Y,b.Width,b.Height}.Select(v=>BitConverter.DoubleToInt64Bits(v).ToString("x16"))))));
  return(cells.OrderBy(Key,StringComparer.Ordinal).First(),cells.Count,work);
 }
 internal static void Controls()
 {
  var rules=new List<PdfRule>();foreach(int y in new[]{0,60,120})rules.Add(new(0,y,200,y));foreach(int x in new[]{0,100,200})rules.Add(new(x,0,x,120));var r=Select(rules,900,600);Check.That(r.Cells==4,"closed cells positive");Check.That(Select(rules.AsEnumerable().Reverse().ToArray(),900,600).ROI==r.ROI,"ordering independence");
  void Reject(Action a){bool rejected=false;try{a();}catch(InvalidDataException){rejected=true;}Check.That(rejected,"missing negative refusal");}
  Reject(()=>Select(rules.Take(2).ToArray(),900,600));Reject(()=>Select(rules.Concat(rules).ToArray(),900,600));Reject(()=>Select([new(0,0,90,0),new(0,60,90,60),new(0,0,0,60),new(100,0,100,60)],900,600));Reject(()=>Select([new(0,0,double.NaN,0)],900,600));Reject(()=>Select([new(0,0,100,1)],900,600));Reject(()=>Select(Enumerable.Repeat(rules[0],2049).ToArray(),900,600));
  Check.That(ProductShape(4060,2800)==(2400,1655)&&ProductShape(600,2400)==(800,3200),"production shapes");Reject(()=>ProductShape(double.PositiveInfinity,100));var roi=new RecoveryBox(120,300,240,60);Check.That(MapBack(new(0,0,960,243),roi,960,243)==roi,"per axis actualdims");Reject(()=>MapBack(new(0,0,960.000001,243),roi,960,243));Reject(()=>NativeRect(new(899,0,2,1),900,600,1200,800));
  var native=NativeRect(roi,2400,1600,5413.333,3733.333);Check.That(Math.Abs(native.X-270.66665)<1e-9&&Math.Abs(native.Y-699.9999375)<1e-9,"native units not guessed PDF points");Console.WriteLine("ROI_GEOMETRY_15_CONTROLS_PASS_NATIVE_CALLS_0");
 }
}
