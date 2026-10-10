using System.IO;
using System.Windows.Automation;
using Takupoke.Core;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Storage;
using Takupoke.Testing;
using Takupoke.Win.Platform;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static void CheckChangeRowSkips(string executable, string root)
    {
        Stop();
        PrepareChangeRowsAsync(root).GetAwaiter().GetResult();
        Start(executable);
        OpenChangeDetails();
        OpenChangePreview();
        Require(!ByName("選んだ行を除外").Current.IsEnabled,
            "Exclusion requires an explicit row selection.");
        SelectChangeRow(3);
        SelectChangeRow(4);
        SelectChangeGroup();
        Invoke(ByName("キャンセル"));
        Require(!ReadChangeStateAsync(root).GetAwaiter().GetResult(),
            "Cancelling the preview leaves the previous result and no consent.");

        OpenChangePreview();
        Require(!Checked(WaitElement("change-skip-row-3"))
            && !Checked(WaitElement("change-skip-row-4"))
            && !Checked(WaitElement("change-skip-group-200-300")),
            "Cancelled choices are not silently restored.");
        SelectChangeRow(3);
        SelectChangeRow(4);
        SelectChangeGroup();
        Invoke(ByName("選んだ行を除外"));
        Require(!ReadChangeStateAsync(root).GetAwaiter().GetResult(),
            "Choosing rows alone does not save an analysis.");
        Require(ByName("除外して読み込む").Current.IsEnabled,
            "The final confirmation appears before any analysis is saved.");
        Invoke(ByName("除外して読み込む"));
        Wait(() => Find("change-skipped-count")?.Current.Name.Contains("103行を除外中") == true,
            "The material details show the accepted exclusion count.");
        Require(ReadChangeStateAsync(root).GetAwaiter().GetResult(),
            "The real parser atomically saved 103 physical row IDs with only A and C.");

        Stop();
        Start(executable);
        OpenChangeDetails();
        Wait(() => Find("change-skipped-count")?.Current.Name
            .Contains("103行を除外中（3〜4行目、200〜300行目）") == true,
            "The actual row exclusion survives application restart.");
        Require(ReadChangeStateAsync(root).GetAwaiter().GetResult(),
            "Restart preserves the saved analysis and matching consent.");
    }

    private static async Task PrepareChangeRowsAsync(string root)
    {
        var preferences = new PreferencesStore(root);
        await preferences.SaveAsync((await preferences.LoadAsync()) with { DefaultSchoolYear = "2032" });
        var path = Path.Combine(root, "fictional-change-rows.xlsx");
        await File.WriteAllBytesAsync(path, FictionalChangeWorkbook.Valid());
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector());
        var coordinator = new MaterialCoordinator(store, new(new WindowsFileIdentity()));
        Require((await coordinator.SelectAsync(MaterialKind.Changes, path, 2032)).Parsed,
            "The actual file reader and parser accept the independent valid fixture.");
        await File.WriteAllBytesAsync(path, FictionalChangeWorkbook.WithWeekdayTail());
        Require(!(await coordinator.RefreshAsync(MaterialKind.Changes, 2032)).Parsed,
            "The updated invalid file retains the previous valid result.");
    }

    private static async Task<bool> ReadChangeStateAsync(string root)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector());
        var lease = await store.BeginAsync();
        var source = (await store.ReadAsync<SourceRecord>(lease, "selection.Changes"))!;
        var analysis = (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Changes"))!;
        Require(analysis.Changes!.Select(change => change.AfterSubject)
            .SequenceEqual(new[] { "架空科目A", "架空科目C" }),
            "The saved changes contain no rejected row B.");
        if (source.RowSkipConsent is null)
        {
            Require(analysis.OriginalId != source.Id && analysis.RowSkipConsent is null,
                "Without consent the previous normal result is retained.");
            return false;
        }
        return source.RowSkipConsent.Rows.SequenceEqual(new[] { 3, 4 }.Concat(Enumerable.Range(200, 101)))
            && source.Id == analysis.OriginalId
            && source.RowSkipConsent.SameAs(analysis.RowSkipConsent);
    }

    private static void OpenChangeDetails()
    {
        Navigate("settings");
        Invoke("settings-materials");
        Invoke("material-details-Changes");
        WaitElement("page-material-Changes");
    }

    private static void OpenChangePreview()
    {
        Invoke("preview-changes");
        Invoke(ByName("確認して表示"));
        WaitElement("change-skip-row-3");
    }

    private static void SelectChangeRow(int row)
    {
        var control = WaitElement("change-skip-row-" + row);
        BringControlIntoView(control);
        control = WaitElement("change-skip-row-" + row);
        Require(control.Current.IsEnabled && !Checked(control), "The actual checkbox starts unchecked.");
        // Toggle invokes the real native checkbox, including its Checked handler.
        // It does not assign a persisted choice or bypass the dialog's confirmation.
        Toggle(control);
        Wait(() => Checked(WaitElement("change-skip-row-" + row)), "One native checkbox operation selects row " + row);
    }

    private static void SelectChangeGroup()
    {
        const string id = "change-skip-group-200-300";
        var control = WaitElement(id);
        BringControlIntoView(control);
        control = WaitElement(id);
        Require(control.Current.IsEnabled && !Checked(control)
            && control.Current.Name == "この101行をまとめて除外する",
            "The real grouped checkbox represents exactly rows 200 through 300 and starts unchecked.");
        Toggle(control);
        Wait(() => Checked(WaitElement(id)), "One native checkbox operation selects the full group.");
    }

    private static System.Windows.Rect MeasuredDialogViewport(AutomationElement? scroller)
    {
        if (scroller is null) return System.Windows.Rect.Empty;
        // Use the nearest actual scrolling peer, not the outer ContentDialog
        // container which also includes the title and action area. Its bounds
        // were independently compared with the failed fictional screen capture.
        var bounds = scroller.Current.BoundingRectangle;
        if (bounds.IsEmpty) return System.Windows.Rect.Empty;
        return System.Windows.Rect.Intersect(bounds, _window!.Current.BoundingRectangle);
    }

    private static void BringControlIntoView(AutomationElement control)
    {
        var id = control.Current.AutomationId;
        if (control.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var item))
            ((ScrollItemPattern)item).ScrollIntoView();
        var scrolls = 0;
        System.Windows.Rect? previousBounds = null;
        AutomationElement? scroller = null;
        try
        {
            Wait(() =>
            {
                var current = Find(id);
                if (current is null) return false;
                var bounds = current.Current.BoundingRectangle;
                // Raw view includes native scroll peers omitted from the control view.
                var walker = TreeWalker.RawViewWalker;
                scroller = walker.GetParent(current);
                while (scroller is not null)
                {
                    if (scroller.TryGetCurrentPattern(ScrollPattern.Pattern, out var candidate)
                        && ((ScrollPattern)candidate).Current.VerticallyScrollable) break;
                    scroller = walker.GetParent(scroller);
                }
                var viewport = MeasuredDialogViewport(scroller);
                if (!bounds.IsEmpty && !viewport.IsEmpty && viewport.Contains(bounds)
                    && !current.Current.IsOffscreen) return true;
                if (bounds.IsEmpty || viewport.IsEmpty || scroller is null || scrolls >= 16) return false;
                // Observe layout movement before requesting another scroll.
                if (previousBounds == bounds) return false;
                previousBounds = bounds;
                var above = bounds.Top < viewport.Top;
                var distance = above ? viewport.Top - bounds.Top : bounds.Bottom - viewport.Bottom;
                var large = distance > viewport.Height / 2;
                var amount = above
                    ? large ? ScrollAmount.LargeDecrement : ScrollAmount.SmallDecrement
                    : large ? ScrollAmount.LargeIncrement : ScrollAmount.SmallIncrement;
                ((ScrollPattern)scroller.GetCurrentPattern(ScrollPattern.Pattern))
                    .Scroll(ScrollAmount.NoAmount, amount);
                scrolls++;
                return false;
            }, "reveal actual checkbox " + id);
        }
        catch (TimeoutException)
        {
            if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                Console.Error.WriteLine($"Synthetic checkbox {id}: bounds={Find(id)?.Current.BoundingRectangle}, " +
                    $"offscreen={Find(id)?.Current.IsOffscreen}, scroller={scroller?.Current.ControlType}, " +
                    $"viewport={MeasuredDialogViewport(scroller)}, geometry={scroller?.Current.Name}, scrolls={scrolls}");
            var ancestor = Find(id);
            for (var depth = 0; ancestor is not null && depth < 12; depth++)
            {
                Console.Error.WriteLine($"Synthetic checkbox ancestor: id={ancestor.Current.AutomationId}, " +
                    $"name={ancestor.Current.Name}, bounds={ancestor.Current.BoundingRectangle}");
                ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
                if (ancestor is not null && ancestor.GetRuntimeId().SequenceEqual(_window!.GetRuntimeId())) break;
            }
            Capture("change-checkbox-unresolved");
            throw;
        }
    }
}
