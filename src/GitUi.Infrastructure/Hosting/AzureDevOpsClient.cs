using System.Net.Http.Headers;
using System.Text.Json;
using GitUi.Application.Hosting;

namespace GitUi.Infrastructure.Hosting;

/// <summary>Azure DevOps Services (dev.azure.com) REST API 7.1, scoped to the account's organization.</summary>
public sealed class AzureDevOpsClient : HostingApiClient
{
    private const string ApiVersion = "api-version=7.1";

    public AzureDevOpsClient(HttpClient http) : base(http)
    {
    }

    public override HostingProvider Provider => HostingProvider.AzureDevOps;

    /// <summary>
    /// A personal access token goes as the password of basic authentication; Git Credential Manager's
    /// OAuth tokens (JWTs, "eyJ...") as bearer tokens — and it may also hold a PAT, so both are tried.
    /// </summary>
    protected override IReadOnlyList<AuthenticationHeaderValue> AuthorizationFor(HostingSession session)
    {
        var secret = session.Credential.Secret;
        var basic = Basic(session.Credential.Username ?? string.Empty, secret);
        if (session.Account.AuthMethod == HostingAuthMethod.PersonalAccessToken)
        {
            return new[] { basic };
        }

        return secret.StartsWith("eyJ", StringComparison.Ordinal) ? new[] { Bearer(secret), basic } : new[] { basic, Bearer(secret) };
    }

    public override async Task<HostingUser> GetCurrentUserAsync(HostingSession session, CancellationToken cancellationToken = default)
    {
        // connectionData answers for any valid credential of the organization, whatever its scopes.
        var data = await GetAsync(session, $"{OrganizationUrl(session.Account.Organization)}/_apis/connectionData", cancellationToken);
        var user = data.TryGetProperty("authenticatedUser", out var authenticated) ? authenticated : default;
        var display = Str(user, "customDisplayName") ?? Str(user, "providerDisplayName");
        var account = Str(user, "properties", "Account", "$value");
        if (display is null && account is null)
        {
            throw new HostingException($"Azure DevOps did not recognize the credential for the organization {session.Account.Organization}.");
        }

        return new HostingUser(account ?? display!, display);
    }

    public override async Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(HostingSession session, CancellationToken cancellationToken = default)
    {
        var data = await GetAsync(session, $"{OrganizationUrl(session.Account.Organization)}/_apis/git/repositories?{ApiVersion}", cancellationToken);
        return Items(data, "value")
            .Where(repo => !Bool(repo, "isDisabled"))
            .Select(repo => new HostedRepository(
                $"{Str(repo, "project", "name")}/{Str(repo, "name")}",
                Str(repo, "name") ?? "?",
                StripUser(Str(repo, "remoteUrl") ?? string.Empty),
                Str(repo, "project", "description"),
                Str(repo, "project", "visibility") is not "public",
                Date(repo, "project", "lastUpdateTime")))
            .OrderByDescending(r => r.UpdatedAt)
            .ToList();
    }

    public override async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default)
    {
        var data = await GetAsync(session, $"{RepoUrl(repository)}/pullrequests?searchCriteria.status=active&$top=200&{ApiVersion}", cancellationToken);
        return Items(data, "value").Select(pull => MapPullRequest(pull, repository)).ToList();
    }

    public override async Task<string?> GetDefaultBranchAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default) =>
        BranchName(Str(await GetAsync(session, $"{RepoUrl(repository)}?{ApiVersion}", cancellationToken), "defaultBranch"));

    public override async Task<PullRequest> CreatePullRequestAsync(HostingSession session, RemoteRepositoryRef repository, NewPullRequest request, CancellationToken cancellationToken = default)
    {
        var created = await PostAsync(session, $"{RepoUrl(repository)}/pullrequests?{ApiVersion}", new
        {
            sourceRefName = $"refs/heads/{request.SourceBranch}",
            targetRefName = $"refs/heads/{request.TargetBranch}",
            title = request.Title,
            description = request.Description,
            isDraft = request.IsDraft,
        }, cancellationToken);
        return MapPullRequest(created, repository);
    }

    private static string OrganizationUrl(string? organization) => $"https://dev.azure.com/{Escape(organization ?? string.Empty)}";

    private static string RepoUrl(RemoteRepositoryRef repository) =>
        $"{OrganizationUrl(repository.Owner)}/{Escape(repository.Project ?? string.Empty)}/_apis/git/repositories/{Escape(repository.Name)}";

    private static string? BranchName(string? refName) =>
        refName is not null && refName.StartsWith("refs/heads/", StringComparison.Ordinal) ? refName["refs/heads/".Length..] : refName;

    /// <summary>"https://org@dev.azure.com/..." → "https://dev.azure.com/...": the clone URL should not carry a user.</summary>
    private static string StripUser(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri
            : url;

    private static PullRequest MapPullRequest(JsonElement pull, RemoteRepositoryRef repository)
    {
        var id = Long(pull, "pullRequestId");
        var sourceRepository = Str(pull, "forkSource", "repository", "name");
        return new PullRequest(
            id,
            Str(pull, "title") ?? string.Empty,
            Str(pull, "createdBy", "displayName") ?? "?",
            BranchName(Str(pull, "sourceRefName")) ?? string.Empty,
            BranchName(Str(pull, "targetRefName")) ?? string.Empty,
            $"{repository.WebUrl}/pullrequest/{id}",
            Bool(pull, "isDraft"),
            Date(pull, "creationDate"),
            sourceRepository);
    }

    private static DateTimeOffset? Date(JsonElement element, string first, string second) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(first, out var inner) ? Date(inner, second) : null;
}
