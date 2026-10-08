using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Takupoke.Infrastructure.Parsing;

/// Bounded font-metadata decoding for recovery only. A unique cmap entry does
/// not certify drawing visibility, page completeness or timetable ownership.
internal sealed record PdfTrueTypeRecoveryMap(string FontHash,
    IReadOnlyDictionary<int, int> UniqueScalars, IReadOnlySet<int> AmbiguousGlyphs, int GlyphCount)
{
    internal const int MaximumFontBytes = 20_000_000;
    private const int MaximumWork = 1_200_000;

    internal (int GlyphId, string Text) Resolve(int cid, ReadOnlyMemory<byte>? cidToGid)
    {
        if (cid is < 0 or > 65535) throw new PdfParseException("P01");
        var gid = cid;
        if (cidToGid is { } bytes)
        {
            if (bytes.Length > 131072 || bytes.Length % 2 != 0 || cid * 2 + 2 > bytes.Length)
                throw new PdfParseException("P01");
            gid = BinaryPrimitives.ReadUInt16BigEndian(bytes.Span.Slice(cid * 2, 2));
        }
        if (gid <= 0 || gid >= GlyphCount || AmbiguousGlyphs.Contains(gid) || !UniqueScalars.TryGetValue(gid, out var scalar))
            throw new PdfParseException("P01");
        return (gid, char.ConvertFromUtf32(scalar));
    }

    internal static PdfTrueTypeRecoveryMap Read(byte[] font, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (font.Length is < 12 or > MaximumFontBytes || U32(font, 0) != 0x00010000)
            throw new PdfParseException("P01");
        var count = U16(font, 4);
        if (count is < 1 or > 256) throw new PdfParseException("P01");
        var directoryEnd = 12 + count * 16;
        Slice(font, 0, directoryEnd);
        var tables = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var extents = new List<(int Start, int End)>();
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            var offset = 12 + index * 16;
            var tag = Encoding.ASCII.GetString(Slice(font, offset, 4));
            var start = Size(U32(font, offset + 8)); var length = Size(U32(font, offset + 12));
            if (start < directoryEnd || !tables.TryAdd(tag, font.AsMemory(start, CheckedLength(font, start, length))))
                throw new PdfParseException("P01");
            if (length > 0) extents.Add((start, start + length));
        }
        extents.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (var index = 1; index < extents.Count; index++)
            if (extents[index - 1].End > extents[index].Start) throw new PdfParseException("P01");
        if (!new[] { "cmap", "maxp", "glyf", "loca" }.All(tables.ContainsKey)) throw new PdfParseException("P01");
        var glyphCount = U16(tables["maxp"].Span, 4);
        if (glyphCount == 0) throw new PdfParseException("P01");
        var cmap = tables["cmap"].Span;
        if (U16(cmap, 0) != 0) throw new PdfParseException("P01");
        var maps = U16(cmap, 2);
        if (maps is < 1 or > 64) throw new PdfParseException("P01");
        var recordsEnd = 4 + maps * 8;
        Slice(cmap, 0, recordsEnd);
        var seen = new HashSet<int>(); var unique = new Dictionary<int, int>(); var ambiguous = new HashSet<int>();
        var supported = 0; var work = 0;
        void Consume(int units)
        {
            token.ThrowIfCancellationRequested();
            if (units < 0 || units > MaximumWork - work) throw new PdfParseException("limit");
            work += units;
        }
        void Add(int scalar, int gid)
        {
            if (gid >= glyphCount) throw new PdfParseException("P01");
            if (gid == 0) return;
            if (!Rune.IsValid(scalar)) throw new PdfParseException("P01");
            if (ambiguous.Contains(gid)) return;
            if (!unique.TryAdd(gid, scalar) && unique[gid] != scalar)
            { unique.Remove(gid); ambiguous.Add(gid); }
        }
        for (var index = 0; index < maps; index++)
        {
            Consume(1);
            var record = 4 + index * 8; var platform = U16(cmap, record); var encoding = U16(cmap, record + 2);
            if (platform != 0 && !(platform == 3 && encoding is 1 or 10)) continue;
            var offset = Size(U32(cmap, record + 4));
            if (offset < recordsEnd) throw new PdfParseException("P01");
            if (!seen.Add(offset)) continue;
            var format = U16(cmap, offset); supported++;
            if (format == 4)
            {
                var sub = Slice(cmap, offset, U16(cmap, offset + 2)); var twice = U16(sub, 6);
                if (twice == 0 || twice % 2 != 0 || twice > 8192) throw new PdfParseException("P01");
                var segments = twice / 2; var glyphStart = 16 + segments * 8;
                Slice(sub, 0, glyphStart);
                if (U16(sub, 14 + segments * 2) != 0) throw new PdfParseException("P01");
                var previous = -1;
                for (var segment = 0; segment < segments; segment++)
                {
                    var end = U16(sub, 14 + segment * 2); var start = U16(sub, 16 + segments * 2 + segment * 2);
                    var delta = U16(sub, 16 + segments * 4 + segment * 2);
                    var rangePosition = 16 + segments * 6 + segment * 2; var range = U16(sub, rangePosition);
                    if (start > end || start <= previous || range % 2 != 0) throw new PdfParseException("P01");
                    previous = end; Consume(end - start + 1);
                    for (var scalar = (int)start; scalar <= end; scalar++)
                    {
                        if ((scalar & 255) == 0) token.ThrowIfCancellationRequested();
                        int gid;
                        if (range == 0) gid = (scalar + delta) & 65535;
                        else
                        {
                            var position = rangePosition + range + 2 * (scalar - start);
                            if (position < glyphStart) throw new PdfParseException("P01");
                            gid = U16(sub, position);
                            if (gid != 0) gid = (gid + delta) & 65535;
                        }
                        Add(scalar, gid);
                    }
                }
                if (previous != 65535) throw new PdfParseException("P01");
            }
            else if (format == 12)
            {
                var sub = Slice(cmap, offset, Size(U32(cmap, offset + 4)));
                if (U16(sub, 2) != 0) throw new PdfParseException("P01");
                var groups = Size(U32(sub, 12));
                if (groups > 65536 || sub.Length != 16 + (long)groups * 12) throw new PdfParseException("P01");
                var previous = -1;
                for (var group = 0; group < groups; group++)
                {
                    var position = 16 + group * 12; var start = Size(U32(sub, position)); var end = Size(U32(sub, position + 4));
                    var firstGlyph = Size(U32(sub, position + 8));
                    if (start > end || start <= previous || end > 0x10ffff || start <= 0xdfff && end >= 0xd800
                        || (long)firstGlyph + end - start >= glyphCount) throw new PdfParseException("P01");
                    previous = end; Consume(end - start + 1);
                    for (var scalar = start; scalar <= end; scalar++)
                    {
                        if ((scalar & 255) == 0) token.ThrowIfCancellationRequested();
                        Add(scalar, firstGlyph + scalar - start);
                    }
                }
            }
            else throw new PdfParseException("P01"); // Do not ignore a potentially contradictory Unicode cmap.
        }
        if (supported == 0) throw new PdfParseException("P01");
        token.ThrowIfCancellationRequested();
        return new(Convert.ToHexStringLower(SHA256.HashData(font)), unique, ambiguous, glyphCount);
    }

    private static int Size(uint number) => number <= int.MaxValue ? (int)number : throw new PdfParseException("P01");
    private static int CheckedLength(ReadOnlySpan<byte> data, int offset, int length)
    { if (offset < 0 || length < 0 || offset > data.Length - length) throw new PdfParseException("P01"); return length; }
    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int length) => data.Slice(offset, CheckedLength(data, offset, length));
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(Slice(data, offset, 2));
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(Slice(data, offset, 4));
}
