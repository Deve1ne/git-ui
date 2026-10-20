using GitUi.Application.Abstractions;

namespace GitUi.Application.Hosting;

/// <summary>The hosting service a repository's remote points to, and the account to call it with.</summary>
/// <param name="Account">Null when no connected account matches: pull requests need one.</param>
public sealed record RepositoryHosting(string RemoteName, RemoteRepositoryRef Repository, HostingAccount? Account);

/// <summary>
/// Connected accounts (GitHub, GitLab, Azure DevOps, Bitbucket) and what the app does with them:
/// list the account's repositories to clone, list a repository's open pull requests, create one.
/// Accounts are saved in the settings; their tokens in <see cref="IHostingTokenStore"/>.
/// </summary>
public sealed class HostingService
{
    private readonly IReadOnlyDictionary<HostingProvider, IHostingClient> _clients;
    private readonly IHostingTokenStore _tokens;
    private readonly IGitCredentialBridge _credentials;
    private readonly ISettingsStore _settings;
    private readonly IGitRepositoryReader _reader;

    public HostingService(
        IEnumerable<IHostingClient> clients,
        IHostingTokenStore tokens,
        IGitCredentialBridge credentials,
        ISettingsStore settings,
        IGitRepositoryReader reader)
    {
        _clients = clients.ToDictionary(c => c.Provider);
        _tokens = tokens;
        _credentials = credentials;
        _settings = settings;
        _reader = reader;
    }

    /// <summary>Raised after an account was added or removed.</summary>
    public event Action? AccountsChanged;

    public async Task<IReadOnlyList<HostingAccount>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        (await _settings.LoadAsync(cancellationToken)).HostingAccounts ?? Array.Empty<HostingAccount>();

    /// <summary>Checks the token against the service, then saves the account and the token.</summary>
    public async Task<HostingAccount> AddTokenAccountAsync(
        HostingProvider provider,
        string token,
        string? organization,
        string? username,
        CancellationToken cancellationToken = default)
    {
        token = token.Trim();
        if (token.Length == 0)
        {
            throw new HostingException("Paste the token first.");
        }

        organization = RequireOrganization(provider, organization);
        username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        if (provider == HostingProvider.Bitbucket && username is null)
        {
            throw new HostingException("Bitbucket needs the user name (app password) or Atlassian account email (API token) that goes with the token.");
        }

        var draft = new HostingAccount(Guid.NewGuid().ToString("N"), provider, HostingAuthMethod.PersonalAccessToken, string.Empty, null, organization, username);
        var user = await Client(provider).GetCurrentUserAsync(new HostingSession(draft, new HostingCredential(token, username)), cancellationToken);
        var account = draft with { Login = user.Login, DisplayName = user.DisplayName };

        await _tokens.SaveAsync(account.Id, token, cancellationToken);
        await SaveAccountAsync(account, cancellationToken);
        return account;
    }

    /// <summary>
    /// Signs in through Git Credential Manager (its browser sign-in when it has nothing stored),
    /// checks the credential against the service's API, and saves the account.
    /// </summary>
    public async Task<HostingAccount> AddCredentialManagerAccountAsync(
        HostingProvider provider,
        string? organization,
        CancellationToken cancellationToken = default)
    {
        organization = RequireOrganization(provider, organization);
        var url = provider.CredentialUrl(organization);
        var credential = await _credentials.FillAsync(url, interactive: true, cancellationToken)
            ?? throw new HostingException(
                $"Git Credential Manager gave no credential for {url}. It comes with Git for Windows (credential.helper=manager); " +
                "without it, add the account with a personal access token.");

        var draft = new HostingAccount(Guid.NewGuid().ToString("N"), provider, HostingAuthMethod.CredentialManager, string.Empty, null, organization);
        HostingUser user;
        try
        {
            user = await Client(provider).GetCurrentUserAsync(new HostingSession(draft, credential), cancellationToken);
        }
        catch (HostingException ex) when (ex.StatusCode is 401 or 403)
        {
            throw new HostingException(
                $"{provider.DisplayName()} refused the credential Git Credential Manager holds for {url} ({ex.Message}). " +
                "It may only allow git access, not the API: add the account with a personal access token instead.", ex.StatusCode, ex);
        }

        await _credentials.ApproveAsync(url, credential, cancellationToken);
        var account = draft with { Login = user.Login, DisplayName = user.DisplayName };
        await SaveAccountAsync(account, cancellationToken);
        return account;
    }

