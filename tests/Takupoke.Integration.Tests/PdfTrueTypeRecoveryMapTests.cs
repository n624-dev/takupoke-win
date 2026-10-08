using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PdfTrueTypeRecoveryMapTests
{
    [Fact]
    public void IdentityAndExplicitCidMapKeepExactFontGlyphAndScalar()
    {
        var font = Font(Format12((65, 66, 1), (0x1f642, 0x1f642, 3)));
        var map = PdfTrueTypeRecoveryMap.Read(font);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(font)), map.FontHash);
        Assert.Equal((1, "A"), map.Resolve(1, null));
        Assert.Equal((3, "🙂"), map.Resolve(3, null));
        Assert.Equal((2, "B"), map.Resolve(1, new byte[] { 0, 0, 0, 2 }));
        Assert.Throws<PdfParseException>(() => map.Resolve(0, null));
        Assert.Throws<PdfParseException>(() => map.Resolve(4, null));
        Assert.Throws<PdfParseException>(() => map.Resolve(1, new byte[] { 0, 0, 0 }));
        Assert.Throws<PdfParseException>(() => map.Resolve(2, new byte[] { 0, 0, 0, 2 }));
    }
    [Fact]
    public void WhitespaceAliasesAreAmbiguousAndNeverReplaced()
    {
        var map = PdfTrueTypeRecoveryMap.Read(Font(Format12((32, 32, 1), (65, 65, 2), (160, 160, 1))));
        Assert.Contains(1, map.AmbiguousGlyphs);
        Assert.DoesNotContain(1, map.UniqueScalars.Keys);
        Assert.Throws<PdfParseException>(() => map.Resolve(1, null));
        Assert.Equal((2, "A"), map.Resolve(2, null)); // An unused ambiguity does not invent another value.
    }
    [Fact]
    public void EveryUnicodeCmapContributesAndConflictsRemainUnresolved()
    {
        var map = PdfTrueTypeRecoveryMap.Read(Font(Format12((65, 65, 1)), Format12((66, 66, 1))));
        Assert.Contains(1, map.AmbiguousGlyphs);
        Assert.Throws<PdfParseException>(() => map.Resolve(1, null));
        var consistent = PdfTrueTypeRecoveryMap.Read(Font(Format4((65, 1)), Format12((65, 65, 1))));
        Assert.Equal((1, "A"), consistent.Resolve(1, null));
    }
    [Fact]
    public void Format4RangeArrayUsesItsOwnPositionAndDelta()
    {
        var sub = Format4((65, 1));
        // First segment's range entry lies at byte 28; glyph array begins at 32.
        U16(sub, 24, 1); U16(sub, 28, 4);
        Array.Resize(ref sub, 34); U16(sub, 2, 34); U16(sub, 32, 1);
        var map = PdfTrueTypeRecoveryMap.Read(Font(sub));
        Assert.Equal((2, "A"), map.Resolve(2, null));
        Assert.Throws<PdfParseException>(() => map.Resolve(1, null));
        U16(sub, 28, 2); // Points back into metadata, not the glyph array.
        Assert.Throws<PdfParseException>(() => PdfTrueTypeRecoveryMap.Read(Font(sub)));
    }
    [Fact]
    public void UnsupportedUnicodeCmapCannotBeIgnoredEvenWithOneGoodMap()
    { Assert.Throws<PdfParseException>(() => PdfTrueTypeRecoveryMap.Read(Font(Format12((65, 65, 1)), new byte[] { 0, 6 })) ); }
    [Fact]
    public void DuplicateSubtableReferencesDoNotConsumeTheWorkBudgetTwice()
    {
        var font = Font(Format12((65, 65, 1)), Format12((65, 65, 1)));
        var cmapOffset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(20, 4)));
        font.AsSpan(cmapOffset + 8, 4).CopyTo(font.AsSpan(cmapOffset + 16, 4));
        Assert.Equal((1, "A"), PdfTrueTypeRecoveryMap.Read(font).Resolve(1, null));
    }
    [Fact]
    public void EveryRequiredBytePrefixIsRejected()
    {
        var font = Font(Format12((65, 65, 1)));
        for (var size = 0; size < font.Length; size++)
            Assert.Throws<PdfParseException>(() => PdfTrueTypeRecoveryMap.Read(font[..size]));
    }
    [Fact]
    public void DuplicateAndOverlappingSfntTablesAreRejected()
    {
        var font = Font(Format12((65, 65, 1)));
        font.AsSpan(12, 4).CopyTo(font.AsSpan(28, 4));
        Assert.Throws<PdfParseException>(() => PdfTrueTypeRecoveryMap.Read(font));
        font = Font(Format12((65, 65, 1)));
        font.AsSpan(20, 4).CopyTo(font.AsSpan(36, 4));
        Assert.Throws<PdfParseException>(() => PdfTrueTypeRecoveryMap.Read(font));
    }
    [Theory]
    [InlineData(0xd800, 0xd800, 1)] [InlineData(0x110000, 0x110000, 1)]
    [InlineData(65, 64, 1)] [InlineData(65, 68, 1)]
    public void InvalidScalarAndGlyphRangesAreRejected(int first, int last, int gid)
    { Assert.Throws<PdfParseException>(() => PdfTrueTypeRecoveryMap.Read(Font(Format12((first, last, gid))))); }
    [Fact]
    public void OutOfOrderGroupsAndMissingUnicodeMapsAreRejected()
    {
        Assert.Throws<PdfParseException>(() => PdfTrueTypeRecoveryMap.Read(Font(Format12((66, 66, 1), (65, 65, 2)))));
        var font = Font(Format12((65, 65, 1)));
        var offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(20, 4)));
        U16(font, offset + 4, 1); // Macintosh cmap is not a Unicode mapping.
        Assert.Throws<PdfParseException>(() => PdfTrueTypeRecoveryMap.Read(font));
    }
    [Fact]
    public void CancellationIsPropagatedBeforeAnyMetadataResult()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.Throws<OperationCanceledException>(() => PdfTrueTypeRecoveryMap.Read(Font(Format12((65, 65, 1))), source.Token));
    }
    [Fact]
    public void ExcessiveDistinctMappingWorkIsRefusedWithoutTruncation()
    {
        var font = Font(Enumerable.Range(0, 64).Select(_ => Format12((0, 50000, 1))).ToArray());
        var maxp = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(36, 4)));
        U16(font, maxp + 4, 65535);
        Assert.Equal("limit", Assert.Throws<PdfParseException>(() => PdfTrueTypeRecoveryMap.Read(font)).Stage);
    }
    private static byte[] Font(params byte[][] subtables)
    {
        var cmap = new byte[4 + subtables.Length * 8 + subtables.Sum(x => x.Length)];
        U16(cmap, 2, subtables.Length); var position = 4 + subtables.Length * 8;
        for (var index = 0; index < subtables.Length; index++)
        {
            U16(cmap, 4 + index * 8, 0); U16(cmap, 6 + index * 8, 4); U32(cmap, 8 + index * 8, position);
            subtables[index].CopyTo(cmap, position); position += subtables[index].Length;
        }
        var maxp = new byte[6]; U32(maxp, 0, 0x00010000); U16(maxp, 4, 4);
        var tables = new[] { ("cmap", cmap), ("maxp", maxp), ("glyf", new byte[] { 0, 0 }), ("loca", new byte[10]) };
        var font = new byte[12 + 16 * tables.Length + tables.Sum(x => x.Item2.Length)];
        U32(font, 0, 0x00010000); U16(font, 4, tables.Length); position = 12 + 16 * tables.Length;
        for (var index = 0; index < tables.Length; index++)
        {
            Encoding.ASCII.GetBytes(tables[index].Item1).CopyTo(font, 12 + index * 16);
            U32(font, 20 + index * 16, position); U32(font, 24 + index * 16, tables[index].Item2.Length);
            tables[index].Item2.CopyTo(font, position); position += tables[index].Item2.Length;
        }
        return font;
    }
    private static byte[] Format12(params (int First, int Last, int Glyph)[] groups)
    {
        var result = new byte[16 + groups.Length * 12]; U16(result, 0, 12); U32(result, 4, result.Length); U32(result, 12, groups.Length);
        for (var index = 0; index < groups.Length; index++)
        { U32(result, 16 + index * 12, groups[index].First); U32(result, 20 + index * 12, groups[index].Last); U32(result, 24 + index * 12, groups[index].Glyph); }
        return result;
    }
    private static byte[] Format4((int Scalar, int Glyph) value)
    {
        var result = new byte[32]; U16(result, 0, 4); U16(result, 2, result.Length); U16(result, 6, 4);
        U16(result, 14, value.Scalar); U16(result, 16, 65535); U16(result, 20, value.Scalar); U16(result, 22, 65535);
        U16(result, 24, (value.Glyph - value.Scalar) & 65535); U16(result, 26, 1);
        return result;
    }
    private static void U16(byte[] value, int offset, int number) => BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(offset, 2), checked((ushort)number));
    private static void U32(byte[] value, int offset, int number) => BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(offset, 4), checked((uint)number));
}
