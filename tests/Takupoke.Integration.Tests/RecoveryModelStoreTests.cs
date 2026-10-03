using System.Text;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;
public sealed class RecoveryModelStoreTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-model-tests-" + Guid.NewGuid().ToString("N"));
    public Task InitializeAsync() { Directory.CreateDirectory(_root); return Task.CompletedTask; }
    public Task DisposeAsync() { Directory.Delete(_root, true); return Task.CompletedTask; }
    private static byte[] Content(string version) => Encoding.UTF8.GetBytes("synthetic-model-" + version);
    private static RecoveryModelManifest Manifest(string version) => new("synthetic", version, "https://models.example.invalid/model", Content(version).Length, NotificationDiff.Digest(Content(version)), "foundryLocal", "10.0.26100", 1, "CPU", "test-only", true);
    private Task<string> Install(RecoveryModelManifest m, byte[] content, Func<string, CancellationToken, Task>? smoke = null, CancellationToken token = default) => new RecoveryModelStore(_root).InstallAsync(m, "foundryLocal", long.MaxValue, true, (_, _) => Task.FromResult<Stream>(new MemoryStream(content)), smoke ?? ((_, _) => Task.CompletedTask), token);
    [Fact] public async Task BadHashKeepsPreviousModelAndPointer() {
        var first = await Install(Manifest("1"), Content("1")); var pointer = await File.ReadAllBytesAsync(Path.Combine(_root, "active.foundryLocal.json"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Install(Manifest("2") with { Sha256 = new string('0', 64) }, Content("2")));
        Assert.True(File.Exists(first)); Assert.Equal(pointer, await File.ReadAllBytesAsync(Path.Combine(_root, "active.foundryLocal.json"))); Assert.Empty(Directory.GetFiles(_root, "staging-*"));
    }
    [Fact] public async Task SmokeFailureKeepsPreviousModel() {
        var first = await Install(Manifest("1"), Content("1"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Install(Manifest("2"), Content("2"), (_, _) => throw new InvalidDataException("synthetic failure"))); Assert.True(File.Exists(first));
    }
    [Fact] public async Task ValidUpdateSwitchesThenRemovesPrevious() {
        var first = await Install(Manifest("1"), Content("1")); var next = await Install(Manifest("2"), Content("2")); Assert.True(File.Exists(next)); Assert.False(File.Exists(first));
    }
    [Fact] public async Task UnvalidatedAndInsufficientMemoryNeverDownload() {
        var store = new RecoveryModelStore(_root); var called = false;
        Task<Stream> Download(string _, CancellationToken __) { called = true; return Task.FromResult<Stream>(new MemoryStream(Content("1"))); }
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(Manifest("1") with { Validated = false }, "foundryLocal", 10, true, Download, (_, _) => Task.CompletedTask));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(Manifest("1"), "foundryLocal", 0, true, Download, (_, _) => Task.CompletedTask)); Assert.False(called);
    }
    [Fact] public async Task CancelledUpdateDoesNotActivate() {
        var first = await Install(Manifest("1"), Content("1")); using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Install(Manifest("2"), Content("2"), token: cts.Token)); Assert.True(File.Exists(first));
    }
    [Fact] public async Task ExplicitDeleteRemovesActivePointerAndFile() {
        var first = await Install(Manifest("1"), Content("1")); await new RecoveryModelStore(_root).DeleteAsync("foundryLocal"); Assert.False(File.Exists(first)); Assert.False(File.Exists(Path.Combine(_root, "active.foundryLocal.json")));
    }
    [Fact] public async Task SameBytesVersionChangeRemovesOldAndCleanupRetainsActiveAndLeasedModels() {
        var first = await Install(Manifest("1"), Content("1")); var next = await Install(Manifest("1") with { Version = "2" }, Content("1")); Assert.False(File.Exists(first)); Assert.True(File.Exists(next));
        var abandoned = Path.Combine(_root, "staging-" + Guid.NewGuid().ToString("N")); var other = Path.Combine(_root, "unrelated.txt"); var leased = Path.Combine(_root, "foundryLocal-synthetic-99-" + new string('a', 64) + ".model");
        foreach (var path in new[] { abandoned, other, leased }) await File.WriteAllTextAsync(path, "synthetic");
        await new RecoveryModelStore(_root).CleanupAbandonedFilesAsync(new HashSet<string> { leased }); Assert.False(File.Exists(abandoned)); Assert.True(File.Exists(other)); Assert.True(File.Exists(leased)); Assert.True(File.Exists(next));
        await new RecoveryModelStore(_root).DeleteAsync("foundryLocal"); Assert.False(File.Exists(next));
    }

}
