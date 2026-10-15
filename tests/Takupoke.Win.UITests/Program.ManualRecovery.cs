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
    private static async Task<RecoveryManualSession> SeedManualRecoveryUiAsync(string root, int count, bool reviewHeader = false, int manualSchemaVersion = 1, bool confusableRoom = false,bool nativeReview = false, string? roomCode = null)
    {
        var preferences = new PreferencesStore(root);
        await preferences.SaveAsync((await preferences.LoadAsync()) with { UseAiFeatures = true });
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector());
        var lease = await store.BeginAsync(); var (document, _) = RecoveryUiFixture(lease.Period);
        if (roomCode is not null)
            await store.WriteAsync(lease, "api.mapping", new SavedMapping(new string('C',43), "fictional-codes", 1,
                "\"fictional-codes\"", new string('c',64), "2032-04-01T00:00:00Z", DateTimeOffset.UtcNow,
                new([], [], [new("I01O", "架空実習室"), new("l010", "別の架空室"), new("1010", "別学級限定", ["1_1"])])));
        var cell = document.Cells[0];
        var extra = new List<RecoverySource> { new("teacher", cell.Id, 1, "架空教員A", new(110,125,60,10)) };
        if(nativeReview)extra.Add(new("native-note","header",1,"架空の原本注記",new(10,920,70,10)));
        if (count == 4) extra.Add(new("subject-second", document.Cells[1].Id, 1, "架空科目B", new(110,210,60,10)));
        var sources = document.Sources.Concat(extra)
            .Select(s => confusableRoom && s.Id == "room" ? s with { Text = "架空室I1O0" } : s)
            .Select(s => roomCode is not null && s.Id == "room" ? s with { Text = roomCode } : s)
            .Select(s => s with { FromOcr = true, NativeConfidence = new[] { "subject", "teacher", "room", "subject-second" }.Take(count).Contains(s.Id) || reviewHeader && s.Id == "class" ? .4 : .95 }).ToArray();
        if(nativeReview)sources=sources.Select(source=>source with {FromOcr=false,NativeConfidence=null,
            RequiresVisualReview=source.NativeConfidence is <.8 || source.Id=="native-note"}).ToArray();
        document = document with { Sources = sources, Cells = document.Cells.Select((c,i) => i == 0 ? c with {
            SourceIds = ["subject","room","teacher"], BlankFields = [], LessonBindings = [new(["subject"],["teacher"],["room"])] } : c).ToArray() };
        if (count == 4) document = document with { Cells = document.Cells.Select((c,i) => i == 1 ? c with {
            SourceIds = ["subject-second"], BlankFields = ["teacher","room"], ConfirmedEmpty = false,
            LessonBindings = [new(["subject-second"],[],[])] } : c).ToArray() };
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
        // The review callback compares native render bytes, not the pre-PDF bitmap.
        // Compute this receipt independently with the same Windows PDF API.
        raster = await RenderManualPdfAsync(bytes, width, height);
        bgra = raster.Bgra;
        // Native PDF image rendering may add a faint resampling/encoding halo.
        // Retain those original rendered pixels in independently bounded source
        // boxes, rather than whitening them or asserting coverage without proof.
        sources = sources.Select(source => {
            var left = Math.Max(0, source.Box.X - 8); var top = Math.Max(0, source.Box.Y - 8);
            return source with { Box = new(left, top,
                Math.Min(width, source.Box.X + source.Box.Width + 8) - left,
                Math.Min(height, source.Box.Y + source.Box.Height + 8) - top) };
        }).ToArray();
        document = document with { Sources = sources };
        if (raster.HasUnrecognizedInk(sources.Select(s => s.Box).ToArray(), raster.Rules()))
            throw new InvalidDataException("Native fictional PDF contains unaccounted ink outside bounded source halos.");
        var path = Path.Combine(root,"fictional-manual-source.pdf"); await File.WriteAllBytesAsync(path,bytes);
        using var content = await new FileSourceReader(new WindowsFileIdentity()).ReadAsync(path,MaterialKind.Timetable,null);
        var now = DateTimeOffset.UtcNow;
        var source = new SourceRecord(Guid.NewGuid().ToString("N"),MaterialKind.Timetable,path,content.Identity,"fictional-manual-source.pdf",hash,bytes.Length,now,now,content.ModifiedAt);
        await store.SaveOriginalAsync(lease,source,bytes);
        document = document with { PdfHash = hash, Capture = new(hash,1,RecoveryValidator.Fingerprint(sources),
            [new(1,width,height,NotificationDiff.Digest(bgra),sources.Length,true)], nativeReview ? 4 : reviewHeader ? 2 : manualSchemaVersion),
            ReferenceNotes=nativeReview ? [new(1,sources.Single(s=>s.Id=="native-note").Box,["native-note"])] : null };
        var targets = RecoveryManualAssistance.CaptureTargets(document) ?? throw new InvalidDataException("Manual UI ownership control failed.");
        var crops = targets.Select(t => {
            var x=(int)Math.Floor(t.Crop.X);var y=(int)Math.Floor(t.Crop.Y);
            var w=(int)Math.Ceiling(t.Crop.X+t.Crop.Width)-x;var h=(int)Math.Ceiling(t.Crop.Y+t.Crop.Height)-y;
            var data=new byte[w*h*4];for(var row=0;row<h;row++) Array.Copy(bgra,((y+row)*width+x)*4,data,row*w*4,w*4);
            return new RecoveryOriginalCrop(t.Target,t.Page,x,y,w,h,NotificationDiff.Digest(bgra),data,NotificationDiff.Digest(data));
        }).ToArray();
        document = document with { Capture = document.Capture! with { OriginalCrops = crops } };
        var plan = RecoveryManualAssistance.Prepare(document) ?? throw new InvalidDataException("Manual UI input control refused.");
        if (plan.Targets.Count != count + (reviewHeader ? 1 : 0) + (nativeReview ? 1 : 0)) throw new InvalidDataException("Manual field count differs from control.");
        var job = new RecoveryJob(hash,RecoveryDocumentKind.Timetable,RecoveryJobState.AwaitingManualCorrection,now) { ManualPlan = plan };
        await store.WriteAsync(lease,"acquisition.Timetable",new MaterialAttempt(now,null,false,hash));
        await store.WriteAsync(lease,"attempt.Timetable",new MaterialAttempt(now,"P13",true,hash,lease.Period.SchoolYear,ParserVersion:PdfScheduleParser.TimetableVersion,RecoveryPending:true));
        await store.WriteAsync(lease,"recovery.Timetable",job with { State = RecoveryJobState.Pending, ManualPlan = null });
        await store.SaveRecoveryProgressAsync(lease,source,job,null);
        return new(source.Id,lease,plan,now);
    }
    private static async Task<RecoveryRaster> RenderManualPdfAsync(byte[] bytes, int width, int height)
    {
        using var input = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(input))
        { writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); }
        input.Seek(0);
        var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(input);
        using var page = pdf.GetPage(0);
        using var image = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(image, new Windows.Data.Pdf.PdfPageRenderOptions {
            DestinationWidth = (uint)width, DestinationHeight = (uint)height });
        image.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(image);
        var data = await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, new Windows.Graphics.Imaging.BitmapTransform(),
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        Require(decoder.PixelWidth == width && decoder.PixelHeight == height, "Native fixture render retains capture dimensions.");
        return new(width, height, data.DetachPixelData());
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
        foreach(var count in new[]{1,3,4})
        {
            Stop();var session=SeedManualRecoveryUiAsync(root,count,manualSchemaVersion: count == 4 ? 3 : 1).GetAwaiter().GetResult();
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
                var corrected = "架空手確認"+key;
                ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue(corrected);
                Wait(() => ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == corrected,
                    "Typed manual field is visible before acknowledgement.");
                Toggle(ack);
                Wait(() => Find("manual-ack-"+key) is { } currentAck && Checked(currentAck), "Manual acknowledgement remains explicitly checked.");
            }
            try { Wait(()=>Find("manual-submit-Timetable")?.Current.IsEnabled==true,"All one/three/four acknowledgements enable preview only."); }
            catch (TimeoutException)
            {
                foreach (var key in keys)
                {
                    var field=Find("manual-value-"+key);var ack=Find("manual-ack-"+key);
                    Console.Error.WriteLine($"Manual UI state {key}: text={((ValuePattern?)field?.GetCurrentPattern(ValuePattern.Pattern))?.Current.Value}, acknowledged={(ack is not null && Checked(ack))}, fieldEnabled={field?.Current.IsEnabled}, ackEnabled={ack?.Current.IsEnabled}");
                }
                Console.Error.WriteLine($"Manual UI submit enabled={Find("manual-submit-Timetable")?.Current.IsEnabled}");
                throw;
            }
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
            if (count == 1) CheckConfidentRecoveryReviewUi(root, typed);
            var expectedCount = count == 1 ? 2 : count;
            Invoke("adopt-recovery-Timetable");Wait(()=>Find("adopt-recovery-Timetable") is null,"Explicit whole-document adoption commits manual provenance");
            var adopted=RecoveryUiFormalAsync(root).GetAwaiter().GetResult();
            Require(adopted?.Recovery?.Result.HumanCorrections?.Count==expectedCount && adopted.SourceDigest==session.Plan.Document.PdfHash,"All corrections are stored separately and bound to original PDF.");
            Stop();Start(executable);
            Require(RecoveryUiFormalAsync(root).GetAwaiter().GetResult()?.Recovery?.Result.HumanCorrections?.Count==expectedCount,"Manual correction audit persists across native app restart.");
        }
    }
    private static void CheckRoomCandidatesUi(string executable, string root)
    {
        Stop(); var session = SeedManualRecoveryUiAsync(root, 3, manualSchemaVersion: 3, roomCode: "I010").GetAwaiter().GetResult();
        var before = RecoveryValidator.Fingerprint(RecoveryUiFormalAsync(root).GetAwaiter().GetResult());
        var room = session.Plan.Targets.Single(t => t.Target.Role == RecoveryFieldRole.Room).Target.Key;
        Start(executable); OpenRecoveryUiPreview();
        Wait(() => Find("manual-candidate-" + room + "-0") is not null, "Registered room-code alternatives appear beside the source image");
        Require(WaitElement("manual-candidate-" + room + "-0").Current.Name == "候補を入力: I01O", "Candidate uses the literal room roster and class scope");
        Require(Find("manual-candidate-" + room + "-2") is null, "Unrelated class does not supply a candidate");
        Require(((ValuePattern)WaitElement("manual-value-" + room).GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "I010",
            "Candidates never overwrite the original prefill automatically");
        Invoke("manual-candidate-" + room + "-0");
        Wait(() => ((ValuePattern)WaitElement("manual-value-" + room).GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "I01O",
            "Explicit candidate click fills only this field");
        Require(!Checked(WaitElement("manual-ack-" + room)), "Selecting a candidate does not acknowledge the pixels");
        foreach (var target in session.Plan.Targets) Toggle(WaitElement("manual-ack-" + target.Target.Key));
        Wait(() => WaitElement("manual-submit-Timetable").Current.IsEnabled, "Independent image acknowledgements enable preview");
        Invoke("manual-candidate-" + room + "-0");
        Wait(() => !Checked(WaitElement("manual-ack-" + room)) && !WaitElement("manual-submit-Timetable").Current.IsEnabled,
            "Choosing even the same candidate revokes previous acknowledgement");
        Toggle(WaitElement("manual-ack-" + room));
        Invoke("manual-submit-Timetable");
        Wait(() => Find("adopt-recovery-Timetable") is not null, "Candidate-assisted correction still opens a whole-document preview");
        var pending = RecoveryUiPendingAsync(root).GetAwaiter().GetResult();
        Require(pending.Document.Sources.Single(s => s.Id == "room").Text == "I010" &&
            pending.Result.HumanCorrections!.Single(c => c.Target.Role == RecoveryFieldRole.Room).CorrectedText == "I01O",
            "Original OCR and independently confirmed correction remain separate");
        Require(RecoveryValidator.Fingerprint(RecoveryUiFormalAsync(root).GetAwaiter().GetResult()) == before,
            "Candidate selection and preview preserve the last adopted analysis");
    }
    private static void CheckHeaderRecoveryUi(string executable, string root)
    {
        Stop(); var session = SeedManualRecoveryUiAsync(root, 0, reviewHeader: true).GetAwaiter().GetResult();
        var target = session.Plan.Targets.Single(); Require(target.HeaderProof?.Role == RecoveryHeaderReviewRole.Class, "Native header review owns the class source");
        var key = target.Target.Key; Start(executable); OpenRecoveryUiPreview();
        Wait(() => Find("manual-submit-Timetable") is not null, "Header-only source review opens");
        Require(Find("manual-crop-" + key) is not null && Find("manual-value-" + key) is null,
            "Header review shows original pixels with no editable text input");
        Require(!Find("manual-submit-Timetable")!.Current.IsEnabled, "Header reading is not automatically acknowledged");
        Toggle(WaitElement("manual-ack-" + key));
        Wait(() => Find("manual-submit-Timetable")?.Current.IsEnabled == true, "Unchanged header acknowledgement enables full preview");
        Invoke("manual-submit-Timetable"); Wait(() => Find("adopt-recovery-Timetable") is not null, "Header confirmation returns to full preview");
        var preview = RecoveryUiPendingAsync(root).GetAwaiter().GetResult();
        var review = preview.Result.HumanCorrections!.Single();
        Require(review.HeaderProof == target.HeaderProof && review.CorrectedText == target.OriginalOcr &&
            preview.Document.Sources.Single(s => s.Id == "class").NativeConfidence == .4,
            "Header text, native confidence and separate confirmation provenance are retained");
        Invoke("adopt-recovery-Timetable"); Wait(() => Find("adopt-recovery-Timetable") is null, "Explicit adoption stores unchanged header confirmation");
        Stop(); Start(executable);
        Require(RecoveryUiFormalAsync(root).GetAwaiter().GetResult()?.Recovery?.Result.HumanCorrections?.Single().HeaderProof == target.HeaderProof,
            "Read-only header review persists after native restart");
        Console.WriteLine("Native read-only header review: original pixels, explicit acknowledgement, unchanged text/score, adoption and restart passed.");
    }
    private static void CheckNativeSourceRecoveryUi(string executable,string root)
    {
        // Scripted native uncertainty tests UI/provenance, not acquisition or
        // OCR accuracy. Original crops are independently rendered PDF pixels.
        Stop();var session=SeedManualRecoveryUiAsync(root,1,reviewHeader:true,nativeReview:true).GetAwaiter().GetResult();
        var header=session.Plan.Targets.Single(t=>t.HeaderProof?.Role==RecoveryHeaderReviewRole.Class);
        var note=session.Plan.Targets.Single(t=>t.HeaderProof?.Role==RecoveryHeaderReviewRole.Reference);
        var body=session.Plan.Targets.Single(t=>t.HeaderProof is null);
        Start(executable);OpenRecoveryUiPreview();
        Wait(()=>Find("manual-submit-Timetable") is not null,"Native uncertainty opens original-pixel review");
        Require(Find("manual-value-"+header.Target.Key) is null && Find("manual-value-"+note.Target.Key) is null,"Native header and reference remain read only");
        Require(!WaitElement("manual-submit-Timetable").Current.IsEnabled,"Native text is not automatically acknowledged");
        Require(Find("manual-crop-"+body.Target.Key) is not null && Find("manual-crop-"+header.Target.Key) is not null,"Native body/header retain original pixel crops");
        var input=(ValuePattern)WaitElement("manual-value-"+body.Target.Key).GetCurrentPattern(ValuePattern.Pattern);
        input.SetValue("架空ネイティブ訂正");
        Wait(()=>input.Current.Value=="架空ネイティブ訂正","Native source correction is visible before acknowledgement");
        Toggle(WaitElement("manual-ack-"+body.Target.Key));Toggle(WaitElement("manual-ack-"+header.Target.Key));Toggle(WaitElement("manual-ack-"+note.Target.Key));
        Wait(()=>Find("manual-submit-Timetable")?.Current.IsEnabled==true,"All native sources require explicit acknowledgement");
        Invoke("manual-submit-Timetable");Wait(()=>Find("adopt-recovery-Timetable") is not null,"Native review returns to whole-document preview");
        ((ExpandCollapsePattern)WaitElement("recovery-reference-notes-Timetable").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Wait(()=>RecoveryUiText("1ページ · 架空の原本注記"),"Original native reference note is visible in preview");
        var preview=RecoveryUiPendingAsync(root).GetAwaiter().GetResult();
        Require(preview.Document.Sources.All(s=>!s.FromOcr && s.NativeConfidence is null) && preview.Result.HumanCorrections?.Count==3,
            "Native source is never relabelled OCR or assigned a fabricated confidence");
        Invoke("adopt-recovery-Timetable");Wait(()=>Find("adopt-recovery-Timetable") is null,"Explicit native adoption commits the checked result");
        Stop();Start(executable);
        var formal=RecoveryUiFormalAsync(root).GetAwaiter().GetResult();
        Require(formal?.Timetable?.Lessons.Any(l=>l.Names.Subject=="架空ネイティブ訂正")==true && formal.Recovery?.Document.ReferenceNotes?.Count==1 &&
            formal.Recovery.Result.HumanCorrections?.Count==3,"Native correction, read-only header provenance and reference notes survive restart");
        Console.WriteLine("Native source review: source pixels, header/body acknowledgement, correction, original note, explicit adoption and restart passed.");
    }
    private static async Task SeedBatchRecoveryPreviewAsync(string root)
    {
        var session=await SeedManualRecoveryUiAsync(root,1,confusableRoom:true);
        var target=session.Plan.Targets.Single();
        var correction=new RecoveryHumanCorrection(target.Target,session.Plan.Document.PdfHash,
            RecoveryValidator.Fingerprint(session.Plan.Document.Capture),session.Plan.DocumentSnapshot,
            target.OriginalParentIds,target.Page,target.Crop,target.OriginalOcr,false,DateTimeOffset.UtcNow);
        var result=RecoveryManualAssistance.Complete(session.Plan,[correction],"native-ui-control");
        await using var store=new SchoolDataStore(root,new WindowsDpapiProtector());
        var source=(await store.ReadAsync<SourceRecord>(session.Lease,"selection.Timetable"))!;
        var job=(await store.ReadAsync<RecoveryJob>(session.Lease,"recovery.Timetable"))!;
        await store.SaveRecoveryProgressAsync(session.Lease,source,job with {
            State=RecoveryJobState.AwaitingConfirmation,ManualPlan=null,ResultHash=RecoveryValidator.Fingerprint(result) },
            new RecoveryPreview(source.Id,session.Lease,session.Plan.Document,result,DateTimeOffset.UtcNow));
    }
    private static void CheckBatchRecoveryUi(string executable,string root)
    {
        Stop(); SeedBatchRecoveryPreviewAsync(root).GetAwaiter().GetResult();
        Start(executable);OpenRecoveryUiPreview();
        var original=RecoveryUiPendingAsync(root).GetAwaiter().GetResult();
        var cell=original.Document.Cells.First(c => c.LessonBindings.Count>0);
        var targets=new[] { new RecoveryFieldTarget(cell.Id,0,RecoveryFieldRole.Teacher),new RecoveryFieldTarget(cell.Id,0,RecoveryFieldRole.Room) };
        Require(!WaitElement("recovery-edit-selected-Timetable").Current.IsEnabled,"Empty batch selection is disabled");
        Require(WaitElement("recovery-code-hint-"+targets[1].Key).Current.Name.Contains("大文字アイ"),"Confident unreviewed room code spells out confusing character types");
        Invoke("recovery-select-rooms-Timetable");
        Require(((TogglePattern)WaitElement("recovery-select-"+targets[1].Key).GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState==ToggleState.On,
            "Bulk room selection selects the actual room checkbox");
        Require(((TogglePattern)WaitElement("recovery-select-"+targets[0].Key).GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState==ToggleState.Off,
            "Bulk room selection leaves teacher unchecked");
        Invoke("recovery-clear-selection-Timetable");
        Require(!WaitElement("recovery-edit-selected-Timetable").Current.IsEnabled,"Clearing bulk selection disables review");
        Toggle(WaitElement("recovery-select-"+targets[0].Key));
        Invoke("recovery-select-rooms-Timetable");
        Wait(() => Find("recovery-edit-selected-Timetable")?.Current.IsEnabled==true,"Two selected fields enable one batch review");
        Invoke("recovery-edit-selected-Timetable");
        Wait(() => Find("manual-submit-Timetable") is not null,"Batch selection opens original-image review");
        var plan=RecoveryUiManualPlanAsync(root).GetAwaiter().GetResult();
        Require(plan.Document.Capture?.ManualSchemaVersion==RecoveryManualAssistance.CurrentManualSchemaVersion && plan.Targets.Count==3,"Batch retains prior reviewed field and adds both confident fields");
        foreach(var target in plan.Targets)
        {
            if(target.Target.Role!=RecoveryFieldRole.Subject)
            {
                ((ValuePattern)WaitElement("manual-value-"+target.Target.Key).GetCurrentPattern(ValuePattern.Pattern)).SetValue("架空まとめ訂正"+target.Target.Role);
                Wait(() => ((ValuePattern)WaitElement("manual-value-"+target.Target.Key).GetCurrentPattern(ValuePattern.Pattern)).Current.Value=="架空まとめ訂正"+target.Target.Role,
                    "Batch edit is visible before acknowledgement");
            }
            Toggle(WaitElement("manual-ack-"+target.Target.Key));
        }
        Wait(() => Find("manual-submit-Timetable")?.Current.IsEnabled==true,"Every batch field is explicitly acknowledged");
        Invoke("manual-submit-Timetable");Wait(() => Find("adopt-recovery-Timetable") is not null,"Batch result returns to whole-document preview");
        Require(Find("recovery-code-hint-"+targets[1].Key) is null,"Explicitly reviewed room no longer repeats the code advice");
        Invoke("adopt-recovery-Timetable");Wait(() => Find("adopt-recovery-Timetable") is null,"Explicit adoption commits batch values");
        Stop();Start(executable);
        var saved=RecoveryUiFormalAsync(root).GetAwaiter().GetResult();
        Require(saved?.Recovery?.Result.HumanCorrections?.Count==3 && saved.Timetable!.Lessons.Any(l => l.Names.Teacher=="架空まとめ訂正Teacher" && l.Names.Room=="架空まとめ訂正Room"),
            "Batch values and independent provenance survive native app restart");
        Console.WriteLine("Native batch correction: selection, previous value, two confident fields, explicit adoption and restart passed.");
    }
    private static void CheckConfidentRecoveryReviewUi(string root, string previousValue)
    {
        const string subject = "c0:0:Subject", room = "c0:0:Room";
        var before = RecoveryUiPendingAsync(root).GetAwaiter().GetResult();
        Require(before.Document.Sources.Single(s => s.Id == "room").NativeConfidence == .95,
            "Selected room is intentionally high confidence; this is not an OCR accuracy measurement.");
        Invoke("recovery-edit-" + room);
        try { Wait(() => Find("manual-submit-Timetable") is not null, "High-confidence preview field opens native original capture review"); }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Selected-review operation status: " + Find("operation-status")?.Current.Name);
            throw;
        }
        Require(Find("manual-crop-" + room) is not null, "Native original room pixels are visible");
        Require(((ValuePattern)WaitElement("manual-value-" + subject).GetCurrentPattern(ValuePattern.Pattern)).Current.Value == previousValue,
            "Selecting another field retains the previous human input");
        var pending = RecoveryUiManualPlanAsync(root).GetAwaiter().GetResult();
        Require(pending.Document.Capture?.ManualSchemaVersion == RecoveryManualAssistance.CurrentManualSchemaVersion && pending.Targets.Count == 2,
            "Native review upgrades the receipt and includes previous reviewed targets");
        var crop = pending.Document.Capture!.OriginalCrops!.Single(c => c.Target.Key == room);
        var sourcePdf = File.ReadAllBytes(Path.Combine(root, "fictional-manual-source.pdf"));
        var page = RenderManualPdfAsync(sourcePdf, 650, 950).GetAwaiter().GetResult();
        var expected = new byte[crop.Width * crop.Height * 4];
        for (var y = 0; y < crop.Height; y++)
            Array.Copy(page.Bgra, ((crop.PixelY + y) * page.Width + crop.PixelX) * 4, expected, y * crop.Width * 4, crop.Width * 4);
        Require(crop.Bgra.SequenceEqual(expected) && crop.Sha256 == NotificationDiff.Digest(expected) &&
            crop.OriginalRasterHash == NotificationDiff.Digest(page.Bgra), "Selected crop is byte-for-byte native PDF source pixels");
        Invoke("manual-cancel-Timetable");
        Wait(() => Find("adopt-recovery-Timetable") is not null, "Cancel restores the prior valid preview");
        var restored = RecoveryUiPendingAsync(root).GetAwaiter().GetResult();
        Require(restored.SourceId == before.SourceId && restored.Lease == before.Lease &&
            RecoveryValidator.Fingerprint(restored.Document) == RecoveryValidator.Fingerprint(before.Document) &&
            RecoveryValidator.Fingerprint(restored.Result) == RecoveryValidator.Fingerprint(before.Result),
            "Cancel restores all previous preview values and acquisition proof");
        Invoke("recovery-edit-" + room);
        Wait(() => Find("manual-submit-Timetable") is not null, "High-confidence selection can be reopened after cancellation");
        var newPlan = RecoveryUiManualPlanAsync(root).GetAwaiter().GetResult();
        Require(newPlan.ReviewToken != pending.ReviewToken, "Reopening creates a fresh review session");
        ((ValuePattern)WaitElement("manual-value-" + room).GetCurrentPattern(ValuePattern.Pattern)).SetValue("架空訂正教室B");
        Wait(() => ((ValuePattern)WaitElement("manual-value-" + room).GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "架空訂正教室B",
            "Selected room edit is visible before acknowledgement");
        foreach (var key in new[] { subject, room })
        {
            Toggle(WaitElement("manual-ack-" + key));
            Wait(() => Checked(WaitElement("manual-ack-" + key)), "Explicit source acknowledgement for selected field");
        }
        Wait(() => Find("manual-submit-Timetable")?.Current.IsEnabled == true, "Prior correction plus selected field can be submitted");
        Invoke("manual-submit-Timetable");
        Wait(() => Find("adopt-recovery-Timetable") is not null, "Selected high-confidence correction returns to full preview");
        var revised = RecoveryUiPendingAsync(root).GetAwaiter().GetResult();
        Require(revised.Result.Cells.Single(c => c.CellId == "c0").Lessons[0].Room.Value == "架空訂正教室B" &&
            revised.Document.Sources.Single(s => s.Id == "room").Text == "架空教室A" &&
            revised.Document.Sources.Single(s => s.Id == "room").NativeConfidence == .95,
            "Human room value is separate from unchanged original OCR text and score");
        Require(RecoveryValidator.Validate(revised.Document, revised.Result).CanAdopt, "Selected correction retains full-document validation");
        Console.WriteLine("Native confident-field review: source pixels, previous input, cancel, fresh session and corrected preview passed.");
    }
    private static async Task<RecoveryPreview> RecoveryUiPendingAsync(string root)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector()); var lease = await store.BeginAsync();
        return await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Timetable")
            ?? throw new InvalidDataException("Pending preview missing.");
    }
    private static async Task<RecoveryManualPlan> RecoveryUiManualPlanAsync(string root)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector()); var lease = await store.BeginAsync();
        return (await store.ReadAsync<RecoveryJob>(lease, "recovery.Timetable"))?.ManualPlan
            ?? throw new InvalidDataException("Selected review plan missing.");
    }
}
