using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public sealed record ProfileRow(GitProfile Profile, bool IsActive)
{
    public string Detail => $"{Profile.UserName} <{Profile.Email}>{(Profile.SignCommits ? " · signs commits" : string.Empty)}";
}

/// <summary>
/// Identity profiles: the list and editor in Preferences, and the switcher in the status bar.
/// Activating a profile writes its name, email and signing key to the global git config.
/// </summary>
public partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileService _profiles;
    private readonly CommandErrorHandler _errorHandler;
    private readonly NotificationService _notifications;

    public ProfilesViewModel(ProfileService profiles, CommandErrorHandler errorHandler, NotificationService notifications)
    {
        _profiles = profiles;
        _errorHandler = errorHandler;
        _notifications = notifications;
        _profiles.Changed += () => Dispatcher.UIThread.Post(() => _ = LoadAsync());
    }

    public ObservableCollection<ProfileRow> Profiles { get; } = new();

    public bool HasProfiles => Profiles.Count > 0;

    [ObservableProperty]
    private string activeName = "No profile";

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string editTitle = "New profile";

    private string? _editId;

    [ObservableProperty]
    private string editName = string.Empty;

    [ObservableProperty]
    private string editUserName = string.Empty;

    [ObservableProperty]
    private string editEmail = string.Empty;

    [ObservableProperty]
    private string editSigningKey = string.Empty;

    [ObservableProperty]
    private bool editSignCommits;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Raised after another profile was activated (the identity in Preferences changed).</summary>
    public event Action? Activated;

    public async Task LoadAsync()
    {
        var active = await _profiles.GetActiveAsync();
        Profiles.Clear();
        foreach (var profile in await _profiles.GetProfilesAsync())
        {
            Profiles.Add(new ProfileRow(profile, profile.Id == active?.Id));
        }

        ActiveName = active?.Name ?? "No profile";
        OnPropertyChanged(nameof(HasProfiles));
    }

    [RelayCommand]
    private void New()
    {
        _editId = null;
        EditTitle = "New profile";
        EditName = EditUserName = EditEmail = EditSigningKey = string.Empty;
        EditSignCommits = false;
        ErrorMessage = null;
        IsEditing = true;
    }

    [RelayCommand]
    private void Edit(ProfileRow row)
    {
        var profile = row.Profile;
        _editId = profile.Id;
        EditTitle = $"Edit {profile.Name}";
        EditName = profile.Name;
        EditUserName = profile.UserName;
        EditEmail = profile.Email;
        EditSigningKey = profile.SigningKey ?? string.Empty;
        EditSignCommits = profile.SignCommits;
        ErrorMessage = null;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        try
        {
            var profile = new GitProfile(
                _editId ?? Guid.NewGuid().ToString("N"),
                EditName.Trim(),
                EditUserName.Trim(),
                EditEmail.Trim(),
                string.IsNullOrWhiteSpace(EditSigningKey) ? null : EditSigningKey.Trim(),
                EditSignCommits);
            await _profiles.SaveAsync(profile);
            IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private Task DeleteAsync(ProfileRow row) => _errorHandler.RunGuardedAsync(async () =>
    {
        await _profiles.DeleteAsync(row.Profile.Id);
        await LoadAsync();
    }, "delete profile");

    [RelayCommand]
    private Task ActivateAsync(ProfileRow row) => _errorHandler.RunGuardedAsync(async () =>
    {
        await _profiles.ActivateAsync(row.Profile.Id);
        await LoadAsync();
        _notifications.Show($"Profile {row.Profile.Name}: committing as {row.Profile.UserName} <{row.Profile.Email}>");
        Activated?.Invoke();
    }, "switch profile");
}
