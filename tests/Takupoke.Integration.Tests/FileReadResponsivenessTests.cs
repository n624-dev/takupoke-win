using Takupoke.Core;
using Takupoke.Infrastructure.Materials;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class FileReadResponsivenessTests
{
    private sealed class BlockingIdentity : IFileIdentityProvider, IDisposable
    {
        public readonly ManualResetEventSlim Entered = new(false), Continue = new(false);
        public string Identity(FileStream stream)
        {
            Entered.Set();
            if (!Continue.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Synthetic file-provider timeout.");
            return "fictional-file-identity";
        }
        public void Dispose() { Entered.Dispose(); Continue.Dispose(); }
    }
    [Fact]
    public async Task OpeningAFileProviderCannotBlockTheCallingUiThread()
    {
        var directory = Path.Combine(Path.GetTempPath(), "takupoke-fake-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var identity = new BlockingIdentity(); using var returned = new ManualResetEventSlim(false);
        Task<SourceContent>? operation = null; Exception? startFailure = null;
        Thread? ui = null;
        try
        {
            var path = Path.Combine(directory, "fictional.pdf"); await File.WriteAllTextAsync(path, "%PDF-1.7\n");
            var reader = new FileSourceReader(identity);
            ui = new Thread(() => { try { operation = reader.ReadAsync(path, MaterialKind.Timetable, null); } catch (Exception error) { startFailure = error; } finally { returned.Set(); } }) { IsBackground = true };
            ui.Start();
            Assert.True(identity.Entered.Wait(TimeSpan.FromSeconds(5)), "The synthetic file provider was not called.");
            Assert.True(returned.Wait(TimeSpan.FromSeconds(5)), "Opening the file blocked the caller while the provider was waiting.");
            Assert.Null(startFailure); Assert.NotNull(operation);
        }
        finally
        {
            identity.Continue.Set(); ui?.Join(TimeSpan.FromSeconds(5));
            try { if (operation is not null) using (await operation) { } }
            finally { Directory.Delete(directory, true); }
        }
    }
}
