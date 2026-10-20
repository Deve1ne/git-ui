using GitUi.Application.DTOs;

namespace GitUi.Application.Abstractions;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads, transforms and saves the settings as one step, so two parts of the app updating
    /// different fields at the same time cannot overwrite each other's change.
    /// </summary>
    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default);
}
