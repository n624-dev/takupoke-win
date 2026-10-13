using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

public sealed class InputPaddingTests
{
    [Theory]
    [InlineData(30, 20, 160)] [InlineData(84, 20, 202)]
    public void EveryResizedSampleAndOriginalCropAreUnchanged(int width, int height, int expectedWidth)
    {
        var image = Pixels(width, height);
        var input = OcrInputTransform.Recognition(image, new(0, 0, width, height));
        var reduced = InputPadding.Restrict(input, 160, CancellationToken.None);
        Assert.Equal(input.Crop, reduced.Crop); Assert.Equal(input.ValidWidth, reduced.ValidWidth);
        Assert.Equal(expectedWidth, reduced.InputWidth);
        for (var c = 0; c < 3; c++) for (var y = 0; y < 48; y++) for (var x = 0; x < reduced.InputWidth; x++)
            Assert.Equal(BitConverter.SingleToInt32Bits(input.Tensor[0, c, y, x]), BitConverter.SingleToInt32Bits(reduced.Tensor[0, c, y, x]));
    }

    [Fact]
    public void SourceIntervalStillUsesActualNativeTimeAxisAndRefusesPadding()
    {
        var input = OcrInputTransform.Recognition(Pixels(30, 20), new(0, 0, 30, 20));
        var reduced = InputPadding.Restrict(input, 160, CancellationToken.None);
        Assert.Equal(input.SourceBox(2, 4, 40), reduced.SourceBox(2, 4, 20));
        Assert.Throws<InvalidDataException>(() => reduced.SourceBox(9, 10, 20));
    }

    [Fact]
    public void WideInputsAndDefaultMinimumKeepOriginalTensor()
    {
        var narrow = OcrInputTransform.Recognition(Pixels(30, 20), new(0, 0, 30, 20));
        Assert.Same(narrow, InputPadding.Restrict(narrow, 320, CancellationToken.None));
        var wide = OcrInputTransform.Recognition(Pixels(200, 20), new(0, 0, 200, 20));
        Assert.Same(wide, InputPadding.Restrict(wide, 160, CancellationToken.None));
    }

    [Fact]
    public void NonzeroRemovedSampleUnknownWidthAndCancellationRefuse()
    {
        var input = OcrInputTransform.Recognition(Pixels(30, 20), new(0, 0, 30, 20));
        input.Tensor[0, 2, 47, 319] = .01f;
        Assert.Throws<InvalidDataException>(() => InputPadding.Restrict(input, 160, CancellationToken.None));
        Assert.Throws<InvalidDataException>(() => InputPadding.Restrict(input, 0, CancellationToken.None));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => InputPadding.Restrict(input, 160, cancel.Token));
    }

    private static RecoveryRaster Pixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 256);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return new(width, height, pixels);
    }
}
