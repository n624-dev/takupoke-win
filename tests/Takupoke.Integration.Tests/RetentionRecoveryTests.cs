using System.Security.Cryptography;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RetentionRecoveryTests
{
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public bool Fail { get; set; }
        public byte[] Protect(byte[] bytes) => Fail ? throw new IOException("Fictional key write failure") : _cipher.Encrypt(bytes, "fictional-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fictional-key");
        public void Dispose() => _cipher.Dispose();
    }
    [Theory]
    [InlineData("{fictional-invalid")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":1,\"schoolYear\":2032,\"half\":3}")]
    public async Task CorruptMarkersBlockExistingLeasesAndExplicitRecoveryPreservesPublicAndPersonalFiles(string invalid)
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-retention-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var protector = new Protector();
        try
        {
            await using var store = new SchoolDataStore(root, protector);
            var lease = await store.BeginAsync(); await store.WriteAsync(lease, "fictional", "架空学校データA");
            var marker = Path.Combine(root, "school-data-period.json");
            await File.WriteAllTextAsync(marker, invalid);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "school", "school.sqlite"));
            foreach (var name in new[] { "preferences.json", "events.json", "fictional-original.pdf" }) await File.WriteAllTextAsync(Path.Combine(root, name), "fictional preserved bytes");
            var unavailable = 0; store.RetentionUnavailable += canReset => { Assert.True(canReset); unavailable++; };
            for (var i = 0; i < 3; i++) Assert.True((await Assert.ThrowsAsync<SchoolDataStore.RetentionStateException>(() => store.BeginAsync())).CanRecover);
            Assert.Equal(3, unavailable); Assert.Equal(invalid, await File.ReadAllTextAsync(marker));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, "school", "school.sqlite")));
            await store.RecoverRetentionAsync();
            var fresh = await store.BeginAsync(); Assert.Null(await store.ReadAsync<string>(fresh, "fictional"));
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.ReadAsync<string>(lease, "fictional"));
            foreach (var name in new[] { "preferences.json", "events.json", "fictional-original.pdf" }) Assert.Equal("fictional preserved bytes", await File.ReadAllTextAsync(Path.Combine(root, name)));
            Assert.False(Directory.Exists(Path.Combine(root, "school-data-period-recovery")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task FailedResetKeepsQuarantinedMarkerAndCanResumeAfterRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-retention-failure-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var protector = new Protector();
        try
        {
            await using (var store = new SchoolDataStore(root, protector))
            {
                _ = await store.BeginAsync(); await File.WriteAllTextAsync(Path.Combine(root, "school-data-period.json"), "fictional-invalid");
                protector.Fail = true;
                await Assert.ThrowsAsync<IOException>(() => store.RecoverRetentionAsync());
                var backup = Assert.Single(Directory.GetFiles(Path.Combine(root, "school-data-period-recovery")));
                Assert.Equal("fictional-invalid", await File.ReadAllTextAsync(backup));
                Assert.False(File.Exists(Path.Combine(root, "school-data-period.json")));
            }
            protector.Fail = false;
            await using var reopened = new SchoolDataStore(root, protector); _ = await reopened.BeginAsync();
            Assert.False(Directory.Exists(Path.Combine(root, "school-data-period-recovery")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task NormalMarkersLockedStateAndUnreadableMarkersCannotUseTheResetPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-retention-guard-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var protector = new Protector();
        try
        {
            await using var store = new SchoolDataStore(root, protector); var lease = await store.BeginAsync();
            await store.WriteAsync(lease, "fictional", "架空学校データA");
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecoverRetentionAsync());
            await store.SetProtectedDataAvailableAsync(false);
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.RecoverRetentionAsync());
            await store.SetProtectedDataAvailableAsync(true);
            var marker = Path.Combine(root, "school-data-period.json"); File.Delete(marker); Directory.CreateDirectory(marker);
            Assert.False((await Assert.ThrowsAsync<SchoolDataStore.RetentionStateException>(() => store.BeginAsync())).CanRecover);
            Assert.False((await Assert.ThrowsAsync<SchoolDataStore.RetentionStateException>(() => store.RecoverRetentionAsync())).CanRecover);
            Assert.True(File.Exists(Path.Combine(root, "school", "school.sqlite")));
        }
        finally { Directory.Delete(root, true); }
    }
}
