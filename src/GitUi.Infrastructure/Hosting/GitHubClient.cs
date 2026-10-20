using System.Net.Http.Headers;
using System.Text.Json;
using GitUi.Application.Hosting;

namespace GitUi.Infrastructure.Hosting;

/// <summary>github.com REST API v3.</summary>
public sealed class GitHubClient : HostingApiClient
{
    private const string Api = "https://api.github.com";

    public GitHubClient(HttpClient http) : base(http)
    {
    }

    public override HostingProvider Provider => HostingProvider.GitHub;

    protected override IReadOnlyList<AuthenticationHeaderValue> AuthorizationFor(HostingSession session) =>
        new[] { Bearer(session.Credential.Secret) };

    protected override void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    protected override string? NextPage(HttpResponseMessage response, JsonElement body) => NextFromLinkHeader(response);

    public override async Task<HostingUser> GetCurrentUserAsync(HostingSession session, CancellationToken cancellationToken = default)
    {
        var user = await GetAsync(session, $"{Api}/user", cancellationToken);
        return new HostingUser(Str(user, "login") ?? "?", Str(user, "name"));
    }

    public override async Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(HostingSession session, CancellationToken cancellationToken = default) =>
        await GetPagedAsync(session, $"{Api}/user/repos?per_page=100&sort=updated&affiliation=owner,collaborator,organization_member",
            page => Items(page),
            repo => new HostedRepository(
                Str(repo, "full_name") ?? "?",
                Str(repo, "name") ?? "?",
                Str(repo, "clone_url") ?? string.Empty,
                Str(repo, "description"),
                Bool(repo, "private"),
                Date(repo, "updated_at")),
            cancellationToken);

    public override async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default) =>
        await GetPagedAsync(session, $"{RepoUrl(repository)}/pulls?state=open&per_page=100",
            page => Items(page),
            pull => MapPullRequest(pull, repository),
            cancellationToken);

    public override async Task<string?> GetDefaultBranchAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default) =>
        Str(await GetAsync(session, RepoUrl(repository), cancellationToken), "default_branch");

    public override async Task<PullRequest> CreatePullRequestAsync(HostingSession session, RemoteRepositoryRef repository, NewPullRequest request, CancellationToken cancellationToken = default)
    {
        var created = await PostAsync(session, $"{RepoUrl(repository)}/pulls", new
        {
            title = request.Title,
            head = request.SourceBranch,
            @base = request.TargetBranch,
            body = request.Description,
            draft = request.IsDraft,
        }, cancellationToken);
        return MapPullRequest(created, repository);
    }

    private static string RepoUrl(RemoteRepositoryRef repository) => $"{Api}/repos/{Escape(repository.Owner)}/{Escape(repository.Name)}";

    private static PullRequest MapPullRequest(JsonElement pull, RemoteRepositoryRef repository)
    {
        var sourceRepository = Str(pull, "head", "repo", "full_name");
        var sameRepository = sourceRepository is null
            || string.Equals(sourceRepository, $"{repository.Owner}/{repository.Name}", StringComparison.OrdinalIgnoreCase);
        return new PullRequest(
            Long(pull, "number"),
            Str(pull, "title") ?? string.Empty,
            Str(pull, "user", "login") ?? "?",
            Str(pull, "head", "ref") ?? string.Empty,
            Str(pull, "base", "ref") ?? string.Empty,
            Str(pull, "html_url") ?? repository.WebUrl,
            Bool(pull, "draft"),
            Date(pull, "created_at"),
            sameRepository ? null : sourceRepository);
    }
}
