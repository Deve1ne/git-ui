using GitUi.Application.Abstractions;
using GitUi.Application.DTOs;
using GitUi.Application.Services;
using Xunit;

namespace GitUi.Application.Tests;

public class RecentRepositoriesServiceTests
{
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        public AppSettings Current { get; set; } = AppSettings.CreateDefault();

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
        {
            Current = update(Current);
            return Task.FromResult(Current);
        }
    }

    [Fact]
    public async Task AddRecentAsync_NewPath_PrependsAndDeduplicates()
    {
        var store = new InMemorySettingsStore { Current = AppSettings.CreateDefault() with { RecentRepositoryPaths = new[] { "/a", "/b" } } };

        await new RecentRepositoriesService(store).AddRecentAsync("/a");

        Assert.Equal(new[] { "/a", "/b" }, store.Current.RecentRepositoryPaths);
    }

    [Fact]
    public async Task AddRecentAsync_ExceedsMax_TrimsToTen()
    {
        var store = new InMemorySettingsStore
        {
            Current = AppSettings.CreateDefault() with { RecentRepositoryPaths = Enumerable.Range(0, 10).Select(i => $"/repo{i}").ToArray() },
        };

        await new RecentRepositoriesService(store).AddRecentAsync("/new-repo");

        Assert.Equal(10, store.Current.RecentRepositoryPaths.Count);
        Assert.Equal("/new-repo", store.Current.RecentRepositoryPaths[0]);
    }

    [Fact]
    public async Task RemoveRecentAsync_RemovesPathIgnoringCase()
    {
        var store = new InMemorySettingsStore { Current = AppSettings.CreateDefault() with { RecentRepositoryPaths = new[] { "C:\\A", "C:\\B" } } };

        await new RecentRepositoriesService(store).RemoveRecentAsync("c:\\a");

        Assert.Equal(new[] { "C:\\B" }, store.Current.RecentRepositoryPaths);
    }
}
