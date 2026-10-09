using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Microsoft.Win32;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Materials;
using Takupoke.Win.Platform;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static Process? _process;
    private static AutomationElement? _window;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static int _checks;
    private static string _lastStep = "start";
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args is ["--capture", var capturedApp, var captureRoot] && Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                return CapturePages(capturedApp, captureRoot);
            if (args is ["--check-installer", var installer, var caption] && Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                return CheckInstallerDisplay(installer, caption);
            if (args is ["--shortcut", var shortcut, var target, var directory, var icon])
                return CheckShortcut(shortcut, target, directory, icon);
            if (args.Length != 2 || Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") != "1")
                throw new InvalidOperationException("An executable and isolated offline test data root are required.");
            // UI Automation returns physical pixels. Match the tested app's per-monitor context.
            if (SetThreadDpiAwarenessContext((nint)(-4)) == 0) throw new InvalidOperationException("Per-monitor coordinates could not be enabled for the test.");
            CheckApplication(args);
            Console.WriteLine($"Passed {_checks} Windows UI checks: hierarchical settings, desktop timetable geometry, raw and OS URI callbacks, fake OIDC verification and three datasets, failure/cancellation recovery, transient footer, persistence, colors, pointer and keyboard operations.");
            return 0;
        }
        catch (Exception error)
        {
            // Failure logs contain synthetic geometry only. Screen capture is
            // an explicit, isolated --capture mode with no production data.
            Console.Error.WriteLine("Windows UI check failed: " + error.GetType().Name + " — " + error.Message + " (step: " + _lastStep + ", passed: " + _checks + ")");
            if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                Console.Error.WriteLine("Synthetic native window: " + _window?.Current.BoundingRectangle);
            if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1")
                foreach (var id in new[] { "page-scroller", "status-bar", "timetable-grid-scroller", "page-timetable", "synthetic-lesson" })
                {
                    try
                    {
                        var element = id == "synthetic-lesson" ? FindLessons("架空科目甲").FirstOrDefault() : Find(id); if (element is null) continue;
                        var bounds = element.Current.BoundingRectangle;
                        Console.Error.WriteLine($"Synthetic layout {id}: automationId={element.Current.AutomationId}, focused={element.Current.HasKeyboardFocus}, offscreen={element.Current.IsOffscreen}, enabled={element.Current.IsEnabled}, bounds={bounds}");
                        if (id == "timetable-grid-scroller") Console.Error.WriteLine(element.Current.Name);
                        if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern))
                        { var scroll = ((ScrollPattern)pattern).Current; Console.Error.WriteLine($"Scroll: horizontal={scroll.HorizontalScrollPercent}, vertical={scroll.VerticalScrollPercent}, view={scroll.HorizontalViewSize}/{scroll.VerticalViewSize}"); }
                    }
                    catch (ElementNotAvailableException) { }
                }
            if (args.Length == 2 && Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1"
                && Path.GetFullPath(args[1]) == Path.GetFullPath(Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT") ?? ""))
            {
                var diagnostic = Path.Combine(args[1], "ui-error.txt");
                if (File.Exists(diagnostic)) foreach (var line in File.ReadLines(diagnostic).Take(30)) Console.Error.WriteLine(line);
                var clickProbe = Path.Combine(args[1], "offline-display-clicks.txt");
                if (File.Exists(clickProbe) && new FileInfo(clickProbe).Length <= 4096)
                {
                    foreach (var line in File.ReadLines(clickProbe).Take(12))
                        if (System.Text.RegularExpressions.Regex.IsMatch(line,
                            @"^completed=[01];selected=[01];current=[01]$"))
                            Console.Error.WriteLine("Synthetic display click: " + line);
                }
                else Console.Error.WriteLine("Synthetic display click: missing or oversized");
            }
            return 1;
        }
        finally { Stop(); }
    }
}
