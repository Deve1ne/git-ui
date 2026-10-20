using System.Net.Http.Headers;
using System.Text.Json;
using GitUi.Application.Hosting;

namespace GitUi.Infrastructure.Hosting;

/// <summary>Bitbucket Cloud REST API 2.0.</summary>
public sealed class BitbucketClient : HostingApiClient
{
    private const string Api = "https://api.bitbucket.org/2.0";

    public BitbucketClient(HttpClient http) : base(http)
    {
    }

    public override HostingProvider Provider => HostingProvider.Bitbucket;

    /// <summary>
    /// API tokens and app passwords use basic authentication with their user name / email. Git
    /// Credential Manager holds an OAuth token (bearer) or an app password, so it tries both.
    /// </summary>
    protected override IReadOnlyList<AuthenticationHeaderValue> AuthorizationFor(HostingSession session)
    {
        var credential = session.Credential;
        var basic = Basic(credential.Username ?? string.Empty, credential.Secret);
        return session.Account.AuthMethod == HostingAuthMethod.PersonalAccessToken
            ? new[] { basic }
            : new[] { Bearer(credential.Secret), basic };
    }

    protected override string? NextPage(HttpResponseMessage response, JsonElement body) => Str(body, "next");

    public override async Task<HostingUser> GetCurrentUserAsync(HostingSession session, CancellationToken cancellationToken = default)
    {
        var user = await GetAsync(session, $"{Api}/user", cancellationToken);
        return new HostingUser(Str(user, "username") ?? Str(user, "nickname") ?? "?", Str(user, "display_name"));
    }

    public override async Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(HostingSession session, CancellationToken cancellationToken = default) =>
        await GetPagedAsync(session, $"{Api}/repositories?role=member&pagelen=100&sort=-updated_on",
            page => Items(page, "values"),
            repo => new HostedRepository(
                Str(repo, "full_name") ?? "?",
                Str(repo, "name") ?? "?",
                HttpsCloneUrl(repo),
                Str(repo, "description") is { Length: > 0 } description ? description : null,
                Bool(repo, "is_private"),
                Date(repo, "updated_on")),
            cancellationToken);

    public override async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default) =>
        await GetPagedAsync(session, $"{RepoUrl(repository)}/pullrequests?state=OPEN&pagelen=50",
            page => Items(page, "values"),
            pull => MapPullRequest(pull, repository),
            cancellationToken);

    public override async Task<string?> GetDefaultBranchAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default) =>
        Str(await GetAsync(session, RepoUrl(repository), cancellationToken), "mainbranch", "name");

    public override async Task<PullRequest> CreatePullRequestAsync(HostingSession session, RemoteRepositoryRef repository, NewPullRequest request, CancellationToken cancellationToken = default)
    {
        var created = await PostAsync(session, $"{RepoUrl(repository)}/pullrequests", new
        {
            title = request.Title,
            description = request.Description,
            source = new { branch = new { name = request.SourceBranch } },
            destination = new { branch = new { name = request.TargetBranch } },
            draft = request.IsDraft,
        }, cancellationToken);
        return MapPullRequest(created, repository);
    }

    private static string RepoUrl(RemoteRepositoryRef repository) => $"{Api}/repositories/{Escape(repository.Owner)}/{Escape(repository.Name)}";

    /// <summary>The HTTPS clone link, without the "user@" Bitbucket puts in it.</summary>
    private static string HttpsCloneUrl(JsonElement repo)
    {
        var href = Items(repo.TryGetProperty("links", out var links) ? links : default, "clone")
            .Where(link => Str(link, "name") == "https")
            .Select(link => Str(link, "href"))
            .FirstOrDefault() ?? string.Empty;
        return Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri
            : href;
    }

    private static PullRequest MapPullRequest(JsonElement pull, RemoteRepositoryRef repository)
    {
        var sourceRepository = Str(pull, "source", "repository", "full_name");
        var sameRepository = sourceRepository is null
            || string.Equals(sourceRepository, $"{repository.Owner}/{repository.Name}", StringComparison.OrdinalIgnoreCase);
        return new PullRequest(
            Long(pull, "id"),
            Str(pull, "title") ?? string.Empty,
            Str(pull, "author", "display_name") ?? "?",
            Str(pull, "source", "branch", "name") ?? string.Empty,
            Str(pull, "destination", "branch", "name") ?? string.Empty,
            Str(pull, "links", "html", "href") ?? repository.WebUrl,
            Bool(pull, "draft"),
            Date(pull, "created_on"),
            sameRepository ? null : sourceRepository);
    }
}
