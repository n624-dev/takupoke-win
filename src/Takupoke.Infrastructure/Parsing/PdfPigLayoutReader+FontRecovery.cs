using System.Text;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Takupoke.Infrastructure.Parsing;

public static partial class PdfPigLayoutReader
{
    // Separate entry point: callers must first try the unchanged Strict reader.
    internal static IReadOnlyList<PdfPageLayout> ReadWithMissingUnicodeRecovery(byte[] bytes, MaterialKind kind,
        CancellationToken token, RecoveryReadCapture capture)
        => ReadCore(bytes, kind, token, capture, new FontRecoveryContext());

    internal static void ImproveMissingUnicodeCapture(byte[] bytes, MaterialKind kind, RecoveryReadCapture capture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (capture.Complete || kind is not (MaterialKind.Timetable or MaterialKind.Exam or MaterialKind.ExamReturn)) return;
        var recovered=new RecoveryReadCapture();
        try { ReadWithMissingUnicodeRecovery(bytes,kind,token,recovered); }
        catch (PdfParseException failure) when (Takupoke.Core.Recovery.RecoveryPolicy.Eligible(kind,failure.Stage)) { }
        if (recovered.Pages.Count==0) return;
        if (capture.Pages.Count==0) capture.Begin(recovered.Pages.Count);
        if (capture.Pages.Count!=recovered.Pages.Count) throw new PdfParseException("P01");
        foreach(var page in recovered.Pages)
        {
            token.ThrowIfCancellationRequested();
            // Never replace a previously complete native page with a partial
            // alternate reading, or mark an incomplete alternate page complete.
            if (capture.Pages[page.Page-1].State!=RecoveryInputState.Complete && page.State==RecoveryInputState.Complete && page.Layout is not null)
                capture.Record(page.Page,page.State,page.Layout);
        }
        if (recovered.ReaderCompleted) capture.Finish();
    }

    private sealed class FontRecoveryContext
    {
        internal readonly Dictionary<string, PdfTrueTypeRecoveryMap> Maps = [];
        internal readonly Dictionary<string,IReadOnlySet<int>> SimpleGlyphs = [];
        internal int FontBytes;
    }

    private static (PdfUnicodeMap, Func<int, RecoveryFontEvidence>) MissingUnicodeMap(PdfDocument document,
        DictionaryToken font, string resource, FontRecoveryContext context, CancellationToken token)
    {
        if (resource.Length is < 1 or > 128 || resource.Any(char.IsControl)
            || Name(document, font, "Subtype") != "Type0" || Name(document, font, "Encoding") != "Identity-H")
            throw new PdfParseException("P01");
        var descendants = Resolve<ArrayToken>(document, Get(font, "DescendantFonts"));
        if (descendants.Length != 1) throw new PdfParseException("P01");
        var metrics = Resolve<DictionaryToken>(document, descendants[0]);
        if (Name(document, metrics, "Subtype") != "CIDFontType2") throw new PdfParseException("P01");
        var descriptor = Resolve<DictionaryToken>(document, Get(metrics, "FontDescriptor"));
        if (descriptor.Data.ContainsKey("FontFile") || descriptor.Data.ContainsKey("FontFile3")) throw new PdfParseException("P01");
        var data = DecodeRecoveryStream(document, Resolve<StreamToken>(document, Get(descriptor, "FontFile2")), PdfTrueTypeRecoveryMap.MaximumFontBytes, token);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data));
        if (!context.Maps.TryGetValue(hash, out var fontMap))
        {
            if (data.Length > PdfTrueTypeRecoveryMap.MaximumFontBytes - context.FontBytes) throw new PdfParseException("limit");
            context.FontBytes += data.Length;
            fontMap = PdfTrueTypeRecoveryMap.Read(data, token); context.Maps.Add(hash, fontMap);
            context.SimpleGlyphs.Add(hash,PdfTrueTypeRecoveryMap.SimpleOutlineGlyphs(data,fontMap.GlyphCount,token));
        }
        byte[]? cidMap = null; var cidHash = "identity-default";
        if (metrics.Data.ContainsKey("CIDToGIDMap"))
        {
            var value = Get(metrics, "CIDToGIDMap");
            if (DirectObjectFinder.TryGet<NameToken>(value, document.Structure.TokenScanner, out var identity))
            {
                if (identity.Data != "Identity") throw new PdfParseException("P01");
                cidHash = "identity";
            }
            else
            {
                cidMap = DecodeRecoveryStream(document, Resolve<StreamToken>(document, value), 131072, token);
                if (cidMap.Length % 2 != 0) throw new PdfParseException("P01");
                cidHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(cidMap));
            }
        }
        var values = new Dictionary<int, string>(); var glyphs = new Dictionary<int, int>();
        var codeCount = cidMap is null ? fontMap.GlyphCount : cidMap.Length / 2;
        for (var code = 0; code < codeCount; code++)
        {
            token.ThrowIfCancellationRequested();
            var gid = cidMap is null ? code : cidMap[code * 2] * 256 + cidMap[code * 2 + 1];
            // An unresolved/ambiguous code stays absent: Show must refuse its
            // actual use. Do not discard the drawn glyph or invent whitespace.
            if (gid > 0 && gid < fontMap.GlyphCount && fontMap.UniqueScalars.TryGetValue(gid, out var scalar))
            { values.Add(code, char.ConvertFromUtf32(scalar)); glyphs.Add(code, gid); }
        }
        RecoveryFontEvidence Evidence(int code)
        {
            if (!glyphs.TryGetValue(code, out var gid) || !fontMap.UniqueScalars.TryGetValue(gid, out var scalar)) throw new PdfParseException("P01");
            if (!Rune.IsWhiteSpace(new Rune(scalar)) && !context.SimpleGlyphs[hash].Contains(gid)) throw new PdfParseException("P01");
            return new(resource, hash, cidHash, code, code, gid, scalar);
        }
        return (new PdfUnicodeMap(2, values), Evidence);
    }

    private static byte[] DecodeRecoveryStream(PdfDocument document, StreamToken stream, int limit, CancellationToken token)
    {
        var data = stream.Data;
        var filters = document.Structure.FilterProvider.GetFilters(stream.StreamDictionary, document.Structure.TokenScanner);
        if (filters.Count > 8 || data.Length > limit) throw new PdfParseException("limit");
        if (stream.StreamDictionary.Data.ContainsKey("UseCMap")) throw new PdfParseException("P01");
        for (var index = 0; index < filters.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (!filters[index].IsSupported) throw new PdfParseException("P01");
            data = filters[index].Decode(data, stream.StreamDictionary, document.Structure.FilterProvider, index);
            if (data.Length > limit) throw new PdfParseException("limit");
        }
        token.ThrowIfCancellationRequested(); return data.ToArray();
    }
}
