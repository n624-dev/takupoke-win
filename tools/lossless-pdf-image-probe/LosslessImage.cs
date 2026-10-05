using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;

namespace LosslessImageResearch;
public sealed class Unsupported(string reason) : Exception(reason);
public sealed record Acquisition(string PdfSha256, int Width, int Height, byte[] Rgb, string ContentSha256, string RawImageSha256, string[] TypedOperations);
public static class LosslessImage
{
    static void Require(bool ok, string reason) { if (!ok) throw new Unsupported(reason); }
    static IToken? Get(DictionaryToken d, string key) => d.Data.GetValueOrDefault(key);
    static T Resolve<T>(PdfDocument d, IToken? t) where T : class, IToken => DirectObjectFinder.TryGet<T>(t, d.Structure.TokenScanner, out var value) ? value : throw new Unsupported("Missing/wrong object type");
    static void Keys(DictionaryToken d, params string[] allowed) => Require(d.Data.Count <= allowed.Length && d.Data.Keys.All(k => allowed.Contains(k, StringComparer.Ordinal)), "Unsupported dictionary entry");
    static double Num(PdfDocument d, DictionaryToken dictionary, string key) => Resolve<NumericToken>(d, Get(dictionary, key)).Data;
    static string Name(PdfDocument d, DictionaryToken dictionary, string key) => Resolve<NameToken>(d, Get(dictionary, key)).Data;
    static bool FalseOrAbsent(PdfDocument d, DictionaryToken dictionary, string key) => Get(dictionary,key) is null || !Resolve<BooleanToken>(d, Get(dictionary,key)).Data;
    static string Sha(ReadOnlySpan<byte> b) => Convert.ToHexStringLower(SHA256.HashData(b));
    static byte[] Decode(StreamToken stream, int maximum, bool flateRequired, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dict = stream.StreamDictionary;
        var filter = Get(dict,"Filter");
        Require(filter is null && !flateRequired || filter is NameToken { Data: "FlateDecode" }, "Unsupported stream filter");
        Require(!dict.Data.ContainsKey("DecodeParms") && stream.Data.Length <= 50 * 1024 * 1024, "Unsupported decode parameters/encoded limit");
        if (filter is null) { Require(stream.Data.Length <= maximum,"Decoded limit"); return stream.Data.ToArray(); }
        var decoder = new ICSharpCode.SharpZipLib.Zip.Compression.Inflater(false);
        decoder.SetInput(stream.Data.ToArray());
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (!decoder.IsFinished)
        {
            token.ThrowIfCancellationRequested(); int amount;
            try { amount = decoder.Inflate(buffer,0,Math.Min(buffer.Length,maximum-(int)output.Length+1)); }
            catch (ICSharpCode.SharpZipLib.SharpZipBaseException) { throw new Unsupported("Invalid compressed data/checksum"); }
            Require(output.Length+amount <= maximum,"Decoded limit");
            if(amount>0)output.Write(buffer,0,amount);
            else Require(decoder.IsFinished,"Incomplete encoded stream/dictionary/no progress");
        }
        Require(decoder.RemainingInput==0,"Trailing encoded bytes/member");
        return output.ToArray();
    }
    static string[] FullContentTokens(byte[] bytes)
    {
        Require(bytes.Length is >0 and <=4096 && bytes.All(b => b is 9 or 10 or 12 or 13 or 32 || b is >=33 and <=126),"Content bytes unsupported");
        // Entire stream is inspected: no token is skipped as a comment, unknown
        // operator, inline-image payload or compatibility section.
        var words = System.Text.Encoding.ASCII.GetString(bytes).Split([' ','\t','\n','\r','\f'],StringSplitOptions.RemoveEmptyEntries);
        Require(words.Length==11 && words[0]=="q" && words[7]=="cm" && words[9]=="Do" && words[10]=="Q", "Other or incomplete page painting");
        Require(words[8].Length is >1 and <=65 && words[8][0]=='/' && words[8].Skip(1).All(c=>char.IsAsciiLetterOrDigit(c)),"Unsupported XObject name");
        return words;
    }
    static double Decimal(string text)
    {
        Require(text.Length is >0 and <=32 && text.All(c=>char.IsAsciiDigit(c)||c is '+' or '-' or '.'),"Unsupported matrix number");
        Require(double.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value),"Nonfinite matrix");return value;
    }
    public static Acquisition Extract(byte[] pdf, CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested(); Require(pdf.Length is >0 and <=50*1024*1024,"PDF byte limit");
        var rawGate=RawClassicPdf.Verify(pdf,token);
        using var doc=PdfDocument.Open(pdf,new ParsingOptions{UseLenientParsing=false,SkipMissingFonts=false,MaxStackDepth=64,UseActualText=false});
        Require(!doc.IsEncrypted && doc.NumberOfPages==1,"Only one unencrypted page supported");
        var catalog=doc.Structure.Catalog.CatalogDictionary; Keys(catalog,"Type","Pages");Require(Name(doc,catalog,"Type")=="Catalog","Catalog type");
        var tree=Resolve<DictionaryToken>(doc,Get(catalog,"Pages")); Keys(tree,"Type","Kids","Count");Require(Name(doc,tree,"Type")=="Pages"&&Num(doc,tree,"Count")==1,"Page tree");
        var kids=Resolve<ArrayToken>(doc,Get(tree,"Kids"));Require(kids.Length==1,"Page child count");var leaf=Resolve<DictionaryToken>(doc,kids[0]);
        Keys(leaf,"Type","Parent","MediaBox","CropBox","Rotate","Resources","Contents");Require(Name(doc,leaf,"Type")=="Page"&&ReferenceEquals(Resolve<DictionaryToken>(doc,Get(leaf,"Parent")),tree),"Leaf parent/type");
        Require(Get(leaf,"Rotate") is null||Num(doc,leaf,"Rotate")==0,"Page rotation");
        var media=Resolve<ArrayToken>(doc,Get(leaf,"MediaBox"));Require(media.Length==4,"MediaBox count");var v=media.Data.Select(t=>Resolve<NumericToken>(doc,t).Data).ToArray();
        Require(v.All(double.IsFinite)&&v[0]==0&&v[1]==0&&v[2] is >=1 and <=4096&&v[3] is >=1 and <=4096&&v[2]==Math.Truncate(v[2])&&v[3]==Math.Truncate(v[3]),"Page geometry/sample limit");
        if(Get(leaf,"CropBox") is {} crop){var a=Resolve<ArrayToken>(doc,crop);Require(a.Length==4&&a.Data.Select(t=>Resolve<NumericToken>(doc,t).Data).SequenceEqual(v),"Crop mismatch");}
        int width=(int)v[2],height=(int)v[3];token.ThrowIfCancellationRequested();
        var resources=Resolve<DictionaryToken>(doc,Get(leaf,"Resources"));Keys(resources,"XObject");var objects=Resolve<DictionaryToken>(doc,Get(resources,"XObject"));Require(objects.Data.Count==1,"XObject count");
        var content=Resolve<StreamToken>(doc,Get(leaf,"Contents"));Keys(content.StreamDictionary,"Length","Filter");rawGate.MatchStream(content.Data.Span);var rawContent=Decode(content,4096,false,token);var words=FullContentTokens(rawContent);
        var matrix=words.Skip(1).Take(6).Select(Decimal).ToArray();Require(matrix.SequenceEqual(new double[]{width,0,0,height,0,0}),"Image translation/rotation/reflection/skew/scale");
        var name=words[8][1..];var stream=Resolve<StreamToken>(doc,Get(objects,name));rawGate.MatchStream(stream.Data.Span);var imageDict=stream.StreamDictionary;
        Keys(imageDict,"Type","Subtype","Width","Height","ColorSpace","BitsPerComponent","Interpolate","Filter","Length");
        Require(Name(doc,imageDict,"Type")=="XObject"&&Name(doc,imageDict,"Subtype")=="Image"&&Name(doc,imageDict,"ColorSpace")=="DeviceRGB"&&Num(doc,imageDict,"BitsPerComponent")==8&&Num(doc,imageDict,"Width")==width&&Num(doc,imageDict,"Height")==height&&FalseOrAbsent(doc,imageDict,"Interpolate"),"Image sample/color/opacity contract");
        // Parse the actual page only after proving every raw content token.
        var page=doc.GetPage(1);token.ThrowIfCancellationRequested();
        Require(page.Operations.Count==4&&page.Operations[0] is Push&&page.Operations[1] is ModifyCurrentTransformationMatrix typedMatrix&&typedMatrix.Value.SequenceEqual(matrix)&&page.Operations[2] is InvokeNamedXObject typedImage&&typedImage.Name.Data==name&&page.Operations[3] is Pop,"Typed/raw operation mismatch");
        Require(page.Rotation.Value==0&&page.MediaBox.Bounds==page.CropBox.Bounds&&page.Letters.Count==0&&!page.GetAnnotations().Any()&&!page.Paths.Any(),"Additional page state/paint");
        var images=page.GetImages().Take(2).ToArray();Require(images.Length==1,"Painted image count");var image=images[0];
        Require(!image.IsInlineImage&&!image.IsImageMask&&image.MaskImage is null&&!image.Interpolate&&image.WidthInSamples==width&&image.HeightInSamples==height&&image.BitsPerComponent==8&&image.RawMemory.Span.SequenceEqual(stream.Data.Span),"Painted image mismatch/mask");
        var bounds=image.BoundingBox;Require(bounds.BottomLeft.X==0&&bounds.BottomLeft.Y==0&&bounds.BottomRight.X==width&&bounds.BottomRight.Y==0&&bounds.TopLeft.X==0&&bounds.TopLeft.Y==height&&bounds.TopRight.X==width&&bounds.TopRight.Y==height,"Oriented image corners do not cover page");
        var rgb=Decode(stream,checked(width*height*3),true,token);Require(rgb.Length==checked(width*height*3),"Incomplete/extra RGB samples");token.ThrowIfCancellationRequested();
        return new Acquisition(Sha(pdf),width,height,rgb,Sha(rawContent),Sha(stream.Data.Span),page.Operations.Select(o=>o.GetType().FullName!).ToArray());
    }
}
