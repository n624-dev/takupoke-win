using System.Text.Json;
using Takupoke.Core;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PreferencesStoreTests
{
    [Fact]
    public async Task DefaultRemovesSavedColorAndPreservesOtherPersonalSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-preferences-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferencesStore(root);
            Assert.Equal("default", (await store.LoadAsync()).MainColor);
            await store.SaveAsync(new() { MainColor = "blue", SelectedClasses = ["3_IT"], FavoriteIds = ["fictional-link"] });
            var selected = await store.LoadAsync(); Assert.Equal("blue", selected.MainColor);
            await store.SaveAsync(selected with { MainColor = "default" });
            using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "preferences.json")));
            Assert.False(saved.RootElement.TryGetProperty("mainColor", out _));
            var restored = await store.LoadAsync(); Assert.Equal("default", restored.MainColor);
            Assert.Equal("3_IT", Assert.Single(restored.SelectedClasses)); Assert.Contains("fictional-link", restored.FavoriteIds);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
