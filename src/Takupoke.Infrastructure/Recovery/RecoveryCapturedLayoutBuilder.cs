using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;

/// Reuse complete native reader output before opening a renderer or an OCR runtime.
public static class RecoveryCapturedLayoutBuilder
{
    private sealed class RasterEvidenceRequired : Exception;

    public static RecoveryDocument? TryBuildWithoutRaster(string hash, MaterialKind kind,
        RecoveryReadCapture capture, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!capture.Complete || capture.Pages.Any(p => p.Layout is null)) return null;
        var pages = capture.Pages.Select(p => p.Layout!).ToArray();
        for (var i = 0; i < pages.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            pages[i].Validate(i + 1);
        }
        try
        {
            // Complete text alone does not certify a visually blank field. Fall
            // back to raster evidence only if the existing builder requests it.
            return RecoveryDocumentBuilder.Build(hash, kind, pages,
                (_, _) => throw new RasterEvidenceRequired(), token: token, allowStructureProposal: true);
        }
        catch (RasterEvidenceRequired) { return null; }
    }
}
