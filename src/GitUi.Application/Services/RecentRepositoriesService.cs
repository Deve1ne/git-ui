using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

public sealed class RecentRepositoriesService
{
    private const int MaxRecent = 10;
    private readonly ISettingsStore _settingsStore;

    public RecentRepositoriesService(ISettingsStore settingsStore) => _settingsStore = settingsStore;

    public async Task<IReadOnlyList<string>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        return settings.RecentRepositoryPaths;
    }

    public async Task AddRecentAsync(string path, CancellationToken cancellationToken = default)
    {
        await _settingsStore.UpdateAsync(settings =>
        {
            var updated = new List<string> { path };
            updated.AddRange(settings.RecentRepositoryPaths.Where(p => p != path));

            if (updated.Count > MaxRecent)
            {
                updated.RemoveRange(MaxRecent, updated.Count - MaxRecent);
            }

            return settings with { RecentRepositoryPaths = updated };
        }, cancellationToken);
    }

    public async Task RemoveRecentAsync(string path, CancellationToken cancellationToken = default)
    {
        await _settingsStore.UpdateAsync(
            settings => settings with
            {
                RecentRepositoryPaths = settings.RecentRepositoryPaths.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList(),
            },
            cancellationToken);
    }
}
