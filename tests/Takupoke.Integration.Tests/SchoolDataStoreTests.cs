using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Takupoke.Core;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class SchoolDataStoreTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-store-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestProtector _protector = new();
    private readonly Clock _clock = new();
    private SchoolDataStore Store() => new(_root, _protector, _clock);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2032, 9, 30, 23, 59, 0, TimeSpan.FromHours(9));
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
    private sealed class TestProtector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "test-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "test-key");
        public void Dispose() => _cipher.Dispose();
    }
    public Task InitializeAsync() { Directory.CreateDirectory(_root); return Task.CompletedTask; }
    public Task DisposeAsync() { _protector.Dispose(); Directory.Delete(_root, recursive: true); return Task.CompletedTask; }
    [Fact]
    public async Task SchoolPayloadIsEncryptedAndSurvivesRestart()
    {
        await using (var store = Store())
        {
            var lease = await store.BeginAsync();
            await store.WriteAsync(lease, "test", "架空学校データA");
            var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "school", "school.sqlite"));
            Assert.DoesNotContain("架空学校データA", Encoding.UTF8.GetString(bytes));
        }
        await using var reopened = Store();
        Assert.Equal("架空学校データA", await reopened.ReadAsync<string>(await reopened.BeginAsync(), "test"));
    }
    [Fact]
    public async Task PeriodRolloverClearsSchoolDataPreservesPreferencesAndRejectsOldLease()
    {
        await using var store = Store();
        var preferences = new PreferencesStore(_root);
        await preferences.SaveAsync(new() { FavoriteIds = ["fake-link"], SelectedClasses = ["1_CN"] });
        var old = await store.BeginAsync();
        await store.WriteAsync(old, "test", "架空学校データA");
        _clock.Now = _clock.Now.AddMinutes(1);
        var current = await store.BeginAsync();
        Assert.Null(await store.ReadAsync<string>(current, "test"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.WriteAsync(old, "test", "架空古いデータB"));
        Assert.Contains("fake-link", (await preferences.LoadAsync()).FavoriteIds);
        Assert.Equal(new[] { "1_CN" }, (await preferences.LoadAsync()).SelectedClasses);
    }
    [Fact]
    public async Task LockPreventsReadsAndOldOperationsCannotResumeAfterUnlock()
    {
        await using var store = Store();
        var old = await store.BeginAsync();
        await store.WriteAsync(old, "test", "架空学校データA");
        await store.SetProtectedDataAvailableAsync(false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.BeginAsync());
        await store.SetProtectedDataAvailableAsync(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.ReadAsync<string>(old, "test"));
        Assert.Equal("架空学校データA", await store.ReadAsync<string>(await store.BeginAsync(), "test"));
    }
    [Fact]
    public async Task ParseFailureKeepsNewSelectionAndLastSuccessfulAnalysisWithItsOwnOriginal()
    {
        await using var store = Store();
        var lease = await store.BeginAsync();
        var first = Source("first", "%PDF-fake-A"u8.ToArray());
        await store.SaveOriginalAsync(lease, first, "%PDF-fake-A"u8.ToArray());
        var accepted = new MaterialAnalysis(first.Id, MaterialKind.Timetable, 1, first.Digest, first.OriginalName, _clock.Now, 2032,
            new(2032, "前期", [new("1_CN", 1, 1, new("架空科目A"), "架空原文A", 1)]));
        await store.SaveAnalysisAsync(lease, accepted);
        var second = Source("second", "%PDF-fake-B"u8.ToArray());
        await store.SaveOriginalAsync(lease, second, "%PDF-fake-B"u8.ToArray());
        await store.WriteAsync(lease, "attempt.Timetable", new MaterialAttempt(_clock.Now, "P01", true));
        await store.CollectOriginalsAsync(lease);
        Assert.Equal(second.Id, (await store.ReadAsync<SourceRecord>(lease, "selection.Timetable"))!.Id);
        Assert.Equal(first.Id, (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Timetable"))!.OriginalId);
        Assert.Equal("%PDF-fake-A"u8.ToArray(), await store.ReadOriginalAsync(lease, first.Id));
        Assert.Equal("%PDF-fake-B"u8.ToArray(), await store.ReadOriginalAsync(lease, second.Id));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAnalysisAsync(lease, accepted));
    }
    [Fact]
    public async Task CorruptDatabaseDoesNotGetReinitializedWithinCurrentPeriod()
    {
        await using var store = Store();
        var lease = await store.BeginAsync();
        await File.WriteAllTextAsync(Path.Combine(_root, "school", "school.sqlite"), "fake damaged database");
        await Assert.ThrowsAnyAsync<Exception>(() => store.ReadAsync<string>(lease, "test"));
        Assert.Equal("fake damaged database", await File.ReadAllTextAsync(Path.Combine(_root, "school", "school.sqlite")));
    }
    [Fact]
    public void CipherRejectsTamperingAndDifferentRecordPurpose()
    {
        using var cipher = new EnvelopeCipher(RandomNumberGenerator.GetBytes(32));
        var encrypted = cipher.Encrypt("架空学校データA"u8.ToArray(), "record-a");
        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(encrypted, "record-b"));
        encrypted[^1] ^= 1;
        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(encrypted, "record-a"));
    }
    private SourceRecord Source(string id, byte[] bytes) => new(id, MaterialKind.Timetable, Path.Combine(_root, "fake.pdf"), "fake-identity", "架空資料A.pdf",
        NotificationDiff.Digest(bytes), bytes.Length, _clock.Now, _clock.Now, null);
}
