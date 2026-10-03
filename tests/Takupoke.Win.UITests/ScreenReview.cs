using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Automation;
using Microsoft.Win32;
using Takupoke.Core;
using Takupoke.Infrastructure.Storage;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Parsing;
using System.Text;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static bool TableFitsContentHeight()
    {
        var probe = WaitElement("timetable-grid-scroller").Current.Name;
        double Value(string key)
        {
            var match = System.Text.RegularExpressions.Regex.Match(probe, @"; " + key + @"=([^;]+);");
            return match.Success ? double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : double.NaN;
        }
        return Value("contentHeight") > 0 && Math.Abs(Value("tableHeight") - Value("contentHeight")) <= 2;
    }

    private static int CapturePages(string executable, string root)
    {
        if (Path.GetFullPath(root) != Path.GetFullPath(Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT") ?? ""))
            throw new InvalidOperationException("Captures require the isolated synthetic app root.");
        if (SetThreadDpiAwarenessContext((nint)(-4)) == 0) throw new InvalidOperationException("Cannot enable physical screenshot coordinates.");
        using var accessibility = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility");
        var previousScale = accessibility.GetValue("TextScaleFactor");
        var previousKind = previousScale is null ? RegistryValueKind.DWord : accessibility.GetValueKind("TextScaleFactor");
        try
        {
        SetTextScale(accessibility, 100);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            Environment.SetEnvironmentVariable("TAKUPOKE_TEST_THEME", theme);
            SeedAsync(root).GetAwaiter().GetResult();
            SeedReviewDetails(root).GetAwaiter().GetResult();
            new PreferencesStore(root).SaveAsync(new UserPreferences { SelectedClasses = ["3_IT"], MainColor = "default", SetupCompleted = true }).GetAwaiter().GetResult();
            Start(executable);
            try
            {
                Capture(theme + "-home");
                Navigate("links"); Capture(theme + "-links");
                Invoke(WaitElement("link-menu-fake-study")); Capture(theme + "-link-menu");
                // Invoke an action that safely closes the menu without visiting a URL.
                Invoke(ByName("お気に入りに追加", ControlType.MenuItem));
                Navigate("timetable"); Capture(theme + "-timetable");
                Invoke(WaitElement("timetable-classes")); Capture(theme + "-class-picker"); Invoke(ByName("キャンセル"));
                Navigate("settings"); Capture(theme + "-settings");
                foreach (var child in new[] { "materials", "account", "events", "notifications", "about", "help", "setup" })
                {
                    Navigate("settings"); Invoke(WaitElement("settings-" + child));
                    Wait(() => Find("page-" + child) is not null, "capture " + child); Capture(theme + "-" + child);
                }
                Navigate("settings"); Invoke(WaitElement("settings-materials")); Invoke(WaitElement("material-details-Timetable"));
                Wait(() => Find("page-material-Timetable") is not null, "capture material details"); Capture(theme + "-material-details");
                Invoke(WaitElement("analysis-Timetable")); Wait(() => Find("page-analysis-Timetable") is not null, "capture analysis"); Capture(theme + "-analysis");
                Navigate("settings");
                var window = _window!.Current.BoundingRectangle;
                Require(SetWindowPos(_process!.MainWindowHandle, 0, (int)window.Left, (int)window.Top, 680, 680, 0x0044), "Resize the review window");
                Capture(theme + "-settings-narrow"); Navigate("timetable"); Capture(theme + "-timetable-narrow");
            }
            finally { Stop(); }
        }
        foreach (var (theme, percent) in new[] { ("Light", 200), ("Dark", 150) })
        {
            SetTextScale(accessibility, percent);
            Environment.SetEnvironmentVariable("TAKUPOKE_TEST_THEME", theme);
            Start(executable);
            try
            {
                Navigate("timetable");
                Wait(() =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(WaitElement("timetable-grid-scroller").Current.Name, @"; textScale=([^;]+);");
                    return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var actual) && Math.Abs(actual - percent / 100.0) < 0.01;
                }, "The app receives the real Windows text scale " + percent);
                Capture(theme + "-" + percent + "-timetable");
                var clock = WaitElement("timetable-clock-label-1").Current.BoundingRectangle;
                var cell = WaitElement("timetable-time-1").Current.BoundingRectangle;
                Require(clock.Left >= cell.Left + 11 && clock.Right <= cell.Right - 11 && clock.Bottom <= cell.Bottom - 9,
                    "Timetable clocks retain padding at enlarged Windows text sizes");
                Navigate("settings"); Capture(theme + "-" + percent + "-settings");
                Invoke(WaitElement("settings-materials")); Capture(theme + "-" + percent + "-materials");
                var window = _window!.Current.BoundingRectangle;
                Require(SetWindowPos(_process!.MainWindowHandle, 0, (int)window.Left, (int)window.Top, 680, 680, 0x0044), "Resize the enlarged review window");
                Navigate("settings"); Capture(theme + "-" + percent + "-settings-narrow");
                Navigate("timetable"); Capture(theme + "-" + percent + "-timetable-narrow");
            }
            finally { Stop(); }
        }
        }
        finally
        {
            if (previousScale is null) accessibility.DeleteValue("TextScaleFactor", false);
            else accessibility.SetValue("TextScaleFactor", previousScale, previousKind);
            BroadcastTextScale();
            Environment.SetEnvironmentVariable("TAKUPOKE_TEST_THEME", null);
        }
        return 0;
    }

    private static void SetTextScale(RegistryKey key, int value)
    { key.SetValue("TextScaleFactor", value, RegistryValueKind.DWord); BroadcastTextScale(); }
    private static void BroadcastTextScale() => SendMessageTimeout((nint)0xffff, 0x001a, 0, "Accessibility", 0x0002, 1000, out _);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint parameter, string value, uint flags, uint timeout, out nuint result);

    private static async Task SeedReviewDetails(string root)
    {
        await using var store = new SchoolDataStore(root, new WindowsDpapiProtector());
        var lease = await store.BeginAsync(); var now = DateTimeOffset.UtcNow;
        // Review screenshots show complete, entirely fictional input states.
        // Main regression tests retain their independent missing-source fixtures.
        var bytes = Encoding.UTF8.GetBytes("Entirely fictional accepted change-list fixture.");
        var source = new SourceRecord(Guid.NewGuid().ToString("N"), MaterialKind.Changes, Path.Combine(root, "fake-unavailable-changes.xlsx"),
            "fake-changes-identity", "架空の時間割変更.xlsx", NotificationDiff.Digest(bytes), bytes.Length, now, now, now);
        await store.SaveOriginalAsync(lease, source, bytes);
        await store.SaveAnalysisAsync(lease, new(source.Id, source.Kind, XlsxChangeReader.Version, source.Digest, source.OriginalName, now, lease.Period.SchoolYear, Changes: [new(SchoolDate.InJapan(now).DisplayWeekStart().ToString("yyyy-MM-dd"), "3_IT", "1~2", "", "架空の実習科目（長い名称の折り返し確認）", "架空の担当教員", "架空の実習室", "補講", "完全に架空の授業変更", "架空の授業変更")]));
        await new PublicEventsStore(root).SaveAsync(new(now, new("v1", lease.Period.SchoolYear, new string('a', 64), null, [new($"{lease.Period.SchoolYear + 1}-03-10", $"{lease.Period.SchoolYear + 1}-03-10", "架空の行事メモ", "行事メモ")]), "\"fake-api-etag\""));
    }

    private static void Capture(string name)
    {
        // The mode is reachable only through the offline fixture command. Capture
        // only our own visible app window; never the surrounding desktop or pickers.
        if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") != "1" || _process is null || _window is null)
            throw new InvalidOperationException("Synthetic screenshots must remain isolated.");
        Thread.Sleep(250);
        var bounds = _window.Current.BoundingRectangle;
        if (!SystemParametersInfo(0x0030, 0, out var work, 0) || bounds.Left < work.Left || bounds.Top < work.Top
            || bounds.Right > work.Right + 1 || bounds.Bottom > work.Bottom + 1)
            throw new InvalidOperationException("The app is outside the review desktop.");
        using var image = new Bitmap((int)bounds.Width, (int)bounds.Height);
        using (var graphics = Graphics.FromImage(image))
            graphics.CopyFromScreen((int)bounds.Left, (int)bounds.Top, 0, 0, image.Size, CopyPixelOperation.SourceCopy);
        using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png);
        var bytes = stream.ToArray(); var encoded = Convert.ToBase64String(bytes);
        Console.WriteLine($"TAKUPOKE_UI_IMAGE BEGIN {name} {Convert.ToHexStringLower(SHA256.HashData(bytes))} {bytes.Length}");
        const int chunkLength = 6000;
        var chunks = (encoded.Length + chunkLength - 1) / chunkLength;
        for (var index = 0; index < chunks; index++)
            Console.WriteLine($"TAKUPOKE_UI_IMAGE DATA {name} {index} {encoded.Substring(index * chunkLength, Math.Min(chunkLength, encoded.Length - index * chunkLength))}");
        Console.WriteLine($"TAKUPOKE_UI_IMAGE END {name} {chunks}");
    }
}
