using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Core.Recovery;

namespace Takupoke.Infrastructure.Recovery;

// PaddleX 917f10be OCRReisizeNormImg's single-image resize/padding recipe.
// The app retains its 960-pixel capacity bound, rather than adopting 3200.
internal static class OcrInputTransform
{
    internal sealed record RecognitionInput(DenseTensor<float> Tensor, RecoveryBox Crop, int ValidWidth, int InputWidth)
    {
        public RecoveryBox SourceBox(int start, int end, int timeCount)
        {
            if (timeCount <= 0 || start < 0 || end <= start || end > timeCount)
                throw new InvalidDataException("OCR文字の時間位置が不正です。");
            // Use the entire padded input's time axis. A character touching
            // padding has no fully supported source interval; never clamp it.
            if ((long)end * InputWidth > (long)ValidWidth * timeCount)
                throw new InvalidDataException("OCR文字の位置が入力画像の余白に重なっています。");
            var left = Crop.X + (double)start * InputWidth / ((double)timeCount * ValidWidth) * Crop.Width;
            var right = Crop.X + (double)end * InputWidth / ((double)timeCount * ValidWidth) * Crop.Width;
            var result = new RecoveryBox(left, Crop.Y, right - left, Crop.Height);
            if (!Crop.Contains(result)) throw new InvalidDataException("OCR文字の位置が原画像の範囲外です。");
            return result;
        }
    }

    public static RecognitionInput Recognition(RecoveryRaster image, RecoveryBox crop, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); Validate(image, crop);
        var ratio = crop.Width / crop.Height;
        var scaledWidth = 48 * ratio;
        var requiredWidth = Math.Floor(48 * Math.Max(320d / 48, ratio));
        if (!double.IsFinite(requiredWidth) || requiredWidth > 960)
            throw new InvalidDataException("OCR認識画像の幅が上限を超えています。");
        var inputWidth = (int)requiredWidth;
        var validWidth = (int)Math.Min(inputWidth, Math.Ceiling(scaledWidth));
        if (validWidth < 1) throw new InvalidDataException("OCR認識画像の幅が不正です。");
        var tensor = new DenseTensor<float>([1, 3, 48, inputWidth]);
        Fill(image, crop, tensor, validWidth, 48, false, token);
        // Unwritten right-hand values stay zero AFTER normalization, matching
        // the reference. This is neither white ink nor an empty-field proof.
        return new(tensor, crop, validWidth, inputWidth);
    }

    public static DenseTensor<float> Detection(RecoveryRaster image, RecoveryBox crop, int width, int height, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); Validate(image, crop);
        if (width is < 1 or > 960 || height is < 1 or > 960)
            throw new InvalidDataException("OCR検出画像のサイズが上限を超えています。");
        var tensor = new DenseTensor<float>([1, 3, height, width]);
        Fill(image, crop, tensor, width, height, true, token);
        return tensor;
    }

    private static void Validate(RecoveryRaster image, RecoveryBox crop)
    {
        if (!image.Valid || !new RecoveryBox(0, 0, image.Width, image.Height).Contains(crop))
            throw new InvalidDataException("OCR入力画像の範囲が不正です。");
    }

    private static void Fill(RecoveryRaster image, RecoveryBox crop, DenseTensor<float> tensor, int width, int height, bool detection, CancellationToken token)
    {
        // Sample pixel centres inside the original crop only. For integer
        // crops this is the half-pixel/border replication resize convention.
        // Fractional detector boxes keep their original support and geometry.
        var minX = Math.Max(0, (int)Math.Ceiling(crop.X - .5));
        var maxX = Math.Min(image.Width - 1, (int)Math.Floor(crop.X + crop.Width - .5));
        var minY = Math.Max(0, (int)Math.Ceiling(crop.Y - .5));
        var maxY = Math.Min(image.Height - 1, (int)Math.Floor(crop.Y + crop.Height - .5));
        if (minX > maxX || minY > maxY) throw new InvalidDataException("OCR入力画像に画素の根拠がありません。");
        float[] mean = [.485f, .456f, .406f], std = [.229f, .224f, .225f];
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var sy = Math.Clamp(crop.Y + (y + .5) * crop.Height / height - .5, minY, maxY);
            var y0 = (int)Math.Floor(sy); var y1 = Math.Min(y0 + 1, maxY); var fy = sy - y0;
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp(crop.X + (x + .5) * crop.Width / width - .5, minX, maxX);
                var x0 = (int)Math.Floor(sx); var x1 = Math.Min(x0 + 1, maxX); var fx = sx - x0;
                for (var c = 0; c < 3; c++)
                {
                    var a = image.Bgra[(y0 * image.Width + x0) * 4 + c]; var b = image.Bgra[(y0 * image.Width + x1) * 4 + c];
                    var d = image.Bgra[(y1 * image.Width + x0) * 4 + c]; var e = image.Bgra[(y1 * image.Width + x1) * 4 + c];
                    // Explicit uint8 resize rounding; OpenCV's integer kernel
                    // may differ by one byte. Preserve that measured tolerance.
                    var value = (float)Math.Floor((a * (1 - fx) + b * fx) * (1 - fy) + (d * (1 - fx) + e * fx) * fy + .5);
                    tensor[0, c, y, x] = detection ? (value / 255f - mean[c]) / std[c] : (value / 255f - .5f) / .5f;
                }
            }
        }
    }
}
