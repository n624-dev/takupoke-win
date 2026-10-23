using System.IO;
using Takupoke.Core;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static void CheckRetentionRecovery(string executable, string originalRoot)
    {
        Stop();
        var root = Path.Combine(Path.GetDirectoryName(originalRoot)!, "fictional-retention-ui-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("TAKUPOKE_DATA_ROOT", root);
        try
        {
            SeedAsync(root).GetAwaiter().GetResult();
            new PreferencesStore(root).SaveAsync(new() { SelectedClasses = ["3_IT"], MainColor = "purple" }).GetAwaiter().GetResult();
            var preferences = Path.Combine(root, "preferences.json"); var preferenceBytes = File.ReadAllBytes(preferences);
            var original = Path.Combine(root, "fake-available-original.pdf"); var originalBytes = File.ReadAllBytes(original);
            var publicFile = Path.Combine(root, "public-events", "fictional-retained.cache");
            Directory.CreateDirectory(Path.GetDirectoryName(publicFile)!); File.WriteAllText(publicFile, "fictional public cache bytes");
            var marker = Path.Combine(root, "school-data-period.json"); File.WriteAllText(marker, "{fictional-corrupt-marker");
            var database = Path.Combine(root, "school", "school.sqlite"); var databaseBytes = File.ReadAllBytes(database);
            Start(executable);
            Wait(() => Find("recover-retention")?.Current.IsEnabled == true, "corrupt retention offers explicit recovery");
            Require(Find("link-fake-study") is null, "Unavailable private links are not displayed");
            Invoke("recover-retention"); Invoke(ByName("キャンセル"));
            Wait(() => Find("recover-retention")?.Current.IsEnabled == true, "cancelling keeps recovery available");
            Require(File.ReadAllText(marker) == "{fictional-corrupt-marker" && File.ReadAllBytes(database).SequenceEqual(databaseBytes),
                "Cancelling preserves the malformed marker and original private database");
            Invoke("recover-retention"); Invoke(ByName("初期化して復旧"));
            Wait(() => Find("recover-retention") is null && Find("refresh-home")?.Current.IsEnabled == true, "confirmed recovery and queued reload complete");
            Require(DataCodec.Decode<RetentionMarker>(File.ReadAllBytes(marker)).SchemaVersion == 1, "The actual recovery commits a valid period marker");
            Require(File.ReadAllBytes(preferences).SequenceEqual(preferenceBytes), "Recovery preserves personal preferences");
            Require(File.ReadAllBytes(original).SequenceEqual(originalBytes), "Recovery preserves the selected original file");
            Require(File.ReadAllText(publicFile) == "fictional public cache bytes", "Recovery preserves the public cache");
            Require(!Directory.Exists(Path.Combine(root, "school-data-period-recovery")), "Committed recovery removes its quarantine");
            Navigate("settings"); Invoke("settings-materials");
            Wait(() => Find("material-summary-Timetable")?.Current.Name.EndsWith("未選択", StringComparison.Ordinal) == true, "Private material is reset rather than reused as current");
            Stop(); Start(executable);
            Require(Find("recover-retention") is null, "The recovered marker survives app restart");
            Navigate("settings"); Invoke("settings-materials");
            Wait(() => Find("material-summary-Timetable")?.Current.Name.EndsWith("未選択", StringComparison.Ordinal) == true, "Restart keeps the empty private material state");
        }
        finally
        {
            Stop(); Environment.SetEnvironmentVariable("TAKUPOKE_DATA_ROOT", originalRoot);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
