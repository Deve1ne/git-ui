namespace GitUi.Application.Hosting;

/// <summary>The hosting services the app integrates with (cloud editions only).</summary>
public enum HostingProvider { GitHub, GitLab, AzureDevOps, Bitbucket }

/// <summary>How an account authenticates.</summary>
public enum HostingAuthMethod
{
    /// <summary>A personal access token (Bitbucket: an API token or app password) kept in the app's token store.</summary>
    PersonalAccessToken,

    /// <summary>
    /// Whatever Git Credential Manager holds for the host, asked again with <c>git credential fill</c>
    /// on each use: its browser sign-in is the OAuth flow, and it refreshes expiring tokens itself.
    /// </summary>
    CredentialManager,
}

/// <summary>
/// A connected account, saved in the settings. The secret is never part of it: a token lives in the
/// token store under <see cref="Id"/>, a Credential Manager account has none of its own.
/// </summary>
/// <param name="Login">The user name on the service (Azure DevOps: the account's email or display name).</param>
/// <param name="Organization">Azure DevOps only: the organization (dev.azure.com/{organization}).</param>
/// <param name="Username">Bitbucket token accounts only: the user name or Atlassian email that goes with the token.</param>
public sealed record HostingAccount(
    string Id,
    HostingProvider Provider,
    HostingAuthMethod AuthMethod,
    string Login,
    string? DisplayName = null,
    string? Organization = null,
    string? Username = null)
{
    public string Label => Provider == HostingProvider.AzureDevOps && Organization is { } organization
        ? $"{Login} ({organization})"
        : Login;
}

/// <summary>The secret an API call authenticates with.</summary>
/// <param name="Secret">A token (or, from Credential Manager, the password it holds for the host).</param>
/// <param name="Username">The user name that goes with it, when the service needs one (basic authentication).</param>
public sealed record HostingCredential(string Secret, string? Username);

/// <summary>An account with its credential, ready for API calls.</summary>
public sealed record HostingSession(HostingAccount Account, HostingCredential Credential);

public sealed record HostingUser(string Login, string? DisplayName);

/// <summary>A repository the account can access, for "clone from an account".</summary>
/// <param name="FullName">owner/name (GitLab: the full namespace path; Azure DevOps: project/name).</param>
public sealed record HostedRepository(string FullName, string Name, string CloneUrl, string? Description, bool IsPrivate, DateTimeOffset? UpdatedAt);

/// <summary>
/// Where a remote points on a hosting service.
/// </summary>
/// <param name="Owner">GitHub owner, GitLab namespace path ("group/subgroup"), Azure DevOps organization, Bitbucket workspace.</param>
/// <param name="Project">Azure DevOps only: the project holding the repository.</param>
public sealed record RemoteRepositoryRef(HostingProvider Provider, string Owner, string Name, string? Project = null)
{
    /// <summary>The path a GitLab API call identifies the project with ("group/subgroup/name").</summary>
    public string FullPath => Provider == HostingProvider.AzureDevOps ? $"{Owner}/{Project}/{Name}" : $"{Owner}/{Name}";

    public string WebUrl => Provider switch
    {
        HostingProvider.GitHub => $"https://github.com/{Owner}/{Name}",
        HostingProvider.GitLab => $"https://gitlab.com/{Owner}/{Name}",
        HostingProvider.AzureDevOps => $"https://dev.azure.com/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Project ?? string.Empty)}/_git/{Uri.EscapeDataString(Name)}",
        HostingProvider.Bitbucket => $"https://bitbucket.org/{Owner}/{Name}",
        _ => string.Empty,
    };
}

/// <summary>An open pull request (GitLab: merge request).</summary>
/// <param name="SourceRepository">The repository the source branch lives in when it is not the target repository (a fork).</param>
public sealed record PullRequest(
    long Number,
    string Title,
    string Author,
    string SourceBranch,
    string TargetBranch,
    string WebUrl,
    bool IsDraft,
    DateTimeOffset? CreatedAt,
    string? SourceRepository = null)
{
    public bool IsFromFork => SourceRepository is not null;
}

public sealed record NewPullRequest(string SourceBranch, string TargetBranch, string Title, string Description, bool IsDraft);

/// <summary>A hosting API call failed; the message is meant for the user.</summary>
public sealed class HostingException : Exception
{
    public HostingException(string message, int? statusCode = null, Exception? inner = null) : base(message, inner) => StatusCode = statusCode;

    public int? StatusCode { get; }
}

public static class HostingProviderNames
{
    public static string DisplayName(this HostingProvider provider) => provider switch
    {
        HostingProvider.GitHub => "GitHub",
        HostingProvider.GitLab => "GitLab",
        HostingProvider.AzureDevOps => "Azure DevOps",
        HostingProvider.Bitbucket => "Bitbucket",
        _ => provider.ToString(),
    };

    /// <summary>What the service calls a pull request.</summary>
    public static string PullRequestName(this HostingProvider provider) =>
        provider == HostingProvider.GitLab ? "merge request" : "pull request";

    /// <summary>The URL Git Credential Manager knows the service by.</summary>
    public static Uri CredentialUrl(this HostingProvider provider, string? organization) => provider switch
    {
        HostingProvider.GitHub => new Uri("https://github.com/"),
        HostingProvider.GitLab => new Uri("https://gitlab.com/"),
        HostingProvider.AzureDevOps => new Uri($"https://dev.azure.com/{Uri.EscapeDataString(organization ?? string.Empty)}/"),
        HostingProvider.Bitbucket => new Uri("https://bitbucket.org/"),
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
}
