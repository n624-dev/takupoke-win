using Takupoke.Win.Platform;

internal static class SingleRoleAssessment
{
    internal static bool IsAssessed(SingleRoleObservation? observation) =>
        observation is { NativeReturned: > 0, OperationalError: false };

    internal static object Preflight()
    {
        var raw = "{\"ids\":[\"original\"]}";
        var valid = new SingleRoleObservation("subject", raw, raw.Length, false, ["original"], null, null, false, 1, 1, 1);
        var malformed = valid with { RawOutput = "{}", OriginalLength = 2, SelectedIds = null, ErrorType = "InvalidRecoveryOutputException" };
        var deadlineAfterReturn = valid with { OperationalError = true, ErrorType = "OperationCanceledException" };
        var noReturn = valid with { NativeReturned = 0 };
        if (!IsAssessed(valid) || !IsAssessed(malformed) || IsAssessed(deadlineAfterReturn) || IsAssessed(noReturn) || IsAssessed(null))
            throw new InvalidDataException("Single-role operational/semantic assessment boundary failed.");
        var mixed = new[] { valid, malformed, deadlineAfterReturn };
        var assessed = mixed.Count(IsAssessed); var unassessed = mixed.Length - assessed;
        if (assessed != 2 || unassessed != 1) throw new InvalidDataException("Good completed roles were dropped or runtime failures were counted as wrong.");
        return new { boundaryCases = 5, mixedAssessed = assessed, mixedUnassessed = unassessed, nativeCalls = 0,
            scope = "Scorer boundaries only; invented observations are never model evidence" };
    }
}
