using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

// The only inputs are source bytes and their identity. No expected content,
// roles, coordinates or cells enter the native acquisition/recovery process.
if (args.Length != 2) throw new ArgumentException("Expected fictional source path and digest.");
var bytes = await File.ReadAllBytesAsync(args[0]);
var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
if (hash != args[1] || bytes.Length is < 1 or > 50*1024*1024) throw new InvalidDataException("Source identity failed.");
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(6));
var token = lifetime.Token; var watch = Stopwatch.StartNew();
var capture = new RecoveryReadCapture();
var originalImageSizes=RecoveryPdfImageResolution.Inspect(bytes,token);
var ownedModelRoot=Path.Combine(Path.GetTempPath(),"takupoke-ordered-raster-models-"+(Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local")+"-"+Guid.NewGuid().ToString("N"));
string stage = "Reader", outcome = "unassessed"; string? strictFailure = null, failure = null;
TimetableAnalysis? formal = null; RecoveryDocument? doc = null; IReadOnlyList<PdfPageLayout>? pages = null;
Dictionary<string,object?>? cropFailure=null;
var rasterCaptures=new List<RecoveryRasterCaptureInfo>();
var shadowPages=new List<object>(); string? shadowError=null;
try
{
    try
    {
        pages = PdfPigLayoutReader.Read(bytes, MaterialKind.Timetable, token, capture);
        stage = "Strict"; formal = PdfScheduleParser.Timetable(pages, token); outcome = "strict-returned";
    }
    catch (PdfParseException error)
    {
        strictFailure = error.Stage;
        if (!RecoveryPolicy.Eligible(MaterialKind.Timetable, strictFailure)) outcome = "strict-ineligible";
        else
        {
            stage = "OCR prerequisites";
            var models=new WindowsRecoveryModels(ownedModelRoot);
            await models.InstallOcrAsync(null,token);
            stage = "Windows render/OCR/Builder";
            doc = await new WindowsPdfRecovery(models).BuildAsync(bytes,MaterialKind.Timetable,hash,capture,token,rasterCaptures.Add);
            if (doc is null) outcome = "raster-evidence-required-refused";
            else
            {
                stage = "Engine";
                var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null, token);
                if (run.Result is null) { outcome = "engine-refused"; failure = string.Join(',', run.Errors); }
                else
                {
                    stage = "Validator"; var validation = RecoveryValidator.Validate(doc, run.Result, token);
                    if (!validation.CanAdopt) { outcome = "validator-refused"; failure = string.Join(',', validation.Errors); }
                    else
                    {
                        stage = "Formal";
                        var source = new SourceRecord("invented", MaterialKind.Timetable, "fictional.pdf", "fictional", "fictional.pdf", hash, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
                        formal = RecoveryAnalysisConverter.Convert(source, doc, run.Result, DateTimeOffset.UnixEpoch, token).Timetable;
                        outcome = "recovery-formal-returned";
                    }
                }
            }
        }
    }
}
catch (PdfParseException e) { outcome = "safe-refusal"; failure = e.Stage; }
catch (InvalidDataException e) {
    outcome = stage=="OCR prerequisites" || e.Data.Contains("RecoveryWorkLimitExceeded") ? "execution-error":"safe-refusal"; failure = e.Message;
    var keys=new[]{"OcrCropConflict","OcrCropOwner","OcrCropAttachedRule","OcrCropTextPixels","OcrCropComponentPixels","OcrCropComponentBounds","OcrCropCandidateCount","OcrCropRuleCount","OcrCropRules","OcrCropUnmaskedRows","OcrCropUnmaskedColumns","OcrRecognitionPieceCount","OcrRecognitionLowWhitespaceCount","OcrRecognitionLowBodyCount","OcrRecognitionCrop","OcrRecognitionInput"};
    cropFailure=keys.Append("OcrRecognitionWhitespacePieces").Where(key=>e.Data.Contains(key)).ToDictionary(key=>key,key=>e.Data[key]);
}
catch (OperationCanceledException) { outcome = "execution-error"; failure = "cancelled-or-deadline"; }
catch (Exception e) { outcome = "execution-error"; failure = e.GetType().Name+":"+e.Message; }
finally
{
    // This probe's measurement has no path to formal, Builder or adoption.
    // Expected literals are still unknown; only source identity enters here.
    try
    {
        if (Directory.Exists(ownedModelRoot) && originalImageSizes.Count is > 0 and <= 12)
        {
            using var reader=await new WindowsRecoveryModels(ownedModelRoot).OpenOcrAsync(token);
            using var japanese=await OpenJapaneseReader(ownedModelRoot,token);
            foreach(var page in originalImageSizes.Keys.Order())
            {
                token.ThrowIfCancellationRequested();
                using var original=RecoveryPdfOriginalImage.TryRead(bytes,page,token);
                if(original is null) throw new InvalidDataException("Original image is not available for diagnostic measurement.");
                using var stream=new InMemoryRandomAccessStream();
                using(var writer=new DataWriter(stream)) {writer.WriteBytes(original.Png);await writer.StoreAsync().AsTask(token);writer.DetachStream();}
                stream.Seek(0); var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(token);
                var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.ColorManageToSRgb).AsTask(token);
                var raster=new RecoveryRaster((int)decoder.PixelWidth,(int)decoder.PixelHeight,pixels.DetachPixelData());
                try
                {
                    var rows=new List<object>();
                    reader.RecognitionObserver=o=>rows.Add(new {crop=o.RecognitionCrop,o.ValidWidth,o.InputWidth,o.TimeCount,pieces=o.Pieces,
                        paddingSupported=o.Pieces.All(piece=>piece.Start>=0 && piece.End>piece.Start && piece.End<=o.TimeCount && (long)piece.End*o.InputWidth<=(long)o.ValidWidth*o.TimeCount)});
                    var pixelHash=Convert.ToHexStringLower(SHA256.HashData(raster.Bgra));
                    await Task.Run(()=>reader.ObserveAllRecognition(raster,token),token);
                    if(reader.RecognizedBoxes.Count!=0 || reader.NativeConfidences.Count!=0) throw new InvalidOperationException("Shadow cannot expose usable capture.");
                    var japaneseRows=new List<object>();
                    japanese.RecognitionObserver=o=>japaneseRows.Add(new {crop=o.RecognitionCrop,o.ValidWidth,o.InputWidth,o.TimeCount,pieces=o.Pieces,
                        paddingSupported=o.Pieces.All(piece=>piece.Start>=0 && piece.End>piece.Start && piece.End<=o.TimeCount && (long)piece.End*o.InputWidth<=(long)o.ValidWidth*o.TimeCount)});
                    await Task.Run(()=>japanese.ObserveAllRecognition(raster,token),token);
                    if(japanese.RecognizedBoxes.Count!=0 || japanese.NativeConfidences.Count!=0 || pixelHash!=Convert.ToHexStringLower(SHA256.HashData(raster.Bgra))) throw new InvalidOperationException("Shadow altered the original evidence.");
                    shadowPages.Add(new {page,raster.Width,raster.Height,bgraSha256=pixelHash,rows,japaneseRows});
                }
                finally {reader.RecognitionObserver=null;japanese.RecognitionObserver=null;CryptographicOperations.ZeroMemory(raster.Bgra);}
            }
        }
    }
    catch(Exception error) {shadowError=error.GetType().Name+":"+error.Message;}
    Console.WriteLine(JsonSerializer.Serialize(new { recipe = "ordered-row-e2e-v1", hash, stage, outcome, strictFailure, failure,
        readerComplete = capture.Complete, acquiredPages = capture.Pages.Count,
        acquiredGlyphs = capture.Pages.Sum(p => p.Layout?.Glyphs.Count ?? 0), builderCells = doc?.Cells.Count,
        originalOrderedProofs = doc?.Cells.Count(c => c.OrderedRowProof is not null), formal,
        nativeLowConfidenceSources=doc?.Sources.Count(s=>s.NativeConfidence is < .8),
        manualTargetCount=doc?.Capture?.OriginalCrops?.Count,
        nativeOcrCalls = (int?)null, llmCalls = 0, milliseconds = watch.ElapsedMilliseconds,
        cropFailure,
        rasterCaptures,
        shadow=new {pages=shadowPages,error=shadowError,nonAdoptable=true,formalQuality="UNASSESSED",llmCalls=0},
        inspectedOriginalImageSizes=originalImageSizes.Select(pair=>new {page=pair.Key,width=pair.Value.Width,height=pair.Value.Height}).ToArray(),
        nativeCallScope="Current production whole-page acquisition; native OCR calls are not instrumented and never reported as zero" }));
    if(Directory.Exists(ownedModelRoot))Directory.Delete(ownedModelRoot,true);
    CryptographicOperations.ZeroMemory(bytes);
}

static async Task<OnnxJapaneseOcr> OpenJapaneseReader(string owned,CancellationToken token)
{
    // Fixed model-only GET. PDF, image and OCR data never enter this request.
    const int size=10089078;
    const string sha="2e397c27461751befc82eca78c9b19e87322ebee34e7316468b21856f9977d64";
    var path=Path.Combine(owned,"research-japanese-rec.onnx");
    using(var http=new HttpClient{Timeout=TimeSpan.FromSeconds(90)})
    using(var input=await http.GetStreamAsync("https://huggingface.co/tobiichioriguchi/japan_PP-OCRv3_mobile_rec_onnx/resolve/1c772b3dd782e6ac4e61aba8bfe9683bee3334fc/inference.onnx",token))
    await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
    {
        var buffer=new byte[65536];var total=0;
        try {int count;while((count=await input.ReadAsync(buffer,token))!=0){total=checked(total+count);if(total>size)throw new InvalidDataException("Research model exceeded its pinned size.");await output.WriteAsync(buffer.AsMemory(0,count),token);}if(total!=size)throw new InvalidDataException("Research model size differs from its pin.");}
        finally {CryptographicOperations.ZeroMemory(buffer);}
    }
    await using(var file=File.OpenRead(path))if(Convert.ToHexStringLower(await SHA256.HashDataAsync(file,token))!=sha)throw new InvalidDataException("Research model digest differs from its pin.");
    var installed=await new WindowsRecoveryModels(owned).OcrStateAsync(token) ?? throw new InvalidDataException("Pinned detector is unavailable.");
    var dictionary=Path.Combine(AppContext.BaseDirectory,"japanese-v3-dictionary.json");
    if(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dictionary)))!="137be2712c276c2df9460d27994c6942caa2efc35574c1f9973fb98ff7ed23ac")throw new InvalidDataException("Research Japanese dictionary differs from its pin.");
    return OnnxJapaneseOcr.OpenJapaneseResearch(Path.Combine(installed.Path,"det.onnx"),path,dictionary);
}
