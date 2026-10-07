using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AcquisitionResearch;
using Microsoft.ML.OnnxRuntime;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

if(args.Length==1&&args[0]=="--controls"){RoiGeometry.Controls();return;}
if(args.Length!=4||args[0]!="--one-fixed-page"||Environment.GetEnvironmentVariable("TAKUPOKE_ROI_GO")!="ONE_FIXED_WINDOWS_PDF_MAX3_DETECTOR_CALLS")throw new InvalidDataException("Root-owned exact one packet authorization missing");
string Sha(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
object NativeBox(Rect r)=>new{r.X,r.Y,r.Width,r.Height};
void Emit(object value){var b=JsonSerializer.SerializeToUtf8Bytes(value);if(b.Length>4*1024*1024)throw new InvalidDataException("Individual JSON exceeds parent output cap");Console.WriteLine(System.Text.Encoding.UTF8.GetString(b));Console.Out.Flush();}
using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(120));var token=cancellation.Token;
var pdfBytes=File.ReadAllBytes(args[1]);Check.That(pdfBytes.Length<=2_000_000&&Sha(pdfBytes)==args[3],"fixed fictional PDF identity");
var model=File.ReadAllBytes(args[2]);Check.That(model.Length==4826518&&Sha(model)=="a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d","existing detector identity");CryptographicOperations.ZeroMemory(model);
using var input=new InMemoryRandomAccessStream();using(var writer=new DataWriter(input)){writer.WriteBytes(pdfBytes);await writer.StoreAsync().AsTask(token);writer.DetachStream();}input.Seek(0);
var pdf=await PdfDocument.LoadFromStreamAsync(input).AsTask(token);Check.That(pdf.PageCount==5,"same existing five-page fictional PDF");using var page=pdf.GetPage(0);Check.That(page.Rotation==PdfPageRotation.Normal,"rotated coordinate system unsupported");
async Task<RecoveryRaster> Render(int w,int h,RecoveryBox? nativeRect)
{
 Check.That(w>0&&w<=2400&&h>0&&h<=3200,"render pixel bound");using var image=new InMemoryRandomAccessStream();var options=new PdfPageRenderOptions{DestinationWidth=(uint)w,DestinationHeight=(uint)h};if(nativeRect is not null)options.SourceRect=new Rect(nativeRect.X,nativeRect.Y,nativeRect.Width,nativeRect.Height);
 await page.RenderToStreamAsync(image,options).AsTask(token);Check.That(image.Size<=32*1024*1024,"native encoded image memory bound");image.Seek(0);var decoder=await BitmapDecoder.CreateAsync(image).AsTask(token);using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore).AsTask(token);
 Check.That(bitmap.PixelWidth==w&&bitmap.PixelHeight==h,"native actual output dimensions differ from requested; coordinate mapping refused");var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage).AsTask(token);var raster=new RecoveryRaster(bitmap.PixelWidth,bitmap.PixelHeight,pixels.DetachPixelData());Check.That(raster.Valid,"actual native BGRA dimensions");return raster;
}
var shape=RoiGeometry.ProductShape(page.Size.Width,page.Size.Height);var whole=await Render(shape.W,shape.H,null);RecoveryRaster? local=null;
try
{
 long totalPixelWork=0;RecoveryRaster.PixelWork.GlobalWorkObserver=amount=>{totalPixelWork=checked(totalPixelWork+amount);if(totalPixelWork>256_000_000)throw RecoveryWorkLimits.Exceeded("ROI whole transaction pixel work limit");};var rules=whole.Rules(token);var mask=whole.RuleMask(rules,token);long selectionPixelWork=0;var selected=RoiGeometry.Select(rules,whole.Width,whole.Height,b=>RoiGeometry.ObservedNonRuleInk(whole,mask,b,ref selectionPixelWork,token));var roi=selected.ROI;var nr=RoiGeometry.NativeRect(roi,whole.Width,whole.Height,page.Size.Width,page.Size.Height);double density=960/Math.Max(roi.Width,roi.Height);int lw=Math.Max(32,(int)Math.Round(roi.Width*density)),lh=Math.Max(32,(int)Math.Round(roi.Height*density));Check.That(lw<=960&&lh<=960,"local render cap");
 Emit(new{type="render",osDescription=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),runtimeVersion=Environment.Version.ToString(),sourceCommit=Environment.GetEnvironmentVariable("GITHUB_SHA"),pdfSHA256=Sha(pdfBytes),pageNumber=1,totalPDFPages=pdf.PageCount,originalPDFSize=new{page.Size.Width,page.Size.Height},nativePageDimensions=new{media=NativeBox(page.Dimensions.MediaBox),crop=NativeBox(page.Dimensions.CropBox),art=NativeBox(page.Dimensions.ArtBox),bleed=NativeBox(page.Dimensions.BleedBox),trim=NativeBox(page.Dimensions.TrimBox)},rotation=page.Rotation.ToString(),wholeRaster=new{whole.Width,whole.Height,bytes=whole.Bgra.Length,sha256=Sha(whole.Bgra)},selectedObservedClosedCell=roi,selected.Cells,selected.Work,selected.NonblankCells,selected.SelectedNonRuleInkPixels,selectionPixelWork,maximumSelectionPixelWork=8_000_000,sourceRectNativeWinRT=nr,requestedLocalRaster=new{width=lw,height=lh},originalRules=rules,roiSelection="SHA256 fixed geometry among closed cells with measured production non-rule nonwhite pixels; zero text/oracle/drawing/model input; one selected cell and no alternate retry",mapping="native SourceRect = selected actual whole raster XYWH * actual PdfPage.Size per axis; local boxes back by actual local BGRA width/height",wholepageQuality="UNASSESSED",physicalRoleProof="ABSENT",legacy4060ComparisonPooled=false});
 // Native local raster is rendered from the same original PDF, never resized from the whole BGRA.
 local=await Render(lw,lh,nr);Emit(new{type="local-render",local.Width,local.Height,bytes=local.Bgra.Length,sha256=Sha(local.Bgra),sourceRectNativeWinRT=nr,expectedCoordinateOwner="selected closed-rail region; no semantic role certificate",sameOriginalPDF=true});
 using var options=new SessionOptions{IntraOpNumThreads=2,InterOpNumThreads=1,GraphOptimizationLevel=GraphOptimizationLevel.ORT_ENABLE_ALL,LogSeverityLevel=OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR};using var session=new InferenceSession(args[2],options);
 var modules=Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(m=>Path.GetFileName(m.FileName).Equals("onnxruntime.dll",StringComparison.OrdinalIgnoreCase)).Select(m=>new{name=Path.GetFileName(m.FileName),m.FileVersionInfo.FileVersion,sha256=Sha(File.ReadAllBytes(m.FileName))}).ToArray();Check.That(modules.Length==1&&modules[0].sha256=="dec964ab1ee36cc9b0ae247d13b376627992fc57dec0454354017ab8fd84f1ea"&&OrtEnv.Instance().GetVersionString()=="1.23.2"&&Sha(File.ReadAllBytes(typeof(InferenceSession).Assembly.Location))=="4c6985df4aa2f810ba8a2b37c3c791e619d8fe126954a6bd28cffd3f4adeba95","frozen WindowsNuGet CPU runtime mismatch");Emit(new{type="runtime",nativeVersion=OrtEnv.Instance().GetVersionString(),managedVersion=typeof(InferenceSession).Assembly.GetName().Version?.ToString(),managedSHA256=Sha(File.ReadAllBytes(typeof(InferenceSession).Assembly.Location)),modules,backend="WindowsNuGetCPU",intra=2,inter=1,optimization="ORT_ENABLE_ALL",modelSessions=1,recognizerSessions=0,maximumDetectorCalls=3});
 int calls=0;
 object Coverage(IReadOnlyList<RecoveryBox> boxes)
 {
  var covered=new bool[whole.Width*whole.Height];long steps=0;
  foreach(var b in boxes){Check.That(new RecoveryBox(0,0,whole.Width,whole.Height).Contains(b),"original coordinate candidate outside whole");int l=Math.Max(0,(int)Math.Ceiling(b.X-.5)),r=Math.Min(whole.Width-1,(int)Math.Floor(b.X+b.Width-.5)),t=Math.Max(0,(int)Math.Ceiling(b.Y-.5)),bottom=Math.Min(whole.Height-1,(int)Math.Floor(b.Y+b.Height-.5));for(int y=t;y<=bottom;y++){token.ThrowIfCancellationRequested();Check.That((steps+=Math.Max(0,r-l+1))<=64_000_000,"spatial coverage work budget");for(int x=l;x<=r;x++)covered[y*whole.Width+x]=true;}}
  int original=0,seen=0;for(int y=(int)Math.Ceiling(roi.Y-.5);y<=Math.Floor(roi.Y+roi.Height-.5);y++){token.ThrowIfCancellationRequested();for(int x=(int)Math.Ceiling(roi.X-.5);x<=Math.Floor(roi.X+roi.Width-.5);x++){int i=y*whole.Width+x,p=i*4;if(!mask[i]&&(whole.Bgra[p]!=255||whole.Bgra[p+1]!=255||whole.Bgra[p+2]!=255)){original++;if(covered[i])seen++;}}}
  return new{selectedOriginalWholeRasterNonRuleInkPixels=original,spatiallyCoveredPixels=seen,spatiallyUncoveredPixels=original-seen,meaning="Common-coordinate spatial support only; different raster glyph/component identities are not bridged; no recognition/role/empty proof"};
 }
 object SourceClosure(RecoveryRaster raster,IReadOnlyList<RecoveryBox> boxes)
 {
  try{var complete=OcrCropCompleteness.Complete(raster,boxes,token);bool missing=raster.HasUnrecognizedInk(complete,raster.Rules(token),token);return new{complete=!missing,refusal=missing?"unchanged production unrecognized ink":null,scope="only this actual arm raster; complete here does not qualify PDF/roles"};}
  catch(InvalidDataException e)when(!RecoveryWorkLimits.IsExceeded(e)){return new{complete=false,refusal=e.Message,scope="only this actual arm raster; no suppression of resource failures"};}
 }
 foreach(string arm in new[]{"whole-product-render","roi-existing-product-raster","roi-original-pdf-native-render"})
 {
  token.ThrowIfCancellationRequested();RecoveryRaster raster=arm=="roi-original-pdf-native-render"?local:whole;RecoveryBox crop=arm=="roi-existing-product-raster"?roi:new(0,0,raster.Width,raster.Height);var (dw,dh)=Geometry.Shape(crop.Width,crop.Height);var tensor=OcrInputTransform.Detection(raster,crop,dw,dh,token);var inputHash=Sha(MemoryMarshal.AsBytes(tensor.ToArray().AsSpan()).ToArray());Check.That(++calls<=3,"call cap");Emit(new{type="detector-start",calls,arm,inputSHA256=inputHash,inputShape=tensor.Dimensions.ToArray(),crop,rasterSHA256=Sha(raster.Bgra)});
  using var result=session.Run([NamedOnnxValue.CreateFromTensor("x",tensor)]);var output=result.First().AsTensor<float>();OcrDetectorProbability.ValidateMapShape(output.Dimensions.ToArray(),dw,dh);var raw=output.ToArray();var rawHash=Sha(MemoryMarshal.AsBytes(raw.AsSpan()).ToArray());int finite=0,below=0,above=0,normalized=0;var invalid=new List<object>();float min=float.PositiveInfinity,max=float.NegativeInfinity;for(int i=0;i<raw.Length;i++){if(float.IsFinite(raw[i])){finite++;min=Math.Min(min,raw[i]);max=Math.Max(max,raw[i]);}if(raw[i]<0)below++;if(raw[i]>1)above++;if(!float.IsFinite(raw[i])||raw[i]<0||raw[i]>1){if(invalid.Count<16)invalid.Add(new{index=i,bits=BitConverter.SingleToInt32Bits(raw[i]).ToString("x8")});}if(raw[i]>1&&raw[i]<=float.BitIncrement(1f))normalized++;}
  Emit(new{type="detector-map",calls,arm,outputSHA256=rawHash,shape=output.Dimensions.ToArray(),elements=raw.Length,finite,nonfinite=raw.Length-finite,below,above,normalized,minimumFiniteBits=BitConverter.SingleToInt32Bits(min).ToString("x8"),maximumFiniteBits=BitConverter.SingleToInt32Bits(max).ToString("x8"),firstOutsideUnitInterval=invalid,rawMapRetained=false});
  var view=new View(0,crop,crop,dw,dh);var candidates=Geometry.Decode(view,raw,raster.Width,raster.Height,token);var sourceBoxes=candidates.Select(c=>c.Box).ToArray();RecoveryBox[] mapped=arm=="roi-original-pdf-native-render"?sourceBoxes.Select(b=>RoiGeometry.MapBack(b,roi,local.Width,local.Height)).ToArray():sourceBoxes;bool rawPreserved=Sha(MemoryMarshal.AsBytes(raw.AsSpan()).ToArray())==rawHash;Check.That(rawPreserved,"raw native probability mutation");
  // A fractional crop of the product raster cannot establish full-raster completeness;
  // its untouched context is deliberately left unassessed. Native ROI is its own raster.
  object closure=arm=="roi-existing-product-raster"?new{complete=false,refusal="partial full-raster region; full-page acquisition deliberately UNASSESSED"}:SourceClosure(raster,sourceBoxes);
  Emit(new{type="detector",calls,arm,inputSHA256=inputHash,outputSHA256=rawHash,rawPreserved,candidateCount=mapped.Length,boundaryTouchingCandidates=candidates.Count(c=>c.InternalEdge),sourceBoxes,mappedWholeRasterBoxes=mapped,spatialCoverage=Coverage(mapped),unchangedSourceClosure=closure,scope="Detector acquisition diagnostics only; original source boxes retained. Pixel coverage is not physical-cell/semantic/fulltext qualification"});
 }
 Emit(new{type="result",actualDetectorCalls=calls,totalProductionPixelWork=totalPixelWork,maximumProductionPixelWork=256_000_000,recognizerCalls=0,modelSessions=1,comparisonScope="one fixed original fictional PDF/page; max3 Windows native detector conditions; old4060Linux/V2 never pooled",wholePDFQuality="UNASSESSED",qualifiedModels=Array.Empty<string>(),retry=false});
}
finally{CryptographicOperations.ZeroMemory(whole.Bgra);if(local is not null)CryptographicOperations.ZeroMemory(local.Bgra);CryptographicOperations.ZeroMemory(pdfBytes);}
