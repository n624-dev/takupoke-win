using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Infrastructure.Recovery;

// Research only: keep every original resized FP32 sample and validWidth.
// Remove only excessive normalized-zero RIGHT padding. Never resize again,
// trim ink, change a source crop or clamp a decoded CTC interval.
internal static class InputPadding
{
    internal static OcrInputTransform.RecognitionInput Restrict(OcrInputTransform.RecognitionInput input,
        int minimumInputWidth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (minimumInputWidth is not (160 or 320)) throw new InvalidDataException("Unregistered minimum recognition width.");
        var width = Math.Max(minimumInputWidth, input.ValidWidth);
        if (width >= input.InputWidth) return input;
        var tensor = new DenseTensor<float>([1, 3, 48, width]);
        for (var c = 0; c < 3; c++) for (var y = 0; y < 48; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < input.InputWidth; x++)
            {
                var sample = input.Tensor[0, c, y, x];
                if (x < width) tensor[0, c, y, x] = sample;
                else if (sample != 0) throw new InvalidDataException("Removed padding has nonzero normalized samples.");
            }
        }
        return new(tensor, input.Crop, input.ValidWidth, width);
    }
}
