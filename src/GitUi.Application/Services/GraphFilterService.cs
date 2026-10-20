using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>
/// Remembers, per repository, which branches are hidden from or soloed in the graph (GitKraken's
/// eye and solo toggles). Kept in the settings file so the choice survives a restart.
/// </summary>
public sealed class GraphFilterService
{
    private readonly ISettingsStore _settingsStore;

    public GraphFilterService(ISettingsStore settingsStore) => _settingsStore = settingsStore;

    /// <summary>Raised with the repository path after its filter changed.</summary>
    public event Action<string>? Changed;

    public async Task<GraphRefFilter> GetAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        return settings.GraphFilterByRepository?.GetValueOrDefault(Key(repositoryPath)) ?? GraphRefFilter.Empty;
    }

    public Task ToggleHiddenAsync(string repositoryPath, string refName, CancellationToken cancellationToken = default) =>
        UpdateAsync(repositoryPath, filter => filter with { Hidden = Toggle(filter.Hidden, refName) }, cancellationToken);

    public Task ToggleSoloAsync(string repositoryPath, string refName, CancellationToken cancellationToken = default) =>
        UpdateAsync(repositoryPath, filter => filter with { Solo = Toggle(filter.Solo, refName) }, cancellationToken);

    /// <summary>Shows every branch again.</summary>
    public Task ClearAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        UpdateAsync(repositoryPath, _ => GraphRefFilter.Empty, cancellationToken);

    private async Task UpdateAsync(string repositoryPath, Func<GraphRefFilter, GraphRefFilter> change, CancellationToken cancellationToken)
    {
        var key = Key(repositoryPath);
        await _settingsStore.UpdateAsync(settings =>
        {
            var filters = new Dictionary<string, GraphRefFilter>(settings.GraphFilterByRepository ?? new Dictionary<string, GraphRefFilter>());
            var updated = change(filters.GetValueOrDefault(key) ?? GraphRefFilter.Empty);
            if (updated.IsEmpty)
            {
                filters.Remove(key);
            }
            else
            {
                filters[key] = updated;
            }

            return settings with { GraphFilterByRepository = filters };
        }, cancellationToken);
        Changed?.Invoke(repositoryPath);
    }

    private static IReadOnlyList<string> Toggle(IReadOnlyList<string> refs, string refName) =>
        refs.Contains(refName) ? refs.Where(r => r != refName).ToList() : refs.Append(refName).ToList();

    // Windows paths are case-insensitive: one key per repository whatever the casing it was opened with.
    private static string Key(string repositoryPath) => Path.GetFullPath(repositoryPath).TrimEnd('\\', '/').ToLowerInvariant();
}
