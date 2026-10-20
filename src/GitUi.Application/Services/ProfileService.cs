using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>
/// A named identity — "Work", "Personal" — switched in one click: the name and email written into
/// commits, and optionally the key commits are signed with.
/// </summary>
public sealed record GitProfile(string Id, string Name, string UserName, string Email, string? SigningKey = null, bool SignCommits = false);

/// <summary>
/// Profiles are saved in the app settings; activating one writes its identity (and signing key) to
/// the global git config, so the command line and other tools use it too.
/// </summary>
public sealed class ProfileService
{
    private readonly ISettingsStore _settings;
    private readonly IGitRepositoryWriter _config;

    public ProfileService(ISettingsStore settings, IGitRepositoryWriter config)
    {
        _settings = settings;
        _config = config;
    }

    /// <summary>Raised after profiles were changed or another one was activated.</summary>
    public event Action? Changed;

    public async Task<IReadOnlyList<GitProfile>> GetProfilesAsync(CancellationToken cancellationToken = default) =>
        (await _settings.LoadAsync(cancellationToken)).Profiles ?? Array.Empty<GitProfile>();

    public async Task<GitProfile?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken);
        return settings.Profiles?.FirstOrDefault(p => p.Id == settings.ActiveProfileId);
    }

    /// <summary>Adds the profile, or replaces the one with the same id. The active profile is applied again.</summary>
    public async Task SaveAsync(GitProfile profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.UserName) || string.IsNullOrWhiteSpace(profile.Email))
        {
            throw new InvalidOperationException("A profile needs a name, a user name and an email.");
        }

        if (profile.SignCommits && string.IsNullOrWhiteSpace(profile.SigningKey))
        {
            throw new InvalidOperationException("Signing commits needs a signing key.");
        }

        var updated = await _settings.UpdateAsync(s =>
        {
            var profiles = (s.Profiles ?? Array.Empty<GitProfile>()).Where(p => p.Id != profile.Id).ToList();
            profiles.Add(profile);
            return s with { Profiles = profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList() };
        }, cancellationToken);

        if (updated.ActiveProfileId == profile.Id)
        {
            await ApplyAsync(profile, cancellationToken);
        }

        Changed?.Invoke();
    }

    public async Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await _settings.UpdateAsync(s => s with
        {
            Profiles = (s.Profiles ?? Array.Empty<GitProfile>()).Where(p => p.Id != profileId).ToList(),
            ActiveProfileId = s.ActiveProfileId == profileId ? null : s.ActiveProfileId,
        }, cancellationToken);
        Changed?.Invoke();
    }

    /// <summary>Writes the profile's identity to the global git config and remembers it as active.</summary>
    public async Task ActivateAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var profile = (await GetProfilesAsync(cancellationToken)).FirstOrDefault(p => p.Id == profileId)
            ?? throw new InvalidOperationException("That profile no longer exists.");
        await ApplyAsync(profile, cancellationToken);
        await _settings.UpdateAsync(s => s with { ActiveProfileId = profile.Id }, cancellationToken);
        Changed?.Invoke();
    }

    private async Task ApplyAsync(GitProfile profile, CancellationToken cancellationToken)
    {
        await _config.SetConfigValueAsync(null, "user.name", profile.UserName, global: true, cancellationToken);
        await _config.SetConfigValueAsync(null, "user.email", profile.Email, global: true, cancellationToken);
        // A profile without a key must not keep signing with the previous profile's key.
        await _config.SetConfigValueAsync(null, "user.signingkey", profile.SigningKey, global: true, cancellationToken);
        await _config.SetConfigValueAsync(null, "commit.gpgsign", profile.SignCommits ? "true" : null, global: true, cancellationToken);
    }
}
