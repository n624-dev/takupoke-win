using System.Security.Cryptography;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

// Research-only input condition; no recognition-dependent crop or output clamp.
internal static class ResearchAspect
{
    internal static OcrInputTransform.RecognitionInput Recognition(RecoveryRaster original, int factor, CancellationToken token)
    {
        var crop = new RecoveryBox(0, 0, original.Width, original.Height);
        if (factor == 1) return OcrInputTransform.Recognition(original, crop, token);
        if (factor != 2 || !original.Valid) throw new InvalidDataException("Fixed research aspect factor failed.");
        var width = checked(original.Width * factor);
        var pixels = new byte[checked(width * original.Height * 4)];
        try
        {
            for (var y = 0; y < original.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < original.Width; x++) for (var repeat = 0; repeat < factor; repeat++)
                    original.Bgra.AsSpan((y * original.Width + x) * 4, 4)
                        .CopyTo(pixels.AsSpan((y * width + x * factor + repeat) * 4, 4));
            }
            var stretched = new RecoveryRaster(width, original.Height, pixels);
            var input = OcrInputTransform.Recognition(stretched, new(0, 0, width, original.Height), token);
            // Retain normalized tensor/validWidth from the wider input but map
            // the full supported time axis back to the unchanged original crop.
            // The existing SourceBox still refuses emitted padding intervals.
            return input with { Crop = crop };
        }
        finally { CryptographicOperations.ZeroMemory(pixels); }
    }
}
