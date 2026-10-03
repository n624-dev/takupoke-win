using System.Text.RegularExpressions;

namespace Takupoke.Core.Recovery;
public sealed record RecoveryModelManifest(string ModelId, string Version, string Url, long Size, string Sha256,
    string Runtime, string MinimumOs, long MinimumMemory, string RecommendedBackend, string License, bool Validated)
{
    public string StorageKey => !ModelId.Contains(':') ? ModelId : Regex.Replace(ModelId, "[^A-Za-z0-9._-]", "_") + "-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ModelId)))[..12];
    public bool IsUsable(string runtime, long availableMemory) => Validated && runtime is ("coreAI" or "llamaCpp" or "liteRtLm" or "foundryLocal") && Runtime == runtime && MinimumMemory > 0 && availableMemory >= MinimumMemory
        && Size is > 0 and <= 8L * 1024 * 1024 * 1024 && Regex.IsMatch(ModelId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,80}(?::[0-9]{1,8})?$")
        && Regex.IsMatch(Version, "^[A-Za-z0-9][A-Za-z0-9._-]{0,80}$") && Regex.IsMatch(Sha256, "^[a-f0-9]{64}$")
        && Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0
        && Regex.IsMatch(MinimumOs, @"^[0-9]{1,6}(?:\.[0-9]{1,6}){0,3}$") && (Runtime == "coreAI" ? new[] { "CPU", "GPU", "ANE" } : Runtime == "llamaCpp" ? ["CPU", "Metal"] : ["CPU", "GPU", "NPU"]).Contains(RecommendedBackend) && !string.IsNullOrWhiteSpace(License);
    public bool SupportsOs(string current)
    {
        if (!Regex.IsMatch(current, @"^[0-9]{1,6}(?:\.[0-9]{1,6}){0,3}$") || !Regex.IsMatch(MinimumOs, @"^[0-9]{1,6}(?:\.[0-9]{1,6}){0,3}$")) return false;
        var actual = current.Split('.').Select(int.Parse).ToArray(); var needed = MinimumOs.Split('.').Select(int.Parse).ToArray();
        for (var i = 0; i < 4; i++) { var a = actual.ElementAtOrDefault(i); var b = needed.ElementAtOrDefault(i); if (a != b) return a > b; }
        return true;
    }
}
