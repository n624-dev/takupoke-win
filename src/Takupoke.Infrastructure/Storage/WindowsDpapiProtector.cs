using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Takupoke.Infrastructure.Storage;

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiProtector : IKeyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("jp.n624.takupoke.win.school-data.v1");
    public byte[] Protect(byte[] key) => ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] wrapped) => ProtectedData.Unprotect(wrapped, Entropy, DataProtectionScope.CurrentUser);
}
