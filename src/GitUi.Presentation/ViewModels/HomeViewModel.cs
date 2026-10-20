using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Hosting;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public enum HomeSection { Open, Clone, Init }

/// <summary>One entry of the "Recently opened" list.</summary>
public sealed record RecentRepositoryItem(string Path, string Name, bool Exists, bool IsOpen)
{
    public string Status => !Exists ? "missing" : IsOpen ? "(open)" : string.Empty;
}

/// <summary>
/// New-tab page: open a repository (browse or recent list), clone one, or initialize one —
/// the Open / Clone / Init sections of GitKraken's repository management.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly MainWindowViewModel _mainWindow;
    private readonly RecentRepositoriesService _recentRepositories;
    private readonly RepositorySetupService _setupService;
    private readonly ISettingsStore _settingsStore;
    private readonly CommandErrorHandler _errorHandler;
    private readonly HostingService _hosting;
    private bool _folderNameEdited;
    private bool _updatingFolderName;
    private IReadOnlyList<HostedRepository> _accountRepositories = Array.Empty<HostedRepository>();

    public HomeViewModel(
        MainWindowViewModel mainWindow,
        RecentRepositoriesService recentRepositories,
        RepositorySetupService setupService,
        ISettingsStore settingsStore,
        CommandErrorHandler errorHandler,
        HostingService hosting)
    {
        _mainWindow = mainWindow;
        _recentRepositories = recentRepositories;
        _setupService = setupService;
        _settingsStore = settingsStore;
        _errorHandler = errorHandler;
        _hosting = hosting;
        _mainWindow.Tabs.CollectionChanged += (_, _) => _ = RefreshRecentAsync();
        _hosting.AccountsChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadCloneAccountsAsync());
    }

    // ---- Clone from an account ----

    public ObservableCollection<AccountRow> CloneAccounts { get; } = new();

    public bool HasCloneAccounts => CloneAccounts.Count > 0;

    [ObservableProperty]
    private AccountRow? selectedCloneAccount;

    // Repositories load when the Clone page shows, not at startup: no API call (or sign-in) nobody asked for.
    partial void OnSelectedCloneAccountChanged(AccountRow? value)
    {
        if (IsCloneSection)
        {
            _ = LoadAccountRepositoriesAsync(value);
        }
    }

    private string? _repositoriesAccountId;

    /// <summary>The selected account's repositories matching <see cref="AccountRepositoryFilter"/>.</summary>
    public ObservableCollection<HostedRepository> AccountRepositories { get; } = new();

    [ObservableProperty]
    private string accountRepositoryFilter = string.Empty;

    partial void OnAccountRepositoryFilterChanged(string value) => FilterAccountRepositories();

    [ObservableProperty]
    private HostedRepository? selectedAccountRepository;

    // Picking a repository fills the URL (and, through it, the folder name).
    partial void OnSelectedAccountRepositoryChanged(HostedRepository? value)
    {
        if (value is not null)
        {
            CloneUrl = value.CloneUrl;
        }
    }

    [ObservableProperty]
    private bool isLoadingAccountRepositories;

    [ObservableProperty]
    private string? accountRepositoriesMessage;

    /// <summary>Opens the Accounts window and waits until it closes.</summary>
    public Func<Task>? RequestAccounts { get; set; }

    [RelayCommand]
    private async Task ConnectAccountAsync()
    {
        if (RequestAccounts is not null)
        {
            await RequestAccounts();
        }
    }

    public async Task LoadCloneAccountsAsync()
    {
        var selectedId = SelectedCloneAccount?.Account.Id;
        CloneAccounts.Clear();
        foreach (var account in await _hosting.GetAccountsAsync())
        {
            CloneAccounts.Add(new AccountRow(account));
        }

        OnPropertyChanged(nameof(HasCloneAccounts));
        SelectedCloneAccount = CloneAccounts.FirstOrDefault(a => a.Account.Id == selectedId) ?? CloneAccounts.FirstOrDefault();
    }

    private async Task LoadAccountRepositoriesAsync(AccountRow? row)
    {
        _repositoriesAccountId = row?.Account.Id;
        _accountRepositories = Array.Empty<HostedRepository>();
        AccountRepositories.Clear();
        AccountRepositoriesMessage = null;
        if (row is null)
        {
            return;
        }

        IsLoadingAccountRepositories = true;
        try
        {
            var repositories = await _hosting.GetRepositoriesAsync(row.Account);
            if (SelectedCloneAccount != row)
            {
                return;
            }

            _accountRepositories = repositories;
            FilterAccountRepositories();
            AccountRepositoriesMessage = repositories.Count == 0 ? "This account has no repositories." : null;
        }
        catch (HostingException ex)
        {
            AccountRepositoriesMessage = ex.Message;
            _repositoriesAccountId = null;
        }
        finally
        {
            IsLoadingAccountRepositories = false;
        }
    }

    private void FilterAccountRepositories()
    {
        var filter = AccountRepositoryFilter.Trim();
        AccountRepositories.Clear();
        foreach (var repository in _accountRepositories.Where(r => filter.Length == 0 || r.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            AccountRepositories.Add(repository);
        }
    }

    /// <summary>Asks the view for a folder; the argument is the picker title.</summary>
    public Func<string, Task<string?>>? RequestFolderPick { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenSection), nameof(IsCloneSection), nameof(IsInitSection))]
    private HomeSection section = HomeSection.Open;

    public bool IsOpenSection => Section == HomeSection.Open;
    public bool IsCloneSection => Section == HomeSection.Clone;
    public bool IsInitSection => Section == HomeSection.Init;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    // ---- Open ----

    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = new();

    [ObservableProperty]
    private string recentFilter = string.Empty;

    partial void OnRecentFilterChanged(string value) => _ = RefreshRecentAsync();

    // ---- Clone ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloneFullPath))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    private string cloneUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloneFullPath))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    private string cloneParentDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloneFullPath))]
    private string cloneFolderName = string.Empty;

    [ObservableProperty]
    private string cloneBranch = string.Empty;

    [ObservableProperty]
    private bool shallowClone;

    [ObservableProperty]
    private decimal cloneDepth = 1;

    [ObservableProperty]
    private bool recurseSubmodules;

    public string CloneFullPath => string.IsNullOrWhiteSpace(CloneParentDirectory) || string.IsNullOrWhiteSpace(CloneFolderName)
        ? string.Empty
        : System.IO.Path.Combine(CloneParentDirectory, CloneFolderName);

    // The folder name follows the URL until the user types their own, like GitKraken's "Full path".
    partial void OnCloneUrlChanged(string value)
    {
        if (!_folderNameEdited)
        {
            _updatingFolderName = true;
            CloneFolderName = string.IsNullOrWhiteSpace(value) ? string.Empty : RepositorySetupService.DeriveFolderName(value);
            _updatingFolderName = false;
        }
    }

    partial void OnCloneFolderNameChanged(string value)
    {
        if (!_updatingFolderName)
        {
            _folderNameEdited = !string.IsNullOrWhiteSpace(value);
        }
    }

    // ---- Init ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InitCommand))]
    private string initParentDirectory = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InitCommand))]
    private string initName = string.Empty;

    [ObservableProperty]
    private string initDefaultBranch = "main";

    [ObservableProperty]
    private string initGitIgnoreTemplate = "None";

    [ObservableProperty]
    private bool initCreateInitialCommit = true;

    public IReadOnlyList<string> GitIgnoreTemplates { get; } = RepositorySetupService.GitIgnoreTemplates.Keys.ToList();

    public async Task InitializeAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        var defaultDirectory = settings.LastCloneDirectory
            ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos");
        CloneParentDirectory = defaultDirectory;
        InitParentDirectory = defaultDirectory;
        await RefreshRecentAsync();
        await LoadCloneAccountsAsync();
    }

    public async Task RefreshRecentAsync()
    {
        var recents = await _recentRepositories.GetRecentAsync();
        var openPaths = _mainWindow.Tabs.Select(t => t.Path.TrimEnd('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);

        RecentRepositories.Clear();
        foreach (var path in recents)
        {
            var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            if (RecentFilter.Length > 0
                && !name.Contains(RecentFilter, StringComparison.OrdinalIgnoreCase)
                && !path.Contains(RecentFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            RecentRepositories.Add(new RecentRepositoryItem(path, name, Directory.Exists(path), openPaths.Contains(path.TrimEnd('\\', '/'))));
        }
    }

    [RelayCommand]
    private void ShowSection(HomeSection target)
    {
        Section = target;
        StatusMessage = null;
        if (target == HomeSection.Clone && SelectedCloneAccount is { } account && _repositoriesAccountId != account.Account.Id)
        {
            _ = LoadAccountRepositoriesAsync(account);
        }
    }

    [RelayCommand]
    private Task BrowseAndOpenAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (RequestFolderPick is not null && await RequestFolderPick.Invoke("Open a Repository") is { } path)
        {
            await _mainWindow.OpenRepositoryPathAsync(path);
        }
    }, "open repository");

    [RelayCommand]
    private Task OpenRecentAsync(RecentRepositoryItem item) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (!item.Exists)
        {
            StatusMessage = $"{item.Path} no longer exists.";
            return;
        }

        await _mainWindow.OpenRepositoryPathAsync(item.Path);
    }, "open recent repository");

    [RelayCommand]
    private Task RemoveRecentAsync(RecentRepositoryItem item) => _errorHandler.RunGuardedAsync(async () =>
    {
        await _recentRepositories.RemoveRecentAsync(item.Path);
        await RefreshRecentAsync();
    }, "remove recent repository");

    [RelayCommand]
    private Task BrowseCloneDirectoryAsync() => BrowseIntoAsync("Where to clone to", path => CloneParentDirectory = path);

    [RelayCommand]
    private Task BrowseInitDirectoryAsync() => BrowseIntoAsync("Initialize in", path => InitParentDirectory = path);

    private async Task BrowseIntoAsync(string title, Action<string> apply)
    {
        if (RequestFolderPick is not null && await RequestFolderPick.Invoke(title) is { } path)
        {
            apply(path);
        }
    }

    private bool CanClone() => !IsBusy && !string.IsNullOrWhiteSpace(CloneUrl) && !string.IsNullOrWhiteSpace(CloneParentDirectory);

    [RelayCommand(CanExecute = nameof(CanClone))]
    private Task CloneAsync() => RunBusyAsync($"Cloning {CloneUrl}…", async () =>
    {
        var options = new CloneOptions(
            string.IsNullOrWhiteSpace(CloneBranch) ? null : CloneBranch.Trim(),
            ShallowClone ? (int)Math.Max(1, CloneDepth) : null,
            RecurseSubmodules);
        var path = await _setupService.CloneAsync(CloneUrl, CloneParentDirectory, CloneFolderName, options);

        var cloneDirectory = CloneParentDirectory;
        await _settingsStore.UpdateAsync(settings => settings with { LastCloneDirectory = cloneDirectory });

        CloneUrl = string.Empty;
        _folderNameEdited = false;
        await _mainWindow.OpenRepositoryPathAsync(path);
    }, "clone repository");

    private bool CanInit() => !IsBusy && !string.IsNullOrWhiteSpace(InitParentDirectory) && !string.IsNullOrWhiteSpace(InitName);

    [RelayCommand(CanExecute = nameof(CanInit))]
    private Task InitAsync() => RunBusyAsync($"Initializing {InitName}…", async () =>
    {
        var path = await _setupService.InitAsync(InitParentDirectory, InitName, InitDefaultBranch, InitGitIgnoreTemplate, InitCreateInitialCommit);
        InitName = string.Empty;
        await _mainWindow.OpenRepositoryPathAsync(path);
    }, "initialize repository");

    private Task RunBusyAsync(string message, Func<Task> action, string operationName) => _errorHandler.RunGuardedAsync(async () =>
    {
        IsBusy = true;
        StatusMessage = message;
        CloneCommand.NotifyCanExecuteChanged();
        InitCommand.NotifyCanExecuteChanged();
        try
        {
            await action();
            StatusMessage = null;
        }
        catch
        {
            StatusMessage = null;
            throw;
        }
        finally
        {
            IsBusy = false;
            CloneCommand.NotifyCanExecuteChanged();
            InitCommand.NotifyCanExecuteChanged();
        }
    }, operationName);
}
