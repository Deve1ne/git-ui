using GitUi.Application.Abstractions;
using GitUi.Application.DTOs;
using GitUi.Application.Services;
using Xunit;

namespace GitUi.Application.Tests;

public class GraphFilterServiceTests
{
    private sealed class MemorySettingsStore : ISettingsStore
    {
        public AppSettings Settings { get; private set; } = AppSettings.CreateDefault();

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }

        public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
        {
            Settings = update(Settings);
            return Task.FromResult(Settings);
        }
    }

    private readonly MemorySettingsStore _store = new();
    private readonly GraphFilterService _service;

    public GraphFilterServiceTests() => _service = new GraphFilterService(_store);

    [Fact]
    public async Task ToggleHidden_TwiceOnSameRef_AddsThenRemovesIt()
    {
        await _service.ToggleHiddenAsync("C:/repo", "refs/heads/old");
        Assert.Equal(new[] { "refs/heads/old" }, (await _service.GetAsync("C:/repo")).Hidden);

        await _service.ToggleHiddenAsync("C:/repo", "refs/heads/old");
        Assert.True((await _service.GetAsync("C:/repo")).IsEmpty);
        Assert.Empty(_store.Settings.GraphFilterByRepository!);
    }

    [Fact]
    public async Task Filters_AreKeptPerRepositoryAndIgnorePathCase()
    {
        await _service.ToggleSoloAsync("C:/Repo", "refs/heads/main");

        Assert.Equal(new[] { "refs/heads/main" }, (await _service.GetAsync("c:/repo")).Solo);
        Assert.True((await _service.GetAsync("C:/other")).IsEmpty);
    }

    [Fact]
    public async Task Clear_RaisesChangedAndEmptiesTheFilter()
    {
        await _service.ToggleSoloAsync("C:/repo", "refs/heads/main");
        string? changed = null;
        _service.Changed += path => changed = path;

        await _service.ClearAsync("C:/repo");

        Assert.Equal("C:/repo", changed);
        Assert.True((await _service.GetAsync("C:/repo")).IsEmpty);
    }
}
