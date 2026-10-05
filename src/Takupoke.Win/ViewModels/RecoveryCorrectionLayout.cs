namespace Takupoke.Win.ViewModels;

// Pure layout choice; relayout reuses the same controls and never edits consent.
public static class RecoveryCorrectionLayout
{
    public static bool SideBySide(double availableWidth) => double.IsFinite(availableWidth) && availableWidth >= 680;
}
