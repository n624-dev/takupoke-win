using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Takupoke.Infrastructure.Recovery;

/// An owned PNG with the original RGB samples and original ICC profile.
/// This narrow lossless route never supplies an OCR value or cell assignment.
public sealed record RecoveryPdfOriginalImage(int Width, int Height, byte[] Png) : IDisposable
{
    public void Dispose() => CryptographicOperations.ZeroMemory(Png);

    public static RecoveryPdfOriginalImage? TryRead(byte[] pdf, int number, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!RecoveryPdfImageResolution.Inspect(pdf, token).TryGetValue(number, out var size)) return null;
        byte[]? rgb = null, profile = null;
        try
        {
            using var document = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false, MaxStackDepth = 64 });
            var image = document.GetPage(number).GetImages().Single(); var dictionary = image.ImageDictionary;
            // Exact positive RGB samples only. Alpha, range remapping, image
            // masks and alternate bit depths need the complete PDF renderer.
            if (image.BitsPerComponent != 8 || image.IsImageMask || image.MaskImage is not null ||
                new[] { "Mask", "SMask", "SMaskInData", "OC", "Decode", "Intent" }.Any(dictionary.Data.ContainsKey)) return null;
            if (dictionary.Data.TryGetValue("DecodeParms", out var parameters) && parameters is not NullToken &&
                (!DirectObjectFinder.TryGet<DictionaryToken>(parameters, document.Structure.TokenScanner, out var decoding) || decoding.Data.Count != 0)) return null;
            if (!dictionary.Data.TryGetValue("Filter", out var filter) || !DirectObjectFinder.TryGet<NameToken>(filter, document.Structure.TokenScanner, out var filterName) || filterName.Data != "FlateDecode") return null;
            if (!dictionary.Data.TryGetValue("ColorSpace", out var color)) return null;
            if (DirectObjectFinder.TryGet<NameToken>(color, document.Structure.TokenScanner, out var colorName))
            {
                if (colorName.Data != "DeviceRGB") return null;
            }
            else
            {
                if (!DirectObjectFinder.TryGet<ArrayToken>(color, document.Structure.TokenScanner, out var colors) || colors.Length != 2 ||
                    !DirectObjectFinder.TryGet<NameToken>(colors[0], document.Structure.TokenScanner, out var kind) || kind.Data != "ICCBased" ||
                    !DirectObjectFinder.TryGet<StreamToken>(colors[1], document.Structure.TokenScanner, out var icc)) return null;
                var info = icc.StreamDictionary;
                if (!info.Data.TryGetValue("N", out var components) || !DirectObjectFinder.TryGet<NumericToken>(components, document.Structure.TokenScanner, out var count) || count.Data != 3 ||
                    new[] { "Range", "DecodeParms" }.Any(info.Data.ContainsKey)) return null;
                if (info.Data.TryGetValue("Alternate", out var alternate) &&
                    (!DirectObjectFinder.TryGet<NameToken>(alternate, document.Structure.TokenScanner, out var alternateName) || alternateName.Data != "DeviceRGB")) return null;
                if (info.Data.TryGetValue("Filter", out var profileFilter))
                {
                    if (!DirectObjectFinder.TryGet<NameToken>(profileFilter, document.Structure.TokenScanner, out var profileFilterName) || profileFilterName.Data != "FlateDecode") return null;
                    profile = Inflate(icc.Data.ToArray(), 1_048_576, exact: false, token);
                }
                else if (icc.Data.Length <= 1_048_576) profile = icc.Data.ToArray();
                if (profile is null || profile.Length < 128 || BinaryPrimitives.ReadUInt32BigEndian(profile) != profile.Length ||
                    !profile.AsSpan(16, 4).SequenceEqual("RGB "u8) || !profile.AsSpan(36, 4).SequenceEqual("acsp"u8)) return null;
            }
            rgb = Inflate(image.RawMemory.ToArray(), checked(size.Width * size.Height * 3), exact: true, token);
            if (rgb is null) return null;
            var png = Encode(size.Width, size.Height, rgb, profile, token);
            try { token.ThrowIfCancellationRequested(); return new(size.Width, size.Height, png); }
            catch { CryptographicOperations.ZeroMemory(png); throw; }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException) { return null; }
        finally
        {
            if (rgb is not null) CryptographicOperations.ZeroMemory(rgb);
            if (profile is not null) CryptographicOperations.ZeroMemory(profile);
        }
    }

    private static byte[]? Inflate(byte[] packed, int maximum, bool exact, CancellationToken token)
    {
        try
        {
            using var input = new MemoryStream(packed, false); using var zip = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(); var buffer = new byte[65_536];
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested(); var count = zip.Read(buffer, 0, Math.Min(buffer.Length, maximum - (int)output.Length + 1));
                    if (count == 0) break;
                    if (output.Length + count > maximum) return null;
                    output.Write(buffer, 0, count);
                }
                return !exact || output.Length == maximum ? output.ToArray() : null;
            }
            finally { CryptographicOperations.ZeroMemory(buffer); CryptographicOperations.ZeroMemory(output.GetBuffer().AsSpan(0, (int)output.Length)); }
        }
        finally { CryptographicOperations.ZeroMemory(packed); }
    }

    private static byte[] Encode(int width, int height, byte[] rgb, byte[]? profile, CancellationToken token)
    {
        using var png = new MemoryStream();
        try
        {
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height); header[8] = 8; header[9] = 2;
        Chunk(png, "IHDR"u8, header, token);
        if (profile is not null)
        {
            using var icc = new MemoryStream();
            try {
            icc.Write("original-profile\0\0"u8);
            using (var compressor = new ZLibStream(icc, CompressionLevel.Fastest, true)) compressor.Write(profile);
            Chunk(png, "iCCP"u8, icc.GetBuffer().AsSpan(0, (int)icc.Length), token);
            } finally { Wipe(icc); }
        }
        using var pixels = new MemoryStream();
        try {
        using (var compressor = new ZLibStream(pixels, CompressionLevel.Fastest, true))
            for (var row = 0; row < height; row++) { token.ThrowIfCancellationRequested(); compressor.WriteByte(0); compressor.Write(rgb, row * width * 3, width * 3); }
        Chunk(png, "IDAT"u8, pixels.GetBuffer().AsSpan(0, (int)pixels.Length), token);
        } finally { Wipe(pixels); }
        Chunk(png, "IEND"u8, [], token); return png.ToArray();
        } finally { Wipe(png); }
    }

    private static void Wipe(MemoryStream stream) => CryptographicOperations.ZeroMemory(stream.GetBuffer().AsSpan(0, (int)stream.Length));

    private static void Chunk(Stream output, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> data, CancellationToken token)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length); output.Write(number); output.Write(kind); output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in kind) crc = Crc(crc, value);
        for (var i = 0; i < data.Length; i++) { if (i % 65_536 == 0) token.ThrowIfCancellationRequested(); crc = Crc(crc, data[i]); }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
        static uint Crc(uint crc, byte value) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1; return crc; }
    }
}
