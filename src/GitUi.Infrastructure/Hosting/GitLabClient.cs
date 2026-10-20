using System.Net.Http.Headers;
using System.Text.Json;
using GitUi.Application.Hosting;

namespace GitUi.Infrastructure.Hosting;

/// <summary>gitlab.com REST API v4. Pull requests are merge requests there.</summary>
public sealed class GitLabClient : HostingApiClient
{
    private const string Api = "https://gitlab.com/api/v4";

    public GitLabClient(HttpClient http) : base(http)
    {
    }

    public override HostingProvider Provider => HostingProvider.GitLab;

    // Personal access tokens and OAuth tokens are both accepted as bearer tokens.
    protected override IReadOnlyList<AuthenticationHeaderValue> AuthorizationFor(HostingSession session) =>
        new[] { Bearer(session.Credential.Secret) };

    protected override string? NextPage(HttpResponseMessage response, JsonElement body) => NextFromLinkHeader(response);

    public override async Task<HostingUser> GetCurrentUserAsync(HostingSession session, CancellationToken cancellationToken = default)
    {
        var user = await GetAsync(session, $"{Api}/user", cancellationToken);
        return new HostingUser(Str(user, "username") ?? "?", Str(user, "name"));
    }

    public override async Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(HostingSession session, CancellationToken cancellationToken = default) =>
        await GetPagedAsync(session, $"{Api}/projects?membership=true&simple=true&per_page=100&order_by=last_activity_at",
            page => Items(page),
            project => new HostedRepository(
                Str(project, "path_with_namespace") ?? "?",
                Str(project, "name") ?? "?",
                Str(project, "http_url_to_repo") ?? string.Empty,
                Str(project, "description"),
                Str(project, "visibility") is not "public",
                Date(project, "last_activity_at")),
            cancellationToken);

    public override async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default) =>
        await GetPagedAsync(session, $"{ProjectUrl(repository)}/merge_requests?state=opened&per_page=100",
            page => Items(page),
            mergeRequest => MapMergeRequest(mergeRequest, repository),
            cancellationToken);

    public override async Task<string?> GetDefaultBranchAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default) =>
        Str(await GetAsync(session, ProjectUrl(repository), cancellationToken), "default_branch");

    public override async Task<PullRequest> CreatePullRequestAsync(HostingSession session, RemoteRepositoryRef repository, NewPullRequest request, CancellationToken cancellationToken = default)
    {
        // GitLab marks a draft by its title prefix.
        var title = request.IsDraft && !request.Title.StartsWith("Draft:", StringComparison.OrdinalIgnoreCase) ? $"Draft: {request.Title}" : request.Title;
        var created = await PostAsync(session, $"{ProjectUrl(repository)}/merge_requests", new
        {
            source_branch = request.SourceBranch,
            target_branch = request.TargetBranch,
            title,
            description = request.Description,
        }, cancellationToken);
        return MapMergeRequest(created, repository);
    }

    /// <summary>Projects are addressed by their URL-encoded full path ("group%2Fsub%2Fname").</summary>
    private static string ProjectUrl(RemoteRepositoryRef repository) => $"{Api}/projects/{Escape(repository.FullPath)}";

    private static PullRequest MapMergeRequest(JsonElement mergeRequest, RemoteRepositoryRef repository)
    {
        var fromFork = Long(mergeRequest, "source_project_id") != Long(mergeRequest, "target_project_id");
        return new PullRequest(
            Long(mergeRequest, "iid"),
            Str(mergeRequest, "title") ?? string.Empty,
            Str(mergeRequest, "author", "username") ?? "?",
            Str(mergeRequest, "source_branch") ?? string.Empty,
            Str(mergeRequest, "target_branch") ?? string.Empty,
            Str(mergeRequest, "web_url") ?? repository.WebUrl,
            Bool(mergeRequest, "draft") || Bool(mergeRequest, "work_in_progress"),
            Date(mergeRequest, "created_at"),
            fromFork ? "fork" : null);
    }
}
