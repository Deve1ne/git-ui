using CommunityToolkit.Mvvm.ComponentModel;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Presentation.Controls;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// Preferences window: git identity (global and per repository), auto-fetch interval and the graph
/// columns — the parts of GitKraken's preferences that apply to a local client.
/// </summary>
public partial class PreferencesViewModel : ObservableObject
{
    private readonly IGitRepositoryWriter _writer;
    private readonly ISettingsStore _settingsStore;
    private readonly SigningService _signing;
    private string? _repositoryPath;
    private CommitGraphColumns? _columns;
    private (string? Name, string? Email, string? RepoName, string? RepoEmail) _loaded;
    private SigningSettings? _loadedSigning;

    public PreferencesViewModel(IGitRepositoryWriter writer, ISettingsStore settingsStore, SigningService signing, ProfilesViewModel profiles)
    {
        _writer = writer;
        _settingsStore = settingsStore;
        _signing = signing;
        Profiles = profiles;
        // Switching profile rewrites the global identity: show it, and do not write the old one back.
        profiles.Activated += OnProfileActivated;
    }

    private void OnProfileActivated() => _ = ReloadGlobalIdentityAsync();

    /// <summary>Stops following profile switches once the window is closed (the profiles view model lives on).</summary>
    public void Detach() => Profiles.Activated -= OnProfileActivated;

    /// <summary>Identity profiles, edited and activated at once (not on Save).</summary>
    public ProfilesViewModel Profiles { get; }

    public IReadOnlyList<string> SigningFormats => SigningService.Formats;

    [ObservableProperty]
    private bool signCommits;

    [ObservableProperty]
    private bool signTags;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SigningKeyHint))]
    private string signingFormat = "openpgp";

    [ObservableProperty]
    private string signingKey = string.Empty;

    public string SigningKeyHint => SigningFormat switch
    {
        "ssh" => "Path of your SSH public key (e.g. ~/.ssh/id_ed25519.pub). Verifying signatures needs gpg.ssh.allowedSignersFile.",
        "x509" => "The X.509 certificate id used by gpgsm / smimesign.",
        _ => "Your GPG key id (gpg --list-secret-keys --keyid-format=long); GPG must be installed (Gpg4win on Windows).",
    };

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void UseSshKey()
    {
        var ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var key = new[] { "id_ed25519.pub", "id_ecdsa.pub", "id_rsa.pub" }.Select(name => Path.Combine(ssh, name)).FirstOrDefault(File.Exists);
        SigningFormat = "ssh";
        SigningKey = key ?? Path.Combine(ssh, "id_ed25519.pub");
    }

    private async Task ReloadGlobalIdentityAsync()
    {
        GlobalName = await _writer.GetConfigValueAsync(null, "user.name", global: true) ?? string.Empty;
        GlobalEmail = await _writer.GetConfigValueAsync(null, "user.email", global: true) ?? string.Empty;
        _loaded = (GlobalName, GlobalEmail, _loaded.RepoName, _loaded.RepoEmail);
        await LoadSigningAsync();
    }

    private async Task LoadSigningAsync()
    {
        var signing = await _signing.GetAsync();
        _loadedSigning = signing;
        SignCommits = signing.SignCommits;
        SignTags = signing.SignTags;
        SigningFormat = signing.Format;
        SigningKey = signing.SigningKey ?? string.Empty;
    }

    [ObservableProperty]
    private string globalName = string.Empty;

    [ObservableProperty]
    private string globalEmail = string.Empty;

    [ObservableProperty]
    private string repositoryName = string.Empty;

    [ObservableProperty]
    private string repositoryEmail = string.Empty;

    [ObservableProperty]
    private bool hasRepository;

    [ObservableProperty]
    private string repositoryLabel = string.Empty;

    /// <summary>Minutes between background fetches of the open repository; 0 turns it off.</summary>
    [ObservableProperty]
    private decimal autoFetchMinutes;

    [ObservableProperty]
    private bool showRefsColumn = true;

    [ObservableProperty]
    private bool showAuthorColumn = true;

    [ObservableProperty]
    private bool showDateColumn = true;

    [ObservableProperty]
    private bool showShaColumn = true;

    public async Task LoadAsync(string? repositoryPath, CommitGraphColumns columns)
    {
        _repositoryPath = repositoryPath;
        _columns = columns;
        HasRepository = repositoryPath is not null;
        RepositoryLabel = repositoryPath is null ? "No repository open" : $"This repository ({Path.GetFileName(repositoryPath.TrimEnd('\\', '/'))}) — overrides the global identity";

        GlobalName = await _writer.GetConfigValueAsync(null, "user.name", global: true) ?? string.Empty;
        GlobalEmail = await _writer.GetConfigValueAsync(null, "user.email", global: true) ?? string.Empty;
        if (repositoryPath is not null)
        {
            RepositoryName = await _writer.GetConfigValueAsync(repositoryPath, "user.name", global: false) ?? string.Empty;
            RepositoryEmail = await _writer.GetConfigValueAsync(repositoryPath, "user.email", global: false) ?? string.Empty;
        }

        _loaded = (GlobalName, GlobalEmail, RepositoryName, RepositoryEmail);
        await LoadSigningAsync();
        await Profiles.LoadAsync();

        var settings = await _settingsStore.LoadAsync();
        AutoFetchMinutes = settings.AutoFetchMinutes ?? 0;

        ShowRefsColumn = columns.IsVisible(CommitGraphColumn.Refs);
        ShowAuthorColumn = columns.IsVisible(CommitGraphColumn.Author);
        ShowDateColumn = columns.IsVisible(CommitGraphColumn.Date);
        ShowShaColumn = columns.IsVisible(CommitGraphColumn.Sha);
    }

    /// <summary>Writes only what changed, so opening and closing the window never rewrites git config.</summary>
    public async Task SaveAsync()
    {
        if (GlobalName != _loaded.Name)
        {
            await _writer.SetConfigValueAsync(null, "user.name", GlobalName, global: true);
        }

        if (GlobalEmail != _loaded.Email)
        {
            await _writer.SetConfigValueAsync(null, "user.email", GlobalEmail, global: true);
        }

        if (_repositoryPath is not null && RepositoryName != _loaded.RepoName)
        {
            await _writer.SetConfigValueAsync(_repositoryPath, "user.name", RepositoryName, global: false);
        }

        if (_repositoryPath is not null && RepositoryEmail != _loaded.RepoEmail)
        {
            await _writer.SetConfigValueAsync(_repositoryPath, "user.email", RepositoryEmail, global: false);
        }

        var signing = new SigningSettings(SignCommits, SignTags, SigningFormat, string.IsNullOrWhiteSpace(SigningKey) ? null : SigningKey.Trim());
        if (signing != _loadedSigning)
        {
            await _signing.SaveAsync(signing);
        }

        var minutes = (int)Math.Clamp(AutoFetchMinutes, 0, 600);
        await _settingsStore.UpdateAsync(settings => settings with { AutoFetchMinutes = minutes });

        if (_columns is { } columns)
        {
            columns.SetVisible(CommitGraphColumn.Refs, ShowRefsColumn);
            columns.SetVisible(CommitGraphColumn.Author, ShowAuthorColumn);
            columns.SetVisible(CommitGraphColumn.Date, ShowDateColumn);
            columns.SetVisible(CommitGraphColumn.Sha, ShowShaColumn);
        }
    }
}
