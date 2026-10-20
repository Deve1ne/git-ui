using GitUi.Application.DTOs;
using GitUi.Infrastructure.Settings;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"gitui-settings-{Guid.NewGuid()}.json");

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsDefaults()
    {
        var store = new JsonSettingsStore(_filePath);

        var settings = await store.LoadAsync();

        Assert.Empty(settings.RecentRepositoryPaths);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsValues()
    {
        var store = new JsonSettingsStore(_filePath);
        var settings = new AppSettings(new[] { "/repo1" }, Array.Empty<string>(), "Light");

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(settings.RecentRepositoryPaths, loaded.RecentRepositoryPaths);
        Assert.Equal("Light", loaded.Theme);
    }

    [Fact]
    public async Task LoadAsync_FileFromOlderVersion_LoadsWithNewFieldsNull()
    {
        File.WriteAllText(_filePath, """{ "RecentRepositoryPaths": ["/old"], "PinnedRepositoryPaths": [], "Theme": "Dark" }""");
        var store = new JsonSettingsStore(_filePath);

        var loaded = await store.LoadAsync();

        Assert.Equal(new[] { "/old" }, loaded.RecentRepositoryPaths);
        Assert.Null(loaded.OpenRepositoryPaths);
        Assert.Null(loaded.Layout);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsTabsAndLayout()
    {
        var store = new JsonSettingsStore(_filePath);
        var layout = new LayoutSettings(230, 410, new Dictionary<string, float> { ["Graph"] = 180f });
        var settings = new AppSettings(Array.Empty<string>(), Array.Empty<string>(), "Dark", new[] { "/a", "/b" }, "/b", layout, "Rebase");

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(new[] { "/a", "/b" }, loaded.OpenRepositoryPaths);
        Assert.Equal("/b", loaded.ActiveRepositoryPath);
        Assert.Equal(230, loaded.Layout!.RefPanelWidth);
        Assert.Equal(180f, loaded.Layout.GraphColumnWidths["Graph"]);
        Assert.Equal("Rebase", loaded.PullMode);
    }

    [Fact]
    public async Task ConcurrentLoadsAndSaves_DoNotFailOnFileSharing()
    {
        var store = new JsonSettingsStore(_filePath);
        await store.SaveAsync(AppSettings.CreateDefault());

        var operations = Enumerable.Range(0, 40).Select(i => i % 2 == 0
            ? store.SaveAsync(AppSettings.CreateDefault() with { Theme = $"T{i}" })
            : store.LoadAsync());

        await Task.WhenAll(operations);
    }

    [Fact]
    public async Task ConcurrentUpdates_KeepEveryChange()
    {
        var store = new JsonSettingsStore(_filePath);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            store.UpdateAsync(s => s with { RecentRepositoryPaths = s.RecentRepositoryPaths.Append($"/repo{i}").ToList() })));

        var loaded = await store.LoadAsync();
        Assert.Equal(20, loaded.RecentRepositoryPaths.Count);
    }

    public void Dispose()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
