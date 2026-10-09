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
    private static string ReadProbe(string path)
    { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var text = new StreamReader(file); return text.ReadToEnd(); }
    private static void WriteProbe(string path, string value)
    { File.WriteAllText(path + ".tmp", value); File.Move(path + ".tmp", path, true); }
    private static void CheckAuthentication(string executable, string root)
    {
        var mode = Path.Combine(root, "offline-auth-mode.txt"); var state = Path.Combine(root, "offline-auth-state.txt");
        var tokenRequests = Path.Combine(root, "offline-token-requests.txt"); var privateRequests = Path.Combine(root, "offline-private-requests.txt");
        const string commandPath = @"Software\Classes\jp.n624.takupoke.win\shell\open\command";
        using var existing = Registry.CurrentUser.OpenSubKey(commandPath);
        var previous = existing?.GetValue("") as string;
        using (var command = Registry.CurrentUser.CreateSubKey(commandPath)) command.SetValue("", "\"" + Path.GetFullPath(executable) + "\" \"----ms-protocol:%1\"");
        using (var scheme = Registry.CurrentUser.CreateSubKey(@"Software\Classes\jp.n624.takupoke.win")) scheme.SetValue("URL Protocol", "");
        try
        {
            File.Delete(tokenRequests); File.Delete(privateRequests);
            Invoke("settings-account");
            Wait(() => Find("page-account") is not null, "account child screen");
            WriteProbe(mode, "fail"); File.Delete(state);
            Invoke("update-account");
            Wait(() => File.Exists(state) && Visible("cancel-operation"), "authentication waits for an OS callback");
            var attemptState = ReadProbe(state);
            Require(Find("shared-details-Links")?.Current.IsEnabled == true, "Saved data details remain available while authenticating.");
            Invoke("shared-details-Links"); Invoke(ByName("閉じる"));
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=forged", shell: false);
            Require(Visible("cancel-operation") && !File.Exists(tokenRequests), "An unmatched callback neither completes authentication nor exchanges a token.");
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=" + attemptState, shell: false);
            Wait(() => Find("update-account")?.Current.IsEnabled == true && !Visible("cancel-operation"), "Legacy raw-URI launch ends authentication after token failure");
            Require(ReadProbe(tokenRequests) == "1" && !File.Exists(privateRequests), "Token failure cannot download school data.");
            Wait(() => _window!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "学校アカウントの認証を完了できませんでした。")) is not null, "authentication failure is visible on the account screen");
            Wait(() => !Visible("status-bar"), "failure footer disappears after a short interval");
            Require(Find("account-update-error")?.Current.Name == "学校アカウントの認証を完了できませんでした。", "The account failure remains available after the transient footer disappears.");
            Wait(() => Find("shared-details-Links")?.Current.IsEnabled == true, "The account screen stays usable after the footer disappears.");
            WriteProbe(mode, "hold"); File.Delete(state);
            Invoke("update-account");
            Wait(() => File.Exists(state) && Visible("cancel-operation"), "retry creates a fresh authentication attempt");
            attemptState = ReadProbe(state);
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=" + attemptState, shell: true);
            Wait(() => ReadProbe(tokenRequests) == "2", "Windows protocol launch reaches token exchange");
            Require(Find("operation-status")?.Current.Name.Contains("認証情報を確認", StringComparison.Ordinal) == true, "Progress identifies token verification rather than a stale refresh result.");
            Invoke("back-settings");
            Wait(() => Find("page-settings") is not null && Find("settings-materials")?.Current.IsEnabled == true && Find("settings-help")?.Current.IsEnabled == true, "Settings navigation stays usable during token exchange");
            Require(SavedMainColor(Path.Combine(root, "preferences.json")) == "purple",
                "The rapid color-return scenario starts from the previously saved purple.");
            SelectMainColor("green", Path.Combine(root, "preferences.json"));
            SelectMainColor("purple", Path.Combine(root, "preferences.json"));
            Wait(() => ReadProbe(tokenRequests) == "2" && Visible("cancel-operation")
                && Find("operation-status")?.Current.Name.Contains("認証情報を確認", StringComparison.Ordinal) == true,
                "the pending token exchange is reflected after preference rendering");
            var pendingCancel = Find("cancel-operation");
            Console.WriteLine($"Synthetic pending exchange: requests={ReadProbe(tokenRequests)}, "
                + $"cancelExists={pendingCancel is not null}, cancelOffscreen={pendingCancel?.Current.IsOffscreen}");
            Require(ReadProbe(tokenRequests) == "2" && Visible("cancel-operation"), "Local preferences save without completing or canceling the pending token exchange.");
            Invoke("settings-help"); Wait(() => Find("page-help") is not null, "help is readable during token exchange");
            Invoke("back-settings"); Invoke("settings-account");
            WriteProbe(mode, "success");
            Wait(() => Find("update-account")?.Current.IsEnabled == true && !Visible("cancel-operation"), "Validated fake authentication completes downloads and releases the UI");
            Require(ReadProbe(privateRequests) == "3", "All three datasets require the verified token and download once.");
            foreach (var kind in Enum.GetValues<DataSet>()) Require(Find("shared-status-" + kind)?.Current.Name == "取得済み", "Each independently saved dataset shows acquired status.");
            Wait(() => !Visible("status-bar"), "success footer automatically hides");
            WriteProbe(Path.Combine(root, "offline-auth-revision.txt"), new string('C', 43));
            WriteProbe(mode, "hold"); File.Delete(state);
            Invoke("update-account"); Wait(() => File.Exists(state) && Visible("cancel-operation"), "another update is cancellable");
            SendCallback(executable, OidcClient.RedirectUri + "?code=fake-code&state=" + ReadProbe(state), shell: true);
            Wait(() => ReadProbe(tokenRequests) == "3", "cancellation test reaches token exchange");
            Invoke("cancel-operation");
            Wait(() => Find("update-account")?.Current.IsEnabled == true && !Visible("cancel-operation"), "canceling token exchange releases the UI");
            Require(ReadProbe(privateRequests) == "3", "Cancellation preserves previous data and starts no private downloads.");
            foreach (var kind in Enum.GetValues<DataSet>()) Require(Find("shared-status-" + kind)?.Current.Name == "取得済み", "Canceled authentication retains all prior datasets.");
            if (Visible("dismiss-status")) Invoke("dismiss-status");
            Require(!Visible("status-bar"), "The result footer can be dismissed immediately.");
            Invoke("back-settings");
        }
        finally
        {
            File.Delete(mode); File.Delete(state); File.Delete(Path.Combine(root, "offline-auth-revision.txt"));
            if (previous is not null) { using var command = Registry.CurrentUser.CreateSubKey(commandPath); command.SetValue("", previous); }
            else Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\jp.n624.takupoke.win", false);
        }
    }
    private static void SendCallback(string executable, string callback, bool shell)
    {
        var info = shell ? new ProcessStartInfo(callback) { UseShellExecute = true } : new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false };
        if (!shell) info.ArgumentList.Add(callback);
        using var redirect = Process.Start(info);
        if (!shell && (redirect is null || !redirect.WaitForExit(10000) || redirect.ExitCode != 0)) throw new InvalidOperationException("The synthetic callback process did not redirect successfully.");
    }
}