    public async Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await _settings.UpdateAsync(s => s with
        {
            HostingAccounts = (s.HostingAccounts ?? Array.Empty<HostingAccount>()).Where(a => a.Id != accountId).ToList(),
        }, cancellationToken);
        await _tokens.DeleteAsync(accountId, cancellationToken);
        AccountsChanged?.Invoke();
    }

    /// <param name="interactive">
    /// False for background loads: Git Credential Manager may then not open its sign-in, and the call
    /// fails instead of popping a browser the user did not ask for.
    /// </param>
    public async Task<HostingSession> GetSessionAsync(HostingAccount account, bool interactive = true, CancellationToken cancellationToken = default)
    {
        if (account.AuthMethod == HostingAuthMethod.CredentialManager)
        {
            var url = account.Provider.CredentialUrl(account.Organization);
            var credential = await _credentials.FillAsync(url, interactive, cancellationToken)
                ?? throw new HostingException(interactive
                    ? $"Git Credential Manager gave no credential for {url}: sign in again from Preferences > Accounts."
                    : $"Git Credential Manager needs a new sign-in for {url}: reload to sign in.");
            return new HostingSession(account, credential);
        }

        var token = await _tokens.GetAsync(account.Id, cancellationToken)
            ?? throw new HostingException($"The token of {account.Provider.DisplayName()} account {account.Label} is missing: remove the account and add it again.");
        return new HostingSession(account, new HostingCredential(token, account.Username));
    }

    /// <summary>
    /// The hosting service behind the repository's remote ("origin" first, else the first one a
    /// service recognizes) and the connected account for it; null when no remote is on a supported service.
    /// </summary>
    public async Task<RepositoryHosting?> FindHostingAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var remotes = await _reader.GetRemotesAsync(repositoryPath, cancellationToken);
        var candidates = remotes
            .OrderBy(r => r.Name == "origin" ? 0 : 1)
            .Select(r => (r.Name, Repository: RemoteUrlParser.Parse(r.FetchUrl)))
            .Where(c => c.Repository is not null)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var (remoteName, repository) = candidates[0];
        var accounts = await GetAccountsAsync(cancellationToken);
        return new RepositoryHosting(remoteName, repository!, MatchAccount(accounts, repository!));
    }

    /// <summary>
    /// The account to call <paramref name="repository"/>'s service with. Azure DevOps accounts are tied
    /// to their organization; on the other services the first account of that service is used.
    /// </summary>
    public static HostingAccount? MatchAccount(IReadOnlyList<HostingAccount> accounts, RemoteRepositoryRef repository) =>
        accounts.FirstOrDefault(a => a.Provider == repository.Provider
            && (repository.Provider != HostingProvider.AzureDevOps
                || string.Equals(a.Organization, repository.Owner, StringComparison.OrdinalIgnoreCase)));

    public async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(RepositoryHosting hosting, bool interactive = true, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(RequireAccount(hosting), interactive, cancellationToken);
        return await Client(hosting.Repository.Provider).GetPullRequestsAsync(session, hosting.Repository, cancellationToken);
    }

    public async Task<string?> GetDefaultBranchAsync(RepositoryHosting hosting, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(RequireAccount(hosting), true, cancellationToken);
        return await Client(hosting.Repository.Provider).GetDefaultBranchAsync(session, hosting.Repository, cancellationToken);
    }

    public async Task<PullRequest> CreatePullRequestAsync(RepositoryHosting hosting, NewPullRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new HostingException("The title is required.");
        }

        if (request.SourceBranch == request.TargetBranch)
        {
            throw new HostingException($"The source and target branches are both {request.SourceBranch}.");
        }

        var session = await GetSessionAsync(RequireAccount(hosting), true, cancellationToken);
        return await Client(hosting.Repository.Provider).CreatePullRequestAsync(session, hosting.Repository, request, cancellationToken);
    }

    public async Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(HostingAccount account, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(account, true, cancellationToken);
        return await Client(account.Provider).GetRepositoriesAsync(session, cancellationToken);
    }

    private static HostingAccount RequireAccount(RepositoryHosting hosting) => hosting.Account
        ?? throw new HostingException(
            $"No {hosting.Repository.Provider.DisplayName()} account is connected" +
            (hosting.Repository.Provider == HostingProvider.AzureDevOps ? $" for the organization {hosting.Repository.Owner}" : string.Empty) +
            ": add one in Preferences > Accounts.");

    private static string? RequireOrganization(HostingProvider provider, string? organization)
    {
        if (provider != HostingProvider.AzureDevOps)
        {
            return null;
        }

        organization = organization?.Trim().Trim('/');
        // Accept a pasted URL: https://dev.azure.com/org/...
        if (organization is not null && organization.Contains("dev.azure.com/", StringComparison.OrdinalIgnoreCase))
        {
            organization = organization[(organization.IndexOf("dev.azure.com/", StringComparison.OrdinalIgnoreCase) + "dev.azure.com/".Length)..].Split('/')[0];
        }

        return string.IsNullOrWhiteSpace(organization)
            ? throw new HostingException("Azure DevOps needs the organization (the name after dev.azure.com/).")
            : organization;
    }

    private async Task SaveAccountAsync(HostingAccount account, CancellationToken cancellationToken)
    {
        // Connecting the same account again replaces it (a new token, another sign-in method).
        var replaced = new List<HostingAccount>();
        await _settings.UpdateAsync(s =>
        {
            var accounts = (s.HostingAccounts ?? Array.Empty<HostingAccount>()).ToList();
            replaced = accounts.Where(a => IsSameAccount(a, account)).ToList();
            accounts.RemoveAll(a => IsSameAccount(a, account));
            accounts.Add(account);
            return s with { HostingAccounts = accounts };
        }, cancellationToken);

        foreach (var old in replaced)
        {
            await _tokens.DeleteAsync(old.Id, cancellationToken);
        }

        AccountsChanged?.Invoke();
    }

    private static bool IsSameAccount(HostingAccount a, HostingAccount b) =>
        a.Provider == b.Provider
        && string.Equals(a.Login, b.Login, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Organization, b.Organization, StringComparison.OrdinalIgnoreCase);

    private IHostingClient Client(HostingProvider provider) =>
        _clients.TryGetValue(provider, out var client) ? client : throw new HostingException($"{provider.DisplayName()} is not supported.");
}
