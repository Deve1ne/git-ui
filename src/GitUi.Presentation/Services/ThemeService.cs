using Avalonia.Styling;
using GitUi.Application.Abstractions;
using GitUi.Application.DTOs;

namespace GitUi.Presentation.Services;

/// <summary>Applies the light/dark theme to the whole app and persists the choice. Dark is the default.</summary>
public sealed class ThemeService
{
    private readonly ISettingsStore _settingsStore;

    public ThemeService(ISettingsStore settingsStore) => _settingsStore = settingsStore;

    public bool IsDark { get; private set; } = true;

    public event Action? ThemeChanged;

    /// <summary>Anything but an explicit "Light" setting means dark.</summary>
    public void ApplyFromSettings(AppSettings settings) =>
        Apply(!string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase));

    public async Task SetDarkAsync(bool isDark)
    {
        Apply(isDark);
        await _settingsStore.UpdateAsync(settings => settings with { Theme = isDark ? "Dark" : "Light" });
    }

    private void Apply(bool isDark)
    {
        IsDark = isDark;
        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        ThemeChanged?.Invoke();
    }
}
