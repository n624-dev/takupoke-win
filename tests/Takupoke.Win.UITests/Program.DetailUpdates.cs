using System.IO;
using System.Windows.Automation;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static bool LessonDetailOpen() => _window?.FindFirst(TreeScope.Descendants,
        new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "閉じる"),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))) is not null;

    private static async Task<(SourceRecord Source, MaterialAnalysis Analysis)> DetailStoreAsync(string root)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector());
        var lease = await store.BeginAsync();
        return (await store.ReadAsync<SourceRecord>(lease, "selection.Timetable") ?? throw new InvalidOperationException("Synthetic source missing"),
            await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Timetable") ?? throw new InvalidOperationException("Synthetic formal analysis missing"));
    }

    private static int DetailClockTicks(string root) => int.TryParse(
        File.Exists(Path.Combine(root,"offline-clock-ticks.txt")) ? File.ReadAllText(Path.Combine(root,"offline-clock-ticks.txt")) : "0", out var ticks) ? ticks : 0;

    private static void CheckDetailSnapshotUpdates(string executable, string root)
    {
        var previousRoot = Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT");
        foreach (var surface in new[] { "timetable", "analysis" })
        {
            var isolated = Path.Combine(root,"fictional-details-" + Guid.NewGuid().ToString("N"));
            try
            {
                Stop(); Directory.CreateDirectory(isolated);
                Environment.SetEnvironmentVariable("TAKUPOKE_DATA_ROOT",isolated);
                SeedAsync(isolated).GetAwaiter().GetResult();
                new PreferencesStore(isolated).SaveAsync(new UserPreferences { SelectedClasses = ["3_IT"], SetupCompleted = true,
                    NotificationsSetupCompleted = true }).GetAwaiter().GetResult();
                Start(executable);
                if (surface == "timetable")
                {
                    Navigate("timetable");
                    // Heading availability precedes the completed UIA subtree. Select
                    // this week through the real control rather than treating a
                    // transient missing row as permission to advance to another week.
                    Wait(() => Find("timetable-current") is { Current.IsEnabled: true },"The native current-week control is available");
                    Invoke("timetable-current");
                }
                else { Navigate("settings"); Invoke("settings-materials"); Invoke("material-details-Timetable"); Invoke("analysis-Timetable"); }

                void Open(string subject)
                {
                    AutomationElement[] lessons = [];
                    try { Wait(() => (lessons=FindLessons(subject)).Length > 0, "The accepted row is rendered for " + surface); }
                    catch
                    {
                        // Report the wholly fictional child before its guaranteed
                        // cleanup; the outer capture otherwise sees no failed window.
                        var saved = DetailStoreAsync(isolated).GetAwaiter().GetResult();
                        var preferences = new PreferencesStore(isolated).LoadAsync().GetAwaiter().GetResult();
                        var elements = _window?.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>()
                            .Select(e => e.Current.ControlType.ProgrammaticName + ":" + e.Current.AutomationId + ":" + e.Current.Name)
                            .Where(name => !string.IsNullOrWhiteSpace(name)).Take(160).ToArray() ?? [];
                        Console.WriteLine($"Synthetic detail failure {surface}: expected={subject}; period={saved.Analysis.SchoolYear}/{saved.Analysis.Timetable?.Term}; classes={string.Join(',',preferences.SelectedClasses)}; formal={string.Join(',',saved.Analysis.Timetable?.Lessons.Select(l => l.ClassName+"/"+l.Weekday+"/"+l.Names.Subject) ?? [])}; current-week={Find("timetable-week-picker")?.Current.Name}; nodes={string.Join(" | ",elements)}");
                        throw;
                    }
                    Invoke(lessons[0]); Wait(LessonDetailOpen,"The native lesson detail opens for " + surface);
                }

                Open("架空科目甲");
                var before = DetailStoreAsync(isolated).GetAwaiter().GetResult();
                var ticks = DetailClockTicks(isolated);
                // A real SourceWatcher refresh changes only acquisition/check metadata.
                File.SetLastWriteTimeUtc(before.Source.Path,DateTime.UtcNow.AddSeconds(2));
                Wait(() => DetailStoreAsync(isolated).GetAwaiter().GetResult().Source.LastCheckedAt > before.Source.LastCheckedAt,
                    "The unchanged original completed a real source refresh while the detail was open");
                Wait(() => DetailClockTicks(isolated)>ticks,"A real periodic clock tick occurred with the detail open");
                Require(LessonDetailOpen(),"Metadata-only refresh and clock/busy changes preserve the detail snapshot");

                var updated = before.Analysis with { ParsedAt = DateTimeOffset.UtcNow,
                    Timetable = before.Analysis.Timetable! with { Lessons = before.Analysis.Timetable.Lessons.Select(l =>
                        l with { Names = l.Names with { Subject = "架空更新科目", SubjectFullName = null } }).ToArray() } };
                using (var operation = new DetailStoreOperation(isolated)) operation.SaveAnalysis(updated);
                File.SetLastWriteTimeUtc(before.Source.Path,DateTime.UtcNow.AddSeconds(3));
                Wait(() => !LessonDetailOpen(),"A same-PDF actual formal save closes the captured detail for " + surface);
                Require(DetailStoreAsync(isolated).GetAwaiter().GetResult().Source.Digest == before.Source.Digest,
                    "The formal update keeps the same original PDF hash");

                Open("架空更新科目");
                using (var operation = new DetailStoreOperation(isolated)) operation.SaveMapping();
                File.SetLastWriteTimeUtc(before.Source.Path,DateTime.UtcNow.AddSeconds(4));
                Wait(() => !LessonDetailOpen(),"Actual mapping replacement closes the captured detail for " + surface);

                Open("架空更新科目");
                Require(RecoveryUiText("架空更新正式名"),"The reopened detail uses the new mapping with the accepted lesson");
                File.AppendAllText(before.Source.Path,"\n% Entirely fictional changed source for an open detail.\n");
                Wait(() => !LessonDetailOpen(),"An actual changed original closes the captured detail for " + surface);
                var changed = DetailStoreAsync(isolated).GetAwaiter().GetResult();
                Require(changed.Source.Digest != before.Source.Digest && changed.Analysis.SourceDigest == before.Source.Digest,
                    "A failed changed source keeps the previous formal analysis while closing stale details");

                Navigate("settings"); Invoke("settings-about"); Invoke(ByName("利用規約"));
                Wait(LessonDetailOpen,"A general legal dialog opens independently of schedule data");
                var checkedAt = changed.Source.LastCheckedAt; ticks = DetailClockTicks(isolated);
                using (var operation = new DetailStoreOperation(isolated)) operation.SaveMapping("架空別正式名");
                File.SetLastWriteTimeUtc(before.Source.Path,DateTime.UtcNow.AddSeconds(5));
                Wait(() => DetailStoreAsync(isolated).GetAwaiter().GetResult().Source.LastCheckedAt > checkedAt,"The general dialog allows a real saved-data refresh");
                Wait(() => DetailClockTicks(isolated)>ticks,"The general dialog stays open across a real clock tick");
                Require(LessonDetailOpen(),"Schedule dependency changes do not dismiss a general settings/legal dialog");
                Invoke(ByName("閉じる"));
                Console.WriteLine($"Synthetic detail UI {surface}: metadata/busy/tick preserved; actual formal/source/mapping changes closed; general dialog preserved.");
            }
            finally
            {
                Stop(); Environment.SetEnvironmentVariable("TAKUPOKE_DATA_ROOT",previousRoot);
                if (Directory.Exists(isolated)) Directory.Delete(isolated,true);
            }
        }
    }

    private sealed class DetailStoreOperation : IDisposable
    {
        private readonly SchoolDataStore _store;
        private readonly SchoolLease _lease;
        public DetailStoreOperation(string root) { _store=new(root,new WindowsDpapiProtector());_lease=_store.BeginAsync().GetAwaiter().GetResult(); }
        public void SaveAnalysis(MaterialAnalysis analysis) => _store.SaveAnalysisAsync(_lease,analysis).GetAwaiter().GetResult();
        public void SaveMapping(string name="架空更新正式名") => _store.WriteAsync(_lease,"api.mapping",new SavedMapping(new string(name=="架空更新正式名"?'B':'C',43),"fictional-detail",1,"\"fictional-detail\"",new string('b',64),"2032-04-01T00:00:00Z",DateTimeOffset.UtcNow,
            new MappingRules([new MappingRule("架空更新科目",name)],[],[]))).GetAwaiter().GetResult();
        public void Dispose() => _store.DisposeAsync().GetAwaiter().GetResult();
    }
}
