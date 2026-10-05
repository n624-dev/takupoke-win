using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrDetectorProbabilityTests
{
    [Fact]
    public void OnlyOneDeclaredNchwProbabilityMapIsAllowed()
    {
        OcrDetectorProbability.ValidateMapShape([1, 1, 32, 64], 64, 32);
        foreach (var dimensions in new int[][] { [2, 1, 32, 64], [1, 2, 32, 64],
            [0, 1, 32, 64], [1, 0, 32, 64], [1, 1, 32], [1, 1, 32, 64, 1],
            [1, 1, 31, 64], [1, 1, 32, 63] })
            Assert.Throws<InvalidDataException>(() => OcrDetectorProbability.ValidateMapShape(dimensions, 64, 32));
        Assert.Throws<InvalidDataException>(() => OcrDetectorProbability.ValidateMapShape([1, 1, 32, 961], 961, 32));
    }

    [Theory]
    [InlineData(0x00000000u)]
    [InlineData(0x80000000u)]
    [InlineData(0x00000001u)]
    [InlineData(0x3e99999au)] // Existing detector threshold .3f.
    [InlineData(0x3f19999au)] // Existing component mean .6f.
    [InlineData(0x3f7fffffu)] // Adjacent in-domain value below 1.
    [InlineData(0x3f800000u)]
    public void InDomainValuesKeepTheirExactBits(uint bits)
    {
        var value = BitConverter.UInt32BitsToSingle(bits);
        Assert.Equal(bits, BitConverter.SingleToUInt32Bits(OcrDetectorProbability.Normalize(value)));
    }

    [Fact]
    public void OnlyOneUpperFloat32NeighbourNormalizesToUnit()
    {
        // Actual frozen call67 sample358977 had exactly these float32 bits.
        var observed = BitConverter.UInt32BitsToSingle(0x3f800001);
        Assert.Equal(1d + Math.Pow(2, -23), (double)observed);
        Assert.Equal(1f, OcrDetectorProbability.Normalize(observed));
        Assert.Equal(0x3f800001u, BitConverter.SingleToUInt32Bits(observed));
    }

    [Theory]
    [InlineData(0x3f800002u)] // Two ULP above the bound.
    [InlineData(0x3fffffffu)]
    [InlineData(0x7f7fffffu)]
    [InlineData(0x80000001u)] // Even one negative subnormal remains invalid.
    [InlineData(0xbf800000u)]
    [InlineData(0x7f800000u)]
    [InlineData(0xff800000u)]
    [InlineData(0x7fc00001u)]
    public void LargerNegativeAndNonfiniteValuesStillRefuse(uint bits)
    {
        Assert.Throws<InvalidDataException>(() =>
            OcrDetectorProbability.Normalize(BitConverter.UInt32BitsToSingle(bits)));
    }
}
