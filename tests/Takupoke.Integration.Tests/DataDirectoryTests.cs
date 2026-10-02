using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class DataDirectoryTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "takupoke-directory-tests-" + Guid.NewGuid().ToString("N"));
    private string Previous => Path.Combine(_parent, "TakupokeWin");
    private string Current => Path.Combine(_parent, "takupoke");
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2032, 6, 1, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class TestProtector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "test-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "test-key");
        public void Dispose() => _cipher.Dispose();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationPreservesKeysEncryptedDataOriginalsAndPreferences(bool emptyDestinationExists)
    {
        using var protector = new TestProtector();
        var clock = new Clock();
        var original = "%PDF-fake-school-document"u8.ToArray();
        await new PreferencesStore(Previous).SaveAsync(new() { SelectedClasses = ["1_CN"], FavoriteIds = ["fake-link"] });
        await using (var store = new SchoolDataStore(Previous, protector, clock))
        {
            var lease = await store.BeginAsync();
            await store.WriteAsync(lease, "test", "架空学校データ");
            var source = new SourceRecord("fake-original", MaterialKind.Timetable, "fake.pdf", "fake-identity", "架空資料.pdf",
                NotificationDiff.Digest(original), original.Length, clock.GetUtcNow(), clock.GetUtcNow(), null);
            await store.SaveOriginalAsync(lease, source, original);
        }
        var files = Directory.GetFiles(Previous, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Previous, path), File.ReadAllBytes);
        if (emptyDestinationExists) Directory.CreateDirectory(Current);
        var result = DataDirectory.Resolve(_parent);
        Assert.Equal(Current, result.Root);
        Assert.Null(result.Message);
        Assert.False(Directory.Exists(Previous));
        foreach (var file in files) Assert.Equal(file.Value, await File.ReadAllBytesAsync(Path.Combine(Current, file.Key)));
        await using var reopened = new SchoolDataStore(result.Root, protector, clock);
        var currentLease = await reopened.BeginAsync();
        Assert.Equal("架空学校データ", await reopened.ReadAsync<string>(currentLease, "test"));
        Assert.Equal(original, await reopened.ReadOriginalAsync(currentLease, "fake-original"));
        var preferences = await new PreferencesStore(result.Root).LoadAsync();
        Assert.Equal("1_CN", Assert.Single(preferences.SelectedClasses));
        Assert.Contains("fake-link", preferences.FavoriteIds);
    }
    [Fact]
    public void ExistingDirectoriesAreNeverMergedOrOverwritten()
    {
        Directory.CreateDirectory(Previous); Directory.CreateDirectory(Current);
        File.WriteAllBytes(Path.Combine(Previous, "preferences.json"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(Current, "preferences.json"), [4, 5, 6]);
        var result = DataDirectory.Resolve(_parent);
        Assert.Equal(Previous, result.Root);
        Assert.NotNull(result.Message);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Previous, "preferences.json")));
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(Path.Combine(Current, "preferences.json")));
    }
    [Fact]
    public void FailedMoveKeepsExistingDataUsableAndDoesNotOverwriteDestination()
    {
        Directory.CreateDirectory(Previous);
        File.WriteAllText(Path.Combine(Previous, "preferences.json"), "fake old preferences");
        File.WriteAllText(Current, "fake destination obstruction");
        var result = DataDirectory.Resolve(_parent);
        Assert.Equal(Previous, result.Root);
        Assert.NotNull(result.Message);
        Assert.Equal("fake old preferences", File.ReadAllText(Path.Combine(result.Root, "preferences.json")));
        Assert.Equal("fake destination obstruction", File.ReadAllText(Current));
    }
    [Fact]
    public void EmptyLegacyDirectoryDoesNotHideCurrentData()
    {
        Directory.CreateDirectory(Previous); Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Current, "preferences.json"), "fake current preferences");
        var result = DataDirectory.Resolve(_parent);
        Assert.Equal(Current, result.Root);
        Assert.Null(result.Message);
        Assert.Equal("fake current preferences", File.ReadAllText(Path.Combine(result.Root, "preferences.json")));
    }
    public void Dispose() { if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true); }
}
