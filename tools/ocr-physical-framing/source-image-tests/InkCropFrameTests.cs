using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

public sealed class InkCropFrameTests
{
    [Fact]
    public void FaintOriginalTextIsRetainedAndOnlyWhiteMarginsAreTrimmed()
    {
        var image = White(40, 30); Paint(image, 10, 12, 0); Paint(image, 22, 17, 254);
        var original = new RecoveryBox(2, 3, 34, 23);
        var box = new InkCropFrame(image, new bool[1200], CancellationToken.None).Restrict(original);
        Assert.Equal(new RecoveryBox(8, 10, 17, 10), box);
        Assert.True(box.Contains(new(10, 12, 1, 1)));
        Assert.True(box.Contains(new(22, 17, 1, 1)));
        Assert.True(original.Contains(box));
        Assert.Equal(254, image.Bgra[(17 * image.Width + 22) * 4]);
    }

    [Fact]
    public void CertifiedRulePixelsAreTheOnlyNonwhitePixelsThatMayBeRemoved()
    {
        var image = White(40, 30); Paint(image, 10, 12, 0); Paint(image, 22, 17, 0);
        var mask = new bool[1200]; mask[17 * image.Width + 22] = true;
        var box = new InkCropFrame(image, mask, CancellationToken.None).Restrict(new(2, 3, 34, 23));
        Assert.Equal(new RecoveryBox(8, 10, 5, 5), box);
        // Only a proven mask changes ownership; the original byte is unchanged.
        Assert.Equal(0, image.Bgra[(17 * image.Width + 22) * 4]);
    }

    [Fact]
    public void FrameNeverAddsPixelsOutsideOriginalFractionalCrop()
    {
        var image = White(20, 20); Paint(image, 5, 5, 0); Paint(image, 19, 19, 0);
        var original = new RecoveryBox(4.4, 4.4, 3.2, 3.2);
        var box = new InkCropFrame(image, new bool[400], CancellationToken.None).Restrict(original);
        Assert.Equal(original, box);
    }

    [Fact]
    public void WhiteOrRuleOnlyCropDoesNotAssertNewEmptyField()
    {
        var image = White(20, 20); var original = new RecoveryBox(0, 0, 20, 20);
        Assert.Equal(original, new InkCropFrame(image, new bool[400], CancellationToken.None).Restrict(original));
        Paint(image, 9, 9, 0); var mask = new bool[400]; mask[189] = true;
        Assert.Equal(original, new InkCropFrame(image, mask, CancellationToken.None).Restrict(original));
    }

    [Fact]
    public void WorkBudgetIsNotReplenishedForTheNextCrop()
    {
        var image = White(512, 512); Paint(image, 256, 256, 0);
        var context = new InkCropFrame(image, new bool[512 * 512], CancellationToken.None);
        Assert.Throws<InvalidDataException>(() =>
        {
            for (var i = 0; i < 129; i++) context.Restrict(new(0, 0, 512, 512));
        });
    }

    [Fact]
    public void CancellationAndBadMaskRefuse()
    {
        var image = White(20, 20);
        Assert.Throws<InvalidDataException>(() => new InkCropFrame(image, new bool[399], CancellationToken.None).Restrict(new(0, 0, 20, 20)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => new InkCropFrame(image, new bool[400], cancel.Token).Restrict(new(0, 0, 20, 20)));
    }

    private static RecoveryRaster White(int width, int height)
    {
        var bytes = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        return new(width, height, bytes);
    }
    private static void Paint(RecoveryRaster image, int x, int y, byte value)
    {
        var index = (y * image.Width + x) * 4;
        image.Bgra[index] = image.Bgra[index + 1] = image.Bgra[index + 2] = value;
    }
}
