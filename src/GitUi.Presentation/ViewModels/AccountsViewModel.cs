using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Hosting;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public sealed record ProviderOption(HostingProvider Provider, string Name);

/// <summary>One connected account in the Accounts window.</summary>
public sealed record AccountRow(HostingAccount Account)
{
    public string ProviderName => Account.Provider.DisplayName();

    public string Label => Account.DisplayName is { Length: > 0 } name && name != Account.Login ? $"{Account.Label} — {name}" : Account.Label;

    public string Method => Account.AuthMethod == HostingAuthMethod.CredentialManager ? "Git Credential Manager" : "Personal access token";
}

/// <summary>
/// Accounts window: the connected GitHub / GitLab / Azure DevOps / Bitbucket accounts, and the form
/// to add one — Git Credential Manager's sign-in (its browser OAuth) or a personal access token.
/// Changes are saved at once.
/// </summary>
public partial class AccountsViewModel : ObservableObject
{
    private readonly HostingService _hosting;

    public AccountsViewModel(HostingService hosting)
    {
        _hosting = hosting;
        selectedProvider = Providers[0];
    }

    public ObservableCollection<AccountRow> Accounts { get; } = new();

    public bool HasAccounts => Accounts.Count > 0;

    public IReadOnlyList<ProviderOption> Providers { get; } = Enum.GetValues<HostingProvider>()
        .Select(p => new ProviderOption(p, p.DisplayName()))
        .ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsOrganization), nameof(NeedsUsername), nameof(TokenHelp), nameof(CredentialManagerHelp))]
    private ProviderOption selectedProvider;

    // GCM's GitHub and Microsoft sign-ins allow the API; its GitLab and Bitbucket ones may only allow git.
    partial void OnSelectedProviderChanged(ProviderOption value)
    {
        UseCredentialManager = value.Provider is HostingProvider.GitHub or HostingProvider.AzureDevOps;
        ErrorMessage = null;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseToken), nameof(NeedsUsername))]
    private bool useCredentialManager = true;

    public bool UseToken
    {
        get => !UseCredentialManager;
        set => UseCredentialManager = !value;
    }

    public bool NeedsOrganization => SelectedProvider.Provider == HostingProvider.AzureDevOps;

    public bool NeedsUsername => UseToken && SelectedProvider.Provider == HostingProvider.Bitbucket;

    [ObservableProperty]
    private string organization = string.Empty;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string token = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public string TokenHelp => SelectedProvider.Provider switch
    {
        HostingProvider.GitHub => "A classic token with the repo and read:org scopes (a fine-grained token needs Pull requests: read & write and Contents: read).",
        HostingProvider.GitLab => "A personal access token with the api scope.",
        HostingProvider.AzureDevOps => "A personal access token of this organization with the Code (Read & write) scope.",
        HostingProvider.Bitbucket => "An API token with the read:user, read:repository, read:pullrequest and write:pullrequest scopes, with your Atlassian account email as user name — or an app password with your Bitbucket user name.",
        _ => string.Empty,
    };

    public string CredentialManagerHelp => SelectedProvider.Provider switch
    {
        HostingProvider.GitHub => "Uses the GitHub sign-in Git Credential Manager already holds for git, or opens its browser sign-in.",
        HostingProvider.AzureDevOps => "Uses Git Credential Manager's Microsoft sign-in for this organization (browser), as git does.",
        _ => $"Git Credential Manager's {SelectedProvider.Name} sign-in may only allow git access, not the API: if the account is refused, use a token.",
    };

    public async Task LoadAsync()
    {
        Accounts.Clear();
        foreach (var account in (await _hosting.GetAccountsAsync()).OrderBy(a => a.Provider).ThenBy(a => a.Login, StringComparer.OrdinalIgnoreCase))
        {
            Accounts.Add(new AccountRow(account));
        }

        OnPropertyChanged(nameof(HasAccounts));
    }

    private bool CanConnect() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = UseCredentialManager
            ? "Waiting for Git Credential Manager (finish the sign-in in the browser or window it opened)…"
            : $"Checking the token with {SelectedProvider.Name}…";
        try
        {
            var account = UseCredentialManager
                ? await _hosting.AddCredentialManagerAccountAsync(SelectedProvider.Provider, Organization)
                : await _hosting.AddTokenAccountAsync(SelectedProvider.Provider, Token, Organization, Username);
            Token = string.Empty;
            StatusMessage = $"Connected {account.Provider.DisplayName()} account {account.Label}.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(AccountRow row)
    {
        await _hosting.RemoveAccountAsync(row.Account.Id);
        StatusMessage = $"Removed {row.ProviderName} account {row.Account.Label}.";
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenTokenPage()
    {
        var organization = Uri.EscapeDataString(Organization.Trim());
        ShellLauncher.OpenUrl(SelectedProvider.Provider switch
        {
            HostingProvider.GitHub => "https://github.com/settings/tokens/new?scopes=repo,read:org&description=Git%20UI",
            HostingProvider.GitLab => "https://gitlab.com/-/user_settings/personal_access_tokens?name=Git%20UI&scopes=api",
            HostingProvider.AzureDevOps => organization.Length > 0 ? $"https://dev.azure.com/{organization}/_usersSettings/tokens" : "https://dev.azure.com/",
            _ => "https://id.atlassian.com/manage-profile/security/api-tokens",
        });
    }
}
