using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Takupoke.Core;

/// <summary>Rules shared by downloaded links, launchers and WebView navigation.</summary>
public static class BrowserNavigationPolicy
{
    public static bool IsWebUrl(string value) => TryWebUri(value, out _);
    public static bool TryWebUri(string value, out Uri? uri)
    {
        uri = null;
        if (Encoding.UTF8.GetByteCount(value) > 2048 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.UserInfo.Length != 0 || parsed.Host.Length == 0
            || parsed.Scheme != "https" && parsed.Scheme != "http" || !value.StartsWith(parsed.Scheme + "://", StringComparison.OrdinalIgnoreCase)
            || parsed.Scheme == "http" && !IsLocalHost(parsed)) return false;
        uri = parsed; return true;
    }
    public static bool IsLocalHost(Uri uri)
    {
        var host = uri.IdnHost.Trim('[', ']').TrimEnd('.');
        if (IPAddress.TryParse(host, out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address)) return true;
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                    || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 169 && bytes[1] == 254;
            return address.AddressFamily == AddressFamily.InterNetworkV6 && ((bytes[0] & 0xfe) == 0xfc || address.IsIPv6LinkLocal);
        }
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase);
    }
    public static bool MayNavigate(Uri opened, Uri? current, string target)
    {
        if (!TryWebUri(target, out var destination)) return false;
        if (!IsLocalHost(destination!)) return true;
        // A remote page cannot turn the embedded browser into a LAN navigator.
        // A local site may use its own ports/HTTP→HTTPS, but not another host.
        return IsLocalHost(opened) && SameHost(opened, destination!)
            && (current is null || SameHost(current, opened));
    }
    private static bool SameHost(Uri first, Uri second) =>
        first.IdnHost.TrimEnd('.').Equals(second.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    public static string NavigationFailure(string status) => status switch
    {
        "HostNameNotResolved" => "接続先の名前を解決できませんでした。学内ネットワーク・DNS・ホスト名を確認してください。",
        "CannotConnect" or "ConnectionAborted" or "ConnectionReset" or "Disconnected" => "接続できませんでした。接続先の起動、ポートとネットワークを確認してください。",
        "Timeout" => "接続が時間内に完了しませんでした。ネットワークと接続先を確認してください。",
        "CertificateCommonNameIsIncorrect" => "証明書の名前と接続先が一致しません。管理者から案内された正しいホスト名を使用してください。",
        "CertificateExpired" => "接続先の証明書の有効期限を確認できません。端末の日時と管理者の証明書更新を確認してください。",
        "CertificateIsInvalid" or "CertificateRevoked" or "CertificateContainsErrors" => "接続先の証明書を信頼できません。証明書検証は省略しません。管理者に証明書と信頼設定を確認してください。",
        "OperationCanceled" => "ページの読み込みが中止されました。",
        _ => "ページを表示できませんでした。ネットワークと接続先を確認してください。"
    };
}
