namespace Takupoke.Infrastructure.Recovery;

/// Numerical domain handling for the detector's Sigmoid probability map only.
/// Recognition confidence and candidate acceptance thresholds use their own guards.
internal static class OcrDetectorProbability
{
    private static readonly float MaximumRoundedUnit = float.BitIncrement(1f);

    internal static void ValidateMapShape(ReadOnlySpan<int> dimensions, int detectorWidth, int detectorHeight)
    {
        if (detectorWidth is <= 0 or > 960 || detectorHeight is <= 0 or > 960 ||
            dimensions.Length != 4 || dimensions[0] != 1 || dimensions[1] != 1 ||
            dimensions[2] != detectorHeight || dimensions[3] != detectorWidth)
            throw new InvalidDataException("OCR検出モデルの出力形状が一致しません。");
    }

    internal static float Normalize(float value)
    {
        // The pinned detector's exact output is in [0, 1]. Its CPU kernel can
        // round the upper endpoint to the immediately adjacent float32 value.
        // Accept only that one representable upper neighbour; reject every
        // negative/nonfinite value and any larger excess without guessing.
        if (!float.IsFinite(value) || value < 0 || value > MaximumRoundedUnit)
            throw new InvalidDataException("OCR検出の確信度が不正です。");
        return value > 1f ? 1f : value;
    }
}
