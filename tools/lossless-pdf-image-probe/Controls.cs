using System.Text;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
namespace LosslessImageResearch;
public static class Controls
{
    static byte[] Ascii(string text)=>Encoding.ASCII.GetBytes(text);
    static byte[] Flate(byte[] data,CompressionLevel level=CompressionLevel.SmallestSize){using var result=new MemoryStream();using(var z=new ZLibStream(result,level,true))z.Write(data);return result.ToArray();}
    static byte[] Stream(byte[] data,string entries="")=>[..Ascii($"<< /Length {data.Length} {entries} >>\nstream\n"),..data,..Ascii("\nendstream")];
    public static byte[] Pdf(string? content=null,string extraPage="",string extraImage="",string extraCatalog="",bool multipage=false,bool contentsArray=false,bool compressedContent=false,byte[]? rgb=null,string box="0 0 3 2",string imageShape="/Width 3 /Height 2",string filter="/Filter /FlateDecode",bool appendContent=false,int rotation=0,string colorSpace="DeviceRGB",byte[]? encodedImage=null,string? infoLiteral=null)
    {
        rgb??=[255,0,0,0,255,0,0,0,255,255,255,0,0,255,255,255,0,255];
        var data=encodedImage??Flate(rgb);var text=Ascii(content??"q\n3 0 0 2 0 0 cm\n/Im0 Do\nQ\n");
        List<byte[]> objects=[Ascii("<< /Type /Catalog /Pages 2 0 R "+extraCatalog+" >>"),Ascii(multipage?"<< /Type /Pages /Count 2 /Kids [3 0 R 6 0 R] >>":"<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [{box}] /CropBox [{box}] /Rotate {rotation} /Resources << /XObject << /Im0 4 0 R >> >> /Contents {(contentsArray?"[5 0 R]":"5 0 R")} {extraPage} >>"),Stream(data,$"/Type /XObject /Subtype /Image {imageShape} /ColorSpace /{colorSpace} /BitsPerComponent 8 {filter} {extraImage}"),Stream(compressedContent?Flate(text):text,compressedContent?"/Filter /FlateDecode":"")];
        if(multipage)objects.Add(Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 3 2] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>"));
        if(appendContent){objects[2]=Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 3 2] /Resources << /XObject << /Im0 4 0 R >> >> /Contents [5 0 R 6 0 R] >>");objects.Add(Stream(Ascii("0 0 1 1 re f")));}
        int? infoId=null;if(infoLiteral is not null){infoId=objects.Count+1;objects.Add(Ascii("<< /Note ("+infoLiteral+") >>"));}
        using var pdf=new MemoryStream();pdf.Write(Ascii("%PDF-1.7\n"));List<long> offsets=[0];foreach(var o in objects){offsets.Add(pdf.Position);pdf.Write(Ascii($"{offsets.Count-1} 0 obj\n"));pdf.Write(o);pdf.Write(Ascii("\nendobj\n"));}
        var xref=pdf.Position;pdf.Write(Ascii($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n"));foreach(var offset in offsets.Skip(1))pdf.Write(Ascii(offset.ToString("D10",CultureInfo.InvariantCulture)+" 00000 n \n"));pdf.Write(Ascii($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R {(infoId is null?"":$"/Info {infoId} 0 R")} >>\nstartxref\n{xref}\n%%EOF\n"));return pdf.ToArray();
    }
    public static object Run(string? knownPath=null)
    {
        var results=new List<object>();int passed=0;
        void Accept(string id,byte[] pdf,byte[]? expected=null){var a=LosslessImage.Extract(pdf);if(expected is not null&&!a.Rgb.SequenceEqual(expected))throw new Exception("Independent original RGB mismatch");passed++;results.Add(new{id,accepted=true,originalSamplesUnchanged=true,width=a.Width,height=a.Height,rgbSha=Convert.ToHexStringLower(SHA256.HashData(a.Rgb))});}
        void Reject(string id,byte[] pdf){try{LosslessImage.Extract(pdf);throw new Exception("Unsafe input accepted: "+id);}catch(Unsupported e){passed++;results.Add(new{id,accepted=false,reason=e.Message});}}
        byte[] colors=[255,0,0,0,255,0,0,0,255,255,255,0,0,255,255,255,0,255];
        Accept("multicolor_original_rows_orientation",Pdf(),colors);Accept("bounded_compressed_content",Pdf(compressedContent:true),colors);
        byte[] markerRgb=Ascii("<< /A 0 /A 1 >>   ");Accept("encoded_binary_not_scanned_as_dictionary",Pdf(rgb:markerRgb,encodedImage:Flate(markerRgb,CompressionLevel.NoCompression)),markerRgb);
        Accept("literal_info_tokens_not_dictionary_keys",Pdf(infoLiteral:"/Width 3 /Width 7 \\(escaped\\)"),colors);
        Reject("overlay_after_image",Pdf(content:"q 3 0 0 2 0 0 cm /Im0 Do Q 0 0 1 1 re f"));
        Reject("overlay_before_image",Pdf(content:"0 0 1 1 re f q 3 0 0 2 0 0 cm /Im0 Do Q"));
        Reject("clip",Pdf(content:"q 0 0 2 1 re W n 3 0 0 2 0 0 cm /Im0 Do Q"));
        Reject("matrix_rotation",Pdf(content:"q 0 3 -2 0 2 0 cm /Im0 Do Q"));
        Reject("reflection",Pdf(content:"q -3 0 0 2 3 0 cm /Im0 Do Q"));
        Reject("skew",Pdf(content:"q 3 1 0 2 0 0 cm /Im0 Do Q"));
        Reject("translation",Pdf(content:"q 3 0 0 2 1 0 cm /Im0 Do Q"));
        Reject("partial_coverage",Pdf(content:"q 2 0 0 2 0 0 cm /Im0 Do Q"));
        Reject("page_rotation",Pdf(rotation:90));
        Reject("softmask",Pdf(extraImage:"/SMask 4 0 R"));Reject("colorkeymask",Pdf(extraImage:"/Mask [0 1 0 1 0 1]"));Reject("imagemask",Pdf(extraImage:"/ImageMask true"));
        Reject("page_transparency_group",Pdf(extraPage:"/Group << /S /Transparency >>"));
        Reject("annotations",Pdf(extraPage:"/Annots [<< /Type /Annot /Subtype /Square /Rect [0 0 1 1] >>]"));
        Reject("annotation_empty_still_unsupported",Pdf(extraPage:"/Annots []"));Reject("catalog_optional_content",Pdf(extraCatalog:"/OCProperties <<>>"));
        Reject("graphics_transparency_state",Pdf(content:"q /T gs 3 0 0 2 0 0 cm /Im0 Do Q"));
        Reject("unknown_operator_skipped_by_some_parsers",Pdf(content:"q BX unknown EX 3 0 0 2 0 0 cm /Im0 Do Q"));
        Reject("unknown_trailing_token",Pdf(content:"q 3 0 0 2 0 0 cm /Im0 Do Q unknown"));
        Reject("comment_not_silently_skipped",Pdf(content:"q % unknown\n3 0 0 2 0 0 cm /Im0 Do Q"));
        Reject("extra_content_stream_paint",Pdf(appendContent:true));Reject("contents_array_unsupported",Pdf(contentsArray:true));Reject("multipage",Pdf(multipage:true));
        Reject("nondefault_userunit",Pdf(extraPage:"/UserUnit 2"));Reject("interpolation",Pdf(extraImage:"/Interpolate true"));
        Reject("decode_changes_samples",Pdf(extraImage:"/Decode [1 0 1 0 1 0]"));Reject("predictor_parameters",Pdf(extraImage:"/DecodeParms << /Predictor 12 >>"));
        Reject("wrong_image_shape",Pdf(imageShape:"/Width 2 /Height 2"));Reject("sample_limit",Pdf(imageShape:"/Width 4097 /Height 2"));
        Reject("extra_decoded_samples",Pdf(rgb:[..colors,0]));Reject("missing_decoded_samples",Pdf(rgb:colors[..^1]));
        Reject("decoded_content_capacity",Pdf(content:new string(' ',4096)+"q 3 0 0 2 0 0 cm /Im0 Do Q",compressedContent:true));
        Reject("matrix_nan",Pdf(content:"q NaN 0 0 2 0 0 cm /Im0 Do Q"));Reject("unbalanced_state",Pdf(content:"q 3 0 0 2 0 0 cm /Im0 Do"));
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{LosslessImage.Extract(Pdf(),cancelled.Token);throw new Exception("Cancel ignored");}catch(OperationCanceledException){passed++;results.Add(new{id="cancellation_before_parse",accepted=false,reason="OperationCanceledException"});}}
        var encoded=Flate(colors);
        Reject("truncated_adler4",Pdf(encodedImage:encoded[..^4]));Reject("truncated_lastbyte",Pdf(encodedImage:encoded[..^1]));
        Reject("trailing_ABCD",Pdf(encodedImage:[..encoded,65,66,67,68]));Reject("second_zlib_member",Pdf(encodedImage:[..encoded,..encoded]));
        var corrupt=(byte[])encoded.Clone();corrupt[^1]^=1;Reject("corrupt_adler",Pdf(encodedImage:corrupt));
        Reject("same_duplicate_colorspace",Pdf(extraImage:"/ColorSpace /DeviceRGB"));Reject("same_duplicate_filter",Pdf(extraImage:"/Filter /FlateDecode"));Reject("same_duplicate_width",Pdf(extraImage:"/Width 3"));
        Reject("conflicting_duplicate_colorspace_last_RGB",Pdf(colorSpace:"DeviceGray",extraImage:"/ColorSpace /DeviceRGB"));
        Reject("escaped_duplicate_colorspace",Pdf(extraImage:"/#43olorSpace /DeviceRGB"));Reject("escaped_duplicate_width",Pdf(extraImage:"/Wid#74h 3"));
        Reject("duplicate_interpolate",Pdf(extraImage:"/Interpolate false /Interpolate false"));
        Reject("duplicate_catalog_type",Pdf(extraCatalog:"/Type /Catalog"));Reject("duplicate_leaf_mediabox",Pdf(extraPage:"/MediaBox [0 0 3 2]"));
        Reject("trailing_unknown_pdf_bytes",[..Pdf(),..Ascii("garbage")]);Reject("incremental_trailing_startxref",[..Pdf(),..Ascii("startxref 0\n%%EOF\n")]);
        var badXref=Pdf();var marker=Ascii("startxref\n");int pos=badXref.AsSpan().IndexOf(marker)+marker.Length;badXref[pos]=(byte)'0';Reject("wrong_startxref",badXref);
        var badLength=Pdf();var lengthMarker=Ascii("/Length "+encoded.Length);int lengthPos=badLength.AsSpan().IndexOf(lengthMarker)+8;badLength[lengthPos]=(byte)'0';Reject("wrong_raw_stream_length",badLength);
        if(knownPath is not null)
        {
        var known=LosslessImage.Extract(File.ReadAllBytes(knownPath));var expectedPdf="7ceb34d191dc48a5d6bc072e75898172cd20f356f6c9c312f558635d6a454323";var expectedRgb="62f80f469cd0d36cb98ddc8ecebee71c94a278c2ee3c7e22336688f844e38fd9";
        if(known.PdfSha256!=expectedPdf||known.Width!=3740||known.Height!=800||known.Rgb.Length!=8976000||Convert.ToHexStringLower(SHA256.HashData(known.Rgb))!=expectedRgb)throw new Exception("Known independent identity assertion failed after extraction");passed++;results.Add(new{id="known_consumed_pdf_independent_postreturn_identity",accepted=true,known.PdfSha256,rgbSha=expectedRgb});
        }
        return new{scope="Source-only acquisition prototype controls. Inputs generated in memory; expected RGB/hash asserted after Extract only. No production hook/OCR/formal/model quality.",passed,failed=0,nativeOCRCalls=0,runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,results};
    }
}
