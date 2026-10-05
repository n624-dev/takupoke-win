using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;

namespace Takupoke.Win.UITests;
internal static partial class Program
{
    // Scripted confidence is a UI control, not a native OCR observation.
    // The independently drawn original bitmap/PDF and retained crops are real
    // temporary input bytes; the global ink receipt is computed, never assumed.
    private static async Task<RecoveryManualSession> SeedManualRecoveryUiAsync(string root, int count)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector());
        var lease = await store.BeginAsync(); var (document, _) = RecoveryUiFixture(lease.Period);
        var cell = document.Cells[0];
        var sources = document.Sources.Concat(new[] { new RecoverySource("teacher", cell.Id, 1, "架空教員A", new(110,125,60,10)) })
            .Select(s => s with { FromOcr = true, NativeConfidence = new[] { "subject", "teacher", "room" }.Take(count).Contains(s.Id) ? .4 : .95 }).ToArray();
        document = document with { Sources = sources, Cells = document.Cells.Select((c,i) => i == 0 ? c with {
            SourceIds = ["subject","room","teacher"], BlankFields = [], LessonBindings = [new(["subject"],["teacher"],["room"])] } : c).ToArray() };
        const int width = 650, height = 950;
        using var bitmap = new Bitmap(width,height,PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Arial",6,GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.White);
            foreach (var s in sources) graphics.DrawString(s.Text,font,Brushes.Black,(float)s.Box.X,(float)s.Box.Y,StringFormat.GenericTypographic);
        }
        var bgra = new byte[width*height*4];
        var locked = bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try { for (var y = 0; y < height; y++) Marshal.Copy(locked.Scan0 + y*locked.Stride,bgra,y*width*4,width*4); }
        finally { bitmap.UnlockBits(locked); }
        var raster = new RecoveryRaster(width,height,bgra);
        if (raster.HasUnrecognizedInk(sources.Select(s => s.Box).ToArray(), []))
            throw new InvalidDataException("Fictional UI raster contains unaccounted ink.");
        var bytes = ManualRasterPdf(raster); var hash = NotificationDiff.Digest(bytes);
        var path = Path.Combine(root,"fictional-manual-source.pdf"); await File.WriteAllBytesAsync(path,bytes);
        using var content = await new FileSourceReader(new WindowsFileIdentity()).ReadAsync(path,MaterialKind.Timetable,null);
        var now = DateTimeOffset.UtcNow;
        var source = new SourceRecord(Guid.NewGuid().ToString("N"),MaterialKind.Timetable,path,content.Identity,"fictional-manual-source.pdf",hash,bytes.Length,now,now,content.ModifiedAt);
        await store.SaveOriginalAsync(lease,source,bytes);
        document = document with { PdfHash = hash, Capture = new(hash,1,RecoveryValidator.Fingerprint(sources),
            [new(1,width,height,NotificationDiff.Digest(bgra),sources.Length,true)]) };
        var targets = RecoveryManualAssistance.CaptureTargets(document) ?? throw new InvalidDataException("Manual UI ownership control failed.");
        var crops = targets.Select(t => {
            var x=(int)Math.Floor(t.Crop.X);var y=(int)Math.Floor(t.Crop.Y);
            var w=(int)Math.Ceiling(t.Crop.X+t.Crop.Width)-x;var h=(int)Math.Ceiling(t.Crop.Y+t.Crop.Height)-y;
            var data=new byte[w*h*4];for(var row=0;row<h;row++) Array.Copy(bgra,((y+row)*width+x)*4,data,row*w*4,w*4);
            return new RecoveryOriginalCrop(t.Target,t.Page,x,y,w,h,NotificationDiff.Digest(bgra),data,NotificationDiff.Digest(data));
        }).ToArray();
        document = document with { Capture = document.Capture! with { OriginalCrops = crops } };
        var plan = RecoveryManualAssistance.Prepare(document) ?? throw new InvalidDataException("Manual UI input control refused.");
        if (plan.Targets.Count != count) throw new InvalidDataException("Manual field count differs from control.");
        var job = new RecoveryJob(hash,RecoveryDocumentKind.Timetable,RecoveryJobState.AwaitingManualCorrection,now) { ManualPlan = plan };
        await store.WriteAsync(lease,"acquisition.Timetable",new MaterialAttempt(now,null,false,hash));
        await store.WriteAsync(lease,"attempt.Timetable",new MaterialAttempt(now,"P13",true,hash,lease.Period.SchoolYear,ParserVersion:PdfScheduleParser.TimetableVersion,RecoveryPending:true));
        await store.WriteAsync(lease,"recovery.Timetable",job with { State = RecoveryJobState.Pending, ManualPlan = null });
        await store.SaveRecoveryProgressAsync(lease,source,job,null);
        return new(source.Id,lease,plan,now);
    }
    private static byte[] ManualRasterPdf(RecoveryRaster raster)
    {
        var rgb=new byte[raster.Width*raster.Height*3];
        for(var i=0;i<raster.Width*raster.Height;i++) { rgb[i*3]=raster.Bgra[i*4+2];rgb[i*3+1]=raster.Bgra[i*4+1];rgb[i*3+2]=raster.Bgra[i*4]; }
        using var compressed=new MemoryStream();using(var z=new ZLibStream(compressed,CompressionLevel.SmallestSize,true))z.Write(rgb);
        var image=compressed.ToArray();var commands=Encoding.ASCII.GetBytes($"q {raster.Width} 0 0 {raster.Height} 0 0 cm /Im0 Do Q");
        byte[] Ascii(string s)=>Encoding.ASCII.GetBytes(s);
        byte[] Stream(byte[] b,string dictionary)=>Ascii(dictionary+"\nstream\n").Concat(b).Concat(Ascii("\nendstream")).ToArray();
        byte[][] objects=[Ascii("<< /Type /Catalog /Pages 2 0 R >>"),Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {raster.Width} {raster.Height}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>"),
            Stream(image,$"<< /Type /XObject /Subtype /Image /Width {raster.Width} /Height {raster.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {image.Length} >>"),
            Stream(commands,$"<< /Length {commands.Length} >>")];
        using var pdf=new MemoryStream();pdf.Write(Ascii("%PDF-1.7\n"));var offsets=new List<long>();
        for(var i=0;i<objects.Length;i++){offsets.Add(pdf.Position);pdf.Write(Ascii($"{i+1} 0 obj\n"));pdf.Write(objects[i]);pdf.Write(Ascii("\nendobj\n"));}
        var xref=pdf.Position;pdf.Write(Ascii("xref\n0 6\n0000000000 65535 f \n"));
        foreach(var offset in offsets)pdf.Write(Ascii(offset.ToString("D10",System.Globalization.CultureInfo.InvariantCulture)+" 00000 n \n"));
        pdf.Write(Ascii($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));return pdf.ToArray();
    }
    private static void CheckManualRecoveryUi(string executable,string root)
    {
        foreach(var count in new[]{1,3})
        {
            Stop();var session=SeedManualRecoveryUiAsync(root,count).GetAwaiter().GetResult();
            var before=RecoveryUiFormalAsync(root).GetAwaiter().GetResult();
            Require(before is not null,"Manual control starts with a persisted lastgood analysis.");
            Start(executable);OpenRecoveryUiPreview();
            var keys=session.Plan.Targets.Select(t=>t.Target.Key).ToArray();
            Wait(()=>Find("manual-submit-Timetable") is not null,"Manual form opens for "+count+" fields");
            Require(!Find("manual-submit-Timetable")!.Current.IsEnabled,"Uncertain prefills do not count as acknowledgements.");
            foreach(var key in keys)
            {
                var ack=WaitElement("manual-ack-"+key);Require(!Checked(ack),"Every uncertain field starts unchecked.");
                Require(Find("manual-crop-"+key) is not null,"Original pixel crop is shown with the field.");
                var input=WaitElement("manual-value-"+key);
                ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue("架空手確認"+key);
                Toggle(ack);
            }
            Wait(()=>Find("manual-submit-Timetable")?.Current.IsEnabled==true,"All one/three acknowledgements enable preview only.");
            var first=keys[0];var edit=WaitElement("manual-value-"+first);edit.SetFocus();
            System.Windows.Forms.SendKeys.SendWait("{END}x");
            Wait(()=>!Checked(WaitElement("manual-ack-"+first)),"Keyboard editing clears that acknowledgement.");
            var typed=((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
            Toggle(WaitElement("manual-ack-"+first));
            var window=_window!.Current.BoundingRectangle;
            Require(SetWindowPos(_process!.MainWindowHandle,0,(int)window.Left,(int)window.Top,780,740,0x0044),"Resize manual form");
            Require(((ValuePattern)WaitElement("manual-value-"+first).GetCurrentPattern(ValuePattern.Pattern)).Current.Value==typed && Checked(WaitElement("manual-ack-"+first)),"Resize preserves typed field and acknowledgement.");
            Invoke("manual-original-Timetable");Wait(()=>RecoveryUiText("1 / 1ページ"),"Session-bound complete original PDF renders");Invoke(ByName("閉じる"));
            Require(((ValuePattern)WaitElement("manual-value-"+first).GetCurrentPattern(ValuePattern.Pattern)).Current.Value==typed && Checked(WaitElement("manual-ack-"+first)),"Same-source refresh preserves input and acknowledgement.");
            Invoke("manual-submit-Timetable");Wait(()=>Find("adopt-recovery-Timetable") is not null,"Manual completion opens whole-document preview");
            Require(RecoveryValidator.Fingerprint(RecoveryUiFormalAsync(root).GetAwaiter().GetResult())==RecoveryValidator.Fingerprint(before),"Completing fields preserves the entire lastgood analysis and audit before adoption.");
            Invoke("adopt-recovery-Timetable");Wait(()=>Find("adopt-recovery-Timetable") is null,"Explicit whole-document adoption commits manual provenance");
            var adopted=RecoveryUiFormalAsync(root).GetAwaiter().GetResult();
            Require(adopted?.Recovery?.Result.HumanCorrections?.Count==count && adopted.SourceDigest==session.Plan.Document.PdfHash,"All corrections are stored separately and bound to original PDF.");
            Stop();Start(executable);
            Require(RecoveryUiFormalAsync(root).GetAwaiter().GetResult()?.Recovery?.Result.HumanCorrections?.Count==count,"Manual correction audit persists across native app restart.");
        }
    }
}
