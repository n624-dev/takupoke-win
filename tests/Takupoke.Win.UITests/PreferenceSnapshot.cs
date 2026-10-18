using System.Diagnostics;
using System.IO;

namespace Takupoke.Win.UITests;

// Inspect the saved value without preventing the app's atomic replacement.
// A failed read never supplies a default value that could satisfy a predicate.
internal static class PreferenceSnapshot
{
    internal static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);

    internal static byte[] ReadBytes(string path, Action<IOException>? onRetry = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using var file = Open(path); using var bytes = new MemoryStream();
                file.CopyTo(bytes); return bytes.ToArray();
            }
            catch (IOException error) when (elapsed.Elapsed < TimeSpan.FromSeconds(1) &&
                (error is FileNotFoundException || (error.HResult & 0xffff) is 32 or 33 ||
                    !OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 11))
            { onRetry?.Invoke(error); Thread.Sleep(20); }
        }
    }
}
