using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
namespace AcquisitionResearch;
internal sealed record View(int Id,RecoveryBox Core,RecoveryBox ROI,int DetectorWidth,int DetectorHeight)
{
 public RecoveryBox MapToPage(RecoveryBox b)=>new(ROI.X+b.X*ROI.Width/DetectorWidth,ROI.Y+b.Y*ROI.Height/DetectorHeight,b.Width*ROI.Width/DetectorWidth,b.Height*ROI.Height/DetectorHeight);
 public bool Owns(double x,double y)=>Core.X<=x&&x<Core.X+Core.Width&&Core.Y<=y&&y<Core.Y+Core.Height;
}
internal sealed record Candidate(View View,RecoveryBox Box,bool InternalEdge);
internal static class Geometry
{
 public const int Core=768,Halo=96;
 public static (int W,int H) Shape(double w,double h)
 {var scale=Math.Min(1,960/Math.Max(w,h));return(Math.Max(32,(int)Math.Round(w*scale/32)*32),Math.Max(32,(int)Math.Round(h*scale/32)*32));}
 public static View Whole(int w,int h){var (dw,dh)=Shape(w,h);return new(0,new(0,0,w,h),new(0,0,w,h),dw,dh);}
 public static View[] Tiles(int w,int h)
 {
  Check.That(w is >0 and <=4096&&h is >0 and <=4096,"raster bounds");var result=new List<View>();
  for(int y=0;y<h;y+=Core)for(int x=0;x<w;x+=Core)
  {var core=new RecoveryBox(x,y,Math.Min(Core,w-x),Math.Min(Core,h-y));var left=Math.Max(0,x-Halo);var top=Math.Max(0,y-Halo);var right=Math.Min(w,x+Core+Halo);var bottom=Math.Min(h,y+Core+Halo);var roi=new RecoveryBox(left,top,right-left,bottom-top);var(dw,dh)=Shape(roi.Width,roi.Height);result.Add(new(result.Count,core,roi,dw,dh));}
  Check.That(result.Count<=36,"tile cap");return result.ToArray();
 }
 public static Candidate[] Decode(View view,float[] map,int pageWidth,int pageHeight,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();int dw=view.DetectorWidth,dh=view.DetectorHeight;Check.That(map.Length==dw*dh,"detector map shape");var probabilities=new float[map.Length];for(int i=0;i<map.Length;i++){if(i%dw==0)token.ThrowIfCancellationRequested();probabilities[i]=OcrDetectorProbability.Normalize(map[i]);}
  var visited=new bool[map.Length];var boxes=new List<Candidate>();
  for(int y=0;y<dh;y++)for(int x=0;x<dw;x++)
  {
   token.ThrowIfCancellationRequested();if(visited[y*dw+x]||probabilities[y*dw+x]<.3f)continue;
   var queue=new Queue<(int X,int Y)>();queue.Enqueue((x,y));visited[y*dw+x]=true;int left=x,top=y,right=x,bottom=y,count=0;double score=0;
   while(queue.TryDequeue(out var p)){token.ThrowIfCancellationRequested();count++;score+=probabilities[p.Y*dw+p.X];left=Math.Min(left,p.X);right=Math.Max(right,p.X);top=Math.Min(top,p.Y);bottom=Math.Max(bottom,p.Y);foreach(var(nx,ny)in new[]{(p.X-1,p.Y),(p.X+1,p.Y),(p.X,p.Y-1),(p.X,p.Y+1)})if(nx>=0&&ny>=0&&nx<dw&&ny<dh&&!visited[ny*dw+nx]&&probabilities[ny*dw+nx]>=.3f){visited[ny*dw+nx]=true;queue.Enqueue((nx,ny));}}
   if(count<6||score/count<.6)continue;var margin=Math.Max(1,(bottom-top+1)*.25);
   double bx=Math.Max(0,left-margin),by=Math.Max(0,top-margin),ex=Math.Min(dw,right+1+margin),ey=Math.Min(dh,bottom+1+margin);
   var box=view.MapToPage(new(bx,by,ex-bx,ey-by));bool edge=(bx==0&&view.ROI.X>0)||(by==0&&view.ROI.Y>0)||(ex==dw&&view.ROI.X+view.ROI.Width<pageWidth)||(ey==dh&&view.ROI.Y+view.ROI.Height<pageHeight);
   boxes.Add(new(view,box,edge));Check.That(boxes.Count<=10000,"production detector candidate cap");
  }
  return boxes.ToArray();
 }
}
internal static class Check{public static void That(bool okay,string reason){if(!okay)throw new InvalidDataException(reason);}}
