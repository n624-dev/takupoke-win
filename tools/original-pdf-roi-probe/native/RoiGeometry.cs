using System.Security.Cryptography;
using System.Text;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
namespace AcquisitionResearch;
internal static class RoiGeometry
{
 internal static (int W,int H) ProductShape(double w,double h)
 {Check.That(double.IsFinite(w)&&double.IsFinite(h)&&w>0&&h>0,"native page size");int dw=(int)Math.Clamp(Math.Round(w*2),640,2400),dh=(int)Math.Round(h/w*dw);if(dh>3200){dw=(int)Math.Round(dw*3200d/dh);dh=3200;}Check.That(dw>0&&dw<=2400&&dh>0&&dh<=3200,"product render shape");return(dw,dh);}
 internal static RecoveryBox NativeRect(RecoveryBox roi,int w,int h,double nw,double nh)
 {Check.That(new RecoveryBox(0,0,w,h).Contains(roi)&&double.IsFinite(nw)&&double.IsFinite(nh)&&nw>0&&nh>0,"native coordinate source");return new(roi.X*nw/w,roi.Y*nh/h,roi.Width*nw/w,roi.Height*nh/h);}
 internal static RecoveryBox MapBack(RecoveryBox box,RecoveryBox roi,int w,int h)
 {Check.That(new RecoveryBox(0,0,w,h).Contains(box),"local actual raster coordinates");var mapped=new RecoveryBox(roi.X+box.X*roi.Width/w,roi.Y+box.Y*roi.Height/h,box.Width*roi.Width/w,box.Height*roi.Height/h);Check.That(roi.Contains(mapped),"mapped ROI bounds");return mapped;}
 internal static (RecoveryBox ROI,int Cells,int Work,int NonblankCells,int SelectedNonRuleInkPixels) Select(IReadOnlyList<PdfRule> rules,int w,int h,Func<RecoveryBox,int>? observedNonRuleInk=null,Func<RecoveryBox,bool>? eligibleRegion=null)
 {
  Check.That(rules.Count<=2048&&w>0&&h>0,"rule bound");var horizontal=new Dictionary<double,List<(double A,double B)>>();var vertical=new Dictionary<double,List<(double A,double B)>>();
  foreach(var r in rules){Check.That(new[]{r.X1,r.Y1,r.X2,r.Y2}.All(double.IsFinite)&&r.X1>=0&&r.Y1>=0&&r.X2<w&&r.Y2<h&&r.X1<=r.X2&&r.Y1<=r.Y2,"native raster rule bounds");if(r.Y1==r.Y2&&r.X1<r.X2){if(!horizontal.ContainsKey(r.Y1))horizontal[r.Y1]=[];horizontal[r.Y1].Add((r.X1,r.X2));}else if(r.X1==r.X2&&r.Y1<r.Y2){if(!vertical.ContainsKey(r.X1))vertical[r.X1]=[];vertical[r.X1].Add((r.Y1,r.Y2));}else throw new InvalidDataException("Non-axis observed rule");}
  var xs=vertical.Keys.Order().ToArray();var ys=horizontal.Keys.Order().ToArray();Check.That(xs.Length<=128&&ys.Length<=128,"rail cap");int work=0;
  bool Supports(List<(double A,double B)> spans,double a,double b){int matches=0;foreach(var s in spans){Check.That(++work<=2_000_000,"selection work cap");if(s.A<=a&&b<=s.B)matches++;}return matches==1;}
  var cells=new List<RecoveryBox>();for(int yi=0;yi+1<ys.Length;yi++)for(int xi=0;xi+1<xs.Length;xi++){double l=xs[xi],r=xs[xi+1],t=ys[yi],b=ys[yi+1];if(r-l<16||r-l>Math.Min(640,w/3d)||b-t<16||b-t>Math.Min(320,h/3d))continue;if(Supports(horizontal[t],l,r)&&Supports(horizontal[b],l,r)&&Supports(vertical[l],t,b)&&Supports(vertical[r],t,b)){cells.Add(new(l,t,r-l,b-t));Check.That(cells.Count<=4096,"cell cap");}}
  Check.That(cells.Count>0,"No bounded observed closed cell; no fallback/retry");string Key(RecoveryBox b)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes("windows-original-pdf-roi-v1:"+string.Join(',',new[]{b.X,b.Y,b.Width,b.Height}.Select(v=>BitConverter.DoubleToInt64Bits(v).ToString("x16"))))));
  var measured=cells.Select(b=>(Box:b,Ink:observedNonRuleInk?.Invoke(b)??0)).ToArray();
  Check.That(measured.All(c=>c.Ink>=0),"negative measured ink count");var eligible=measured.Where(c=>(observedNonRuleInk is null||c.Ink>0)&&(eligibleRegion is null||eligibleRegion(c.Box))).ToArray();
  Check.That(eligible.Length>0,"No nonblank bounded observed closed cell; no fallback/retry");var selected=eligible.OrderBy(c=>Key(c.Box),StringComparer.Ordinal).First();
  return(selected.Box,cells.Count,work,measured.Count(c=>c.Ink>0),selected.Ink);
 }
 internal static RecoveryBox SelectionInterior(RecoveryBox box)
 {
  // Selection only. The complete cell remains the detector input and coverage
  // denominator; edge ink is never discarded or declared empty.
  Check.That(box.Width>6&&box.Height>6,"interior selection bounds");
  return new(box.X+3,box.Y+3,box.Width-6,box.Height-6);
 }
 internal static int ObservedNonRuleInk(RecoveryRaster raster,bool[] mask,RecoveryBox box,ref long pixelWork,CancellationToken token=default)
 {
  Check.That(raster.Valid&&mask.Length==raster.Width*raster.Height&&new RecoveryBox(0,0,raster.Width,raster.Height).Contains(box),"observed ink raster/mask/box bounds");
  int count=0;for(int y=(int)Math.Ceiling(box.Y-.5);y<=Math.Floor(box.Y+box.Height-.5);y++)
  {token.ThrowIfCancellationRequested();for(int x=(int)Math.Ceiling(box.X-.5);x<=Math.Floor(box.X+box.Width-.5);x++)
   {if(++pixelWork>8_000_000)throw RecoveryWorkLimits.Exceeded("ROI selection pixel work cap");RecoveryRaster.PixelWork.GlobalWorkObserver?.Invoke(1);
    int i=y*raster.Width+x,p=i*4;if(!mask[i]&&(raster.Bgra[p]!=255||raster.Bgra[p+1]!=255||raster.Bgra[p+2]!=255))count++;}}
  return count;
 }
 internal static void Controls()
 {
  var rules=new List<PdfRule>();foreach(int y in new[]{0,60,120})rules.Add(new(0,y,200,y));foreach(int x in new[]{0,100,200})rules.Add(new(x,0,x,120));var r=Select(rules,900,600);Check.That(r.Cells==4,"closed cells positive");Check.That(Select(rules.AsEnumerable().Reverse().ToArray(),900,600).ROI==r.ROI,"ordering independence");
  void Reject(Action a){bool rejected=false;try{a();}catch(InvalidDataException){rejected=true;}Check.That(rejected,"missing negative refusal");}
  Reject(()=>Select(rules.Take(2).ToArray(),900,600));Reject(()=>Select(rules.Concat(rules).ToArray(),900,600));Reject(()=>Select([new(0,0,90,0),new(0,60,90,60),new(0,0,0,60),new(100,0,100,60)],900,600));Reject(()=>Select([new(0,0,double.NaN,0)],900,600));Reject(()=>Select([new(0,0,100,1)],900,600));Reject(()=>Select(Enumerable.Repeat(rules[0],2049).ToArray(),900,600));
  Check.That(ProductShape(4060,2800)==(2400,1655)&&ProductShape(600,2400)==(800,3200),"production shapes");Reject(()=>ProductShape(double.PositiveInfinity,100));var roi=new RecoveryBox(120,300,240,60);Check.That(MapBack(new(0,0,960,243),roi,960,243)==roi,"per axis actualdims");Reject(()=>MapBack(new(0,0,960.000001,243),roi,960,243));Reject(()=>NativeRect(new(899,0,2,1),900,600,1200,800));
  var native=NativeRect(roi,2400,1600,5413.333,3733.333);Check.That(Math.Abs(native.X-270.66665)<1e-9&&Math.Abs(native.Y-699.9999375)<1e-9,"native units not guessed PDF points");
  // A printed-rule-only raster is not a useful detector comparison input.
  var pixels=Enumerable.Repeat((byte)255,900*600*4).ToArray();foreach(var line in rules)for(int y=(int)line.Y1;y<=line.Y2;y++)for(int x=(int)line.X1;x<=line.X2;x++){int p=(y*900+x)*4;pixels[p]=pixels[p+1]=pixels[p+2]=0;}
  var raster=new RecoveryRaster(900,600,pixels);var mask=raster.RuleMask(rules);long scan=0;
  int Count(RecoveryBox b)=>ObservedNonRuleInk(raster,mask,b,ref scan);
  Reject(()=>Select(rules,900,600,Count));int ink=(90*900+150)*4;pixels[ink]=pixels[ink+1]=pixels[ink+2]=0;scan=0;
  var nonblank=Select(rules,900,600,Count);Check.That(nonblank.Cells==4&&nonblank.NonblankCells==1&&nonblank.SelectedNonRuleInkPixels==1&&nonblank.ROI==new RecoveryBox(100,60,100,60),"one measured nonblank cell");
  scan=0;Check.That(Select(rules,900,600,Count,b=>Count(SelectionInterior(b))>0).ROI==nonblank.ROI,"interior ink selection retains complete cell");
  pixels[ink]=pixels[ink+1]=pixels[ink+2]=255;int edgeInk=(62*900+150)*4;pixels[edgeInk]=pixels[edgeInk+1]=pixels[edgeInk+2]=254;scan=0;
  Check.That(Count(nonblank.ROI)==1,"border residue remains source ink");
  Reject(()=>Select(rules,900,600,Count,b=>Count(SelectionInterior(b))>0));
  pixels[edgeInk]=pixels[edgeInk+1]=pixels[edgeInk+2]=255;pixels[ink]=pixels[ink+1]=pixels[ink+2]=0;
  scan=0;Check.That(Select(rules.AsEnumerable().Reverse().ToArray(),900,600,Count).ROI==nonblank.ROI,"nonblank ordering independence");
  mask[90*900+150]=true;scan=0;Reject(()=>Select(rules,900,600,Count));mask[90*900+150]=false;
  Reject(()=>ObservedNonRuleInk(raster,new bool[1],nonblank.ROI,ref scan));Reject(()=>ObservedNonRuleInk(raster,mask,new(899,0,2,1),ref scan));
  Reject(()=>Select(rules,900,600,_=>-1));scan=8_000_000;bool budget=false;try{Count(nonblank.ROI);}catch(InvalidDataException e){budget=RecoveryWorkLimits.IsExceeded(e);}Check.That(budget,"selection budget remains fatal");
  scan=0;using var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;try{ObservedNonRuleInk(raster,mask,nonblank.ROI,ref scan,cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check.That(cancelled,"selection cancellation");
  pixels[ink]=pixels[ink+1]=pixels[ink+2]=254;scan=0;Check.That(Count(nonblank.ROI)==1,"production exact nonwhite criterion unchanged");
  var openPixels=Enumerable.Repeat((byte)255,900*600*4).ToArray();
  void Dark(int x,int y){int p=(y*900+x)*4;openPixels[p]=openPixels[p+1]=openPixels[p+2]=0;}
  foreach(int y in new[]{20,100,180})for(int x=20;x<900;x++)Dark(x,y);
  foreach(int x in new[]{20,120,220})for(int y=20;y<=180;y++)Dark(x,y);
  var open=new RecoveryRaster(900,600,openPixels);
  Check.That(open.Rules().Count==0,"open outer stroke baseline refusal");
  var interior=open.Rules(retainClosedInterior:true);
  Check.That(interior.Count==6&&Select(interior,900,600).Cells==4,"closed interior survives open outer stroke");
  Check.That(!open.RuleMask(interior)[100*900+800],"unsupported outer tail never masked as table rule");
  var solitaryPixels=Enumerable.Repeat((byte)255,900*600*4).ToArray();
  for(int x=20;x<=400;x++){int p=(80*900+x)*4;solitaryPixels[p]=solitaryPixels[p+1]=solitaryPixels[p+2]=0;}
  Check.That(new RecoveryRaster(900,600,solitaryPixels).Rules(retainClosedInterior:true).Count==0,"isolated glyph remains ink");
  Console.WriteLine("ROI_GEOMETRY_32_CONTROLS_PASS_NATIVE_CALLS_0");
 }
}
