using System.Net;
using System.Text;
using System.Text.Json;
using GitUi.Application.Hosting;
using GitUi.Infrastructure.Hosting;
using Xunit;

namespace GitUi.Infrastructure.Tests;

/// <summary>The four REST clients against a recorded fake server: URLs, authentication, paging, mapping, errors.</summary>
public class HostingClientTests
{
    private sealed record Call(HttpMethod Method, string Url, string? Authorization, string? Body);

    /// <summary>Answers each request from <c>respond</c> and records it.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeServer(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<Call> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new Call(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
            return _respond(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HostingSession Session(HostingProvider provider, HostingAuthMethod method = HostingAuthMethod.PersonalAccessToken,
        string secret = "secret", string? username = null, string? organization = null) =>
        new(new HostingAccount("id", provider, method, "me", Organization: organization, Username: username), new HostingCredential(secret, username));

    private static string BasicOf(string user, string password) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    [Fact]
    public async Task GitHub_PullRequests_FollowLinkPagingAndMapForks()
    {
        var server = new FakeServer(request => request.RequestUri!.Query.Contains("page=2")
            ? Json("""[{"number":2,"title":"From fork","user":{"login":"bob"},"head":{"ref":"patch","repo":{"full_name":"bob/hello"}},"base":{"ref":"main"},"html_url":"https://github.com/octo/hello/pull/2","draft":true}]""")
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"number":1,"title":"Fix","user":{"login":"amy"},"head":{"ref":"fix","repo":{"full_name":"octo/hello"}},"base":{"ref":"main"},"html_url":"https://github.com/octo/hello/pull/1","draft":false,"created_at":"2026-10-01T10:00:00Z"}]"""),
                Headers = { { "Link", "<https://api.github.com/repos/octo/hello/pulls?state=open&per_page=100&page=2>; rel=\"next\", <https://api.github.com/repos/octo/hello/pulls?page=2>; rel=\"last\"" } },
            });
        var client = new GitHubClient(new HttpClient(server));

        var pulls = await client.GetPullRequestsAsync(Session(HostingProvider.GitHub), new RemoteRepositoryRef(HostingProvider.GitHub, "octo", "hello"));

        Assert.Equal(2, pulls.Count);
        Assert.Equal(new PullRequest(1, "Fix", "amy", "fix", "main", "https://github.com/octo/hello/pull/1", false, DateTimeOffset.Parse("2026-10-01T10:00:00Z")), pulls[0]);
        Assert.True(pulls[1].IsDraft);
        Assert.Equal("bob/hello", pulls[1].SourceRepository);
        Assert.Equal("https://api.github.com/repos/octo/hello/pulls?state=open&per_page=100", server.Calls[0].Url);
        Assert.Equal("Bearer secret", server.Calls[0].Authorization);
    }

    [Fact]
    public async Task GitHub_CreatePullRequest_PostsHeadAndBase()
    {
        var server = new FakeServer(_ => Json("""{"number":9,"title":"T","user":{"login":"me"},"head":{"ref":"feature"},"base":{"ref":"main"},"html_url":"https://github.com/o/r/pull/9"}""", HttpStatusCode.Created));
        var client = new GitHubClient(new HttpClient(server));

        var created = await client.CreatePullRequestAsync(Session(HostingProvider.GitHub), new RemoteRepositoryRef(HostingProvider.GitHub, "o", "r"),
            new NewPullRequest("feature", "main", "T", "Body", true));

        Assert.Equal(9, created.Number);
        var call = Assert.Single(server.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("https://api.github.com/repos/o/r/pulls", call.Url);
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal("feature", body.RootElement.GetProperty("head").GetString());
        Assert.Equal("main", body.RootElement.GetProperty("base").GetString());
        Assert.True(body.RootElement.GetProperty("draft").GetBoolean());
    }

    [Fact]
    public async Task Errors_CarryTheServiceMessageAndStatus()
    {
        var server = new FakeServer(_ => Json("""{"message":"Validation Failed","errors":[{"message":"A pull request already exists for o:feature."}]}""", HttpStatusCode.UnprocessableEntity));
        var client = new GitHubClient(new HttpClient(server));

        var error = await Assert.ThrowsAsync<HostingException>(() => client.CreatePullRequestAsync(Session(HostingProvider.GitHub),
            new RemoteRepositoryRef(HostingProvider.GitHub, "o", "r"), new NewPullRequest("feature", "main", "T", "", false)));

        Assert.Equal(422, error.StatusCode);
        Assert.Contains("A pull request already exists", error.Message);
    }

    [Fact]
    public async Task Unauthorized_SaysTheTokenIsWrong()
    {
        var client = new GitHubClient(new HttpClient(new FakeServer(_ => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized))));

        var error = await Assert.ThrowsAsync<HostingException>(() => client.GetCurrentUserAsync(Session(HostingProvider.GitHub)));

        Assert.Equal(401, error.StatusCode);
        Assert.Contains("Bad credentials", error.Message);
        Assert.Contains("wrong or expired", error.Message);
    }

    [Fact]
    public async Task GitLab_EncodesTheProjectPathAndPrefixesDrafts()
    {
        var server = new FakeServer(_ => Json("""{"iid":4,"title":"Draft: Feature","author":{"username":"me"},"source_branch":"feature","target_branch":"main","web_url":"https://gitlab.com/g/s/p/-/merge_requests/4","draft":true,"source_project_id":1,"target_project_id":1}""", HttpStatusCode.Created));
        var client = new GitLabClient(new HttpClient(server));

        var created = await client.CreatePullRequestAsync(Session(HostingProvider.GitLab), new RemoteRepositoryRef(HostingProvider.GitLab, "g/s", "p"),
            new NewPullRequest("feature", "main", "Feature", "", true));

        Assert.Equal("https://gitlab.com/api/v4/projects/g%2Fs%2Fp/merge_requests", server.Calls[0].Url);
        Assert.Contains("\"title\":\"Draft: Feature\"", server.Calls[0].Body);
        Assert.Equal(4, created.Number);
        Assert.True(created.IsDraft);
        Assert.False(created.IsFromFork);
    }

    [Fact]
    public async Task AzureDevOps_PatIsBasicAuth_AndBranchRefsAreShortened()
    {
        var server = new FakeServer(_ => Json("""{"count":1,"value":[{"pullRequestId":12,"title":"Login","createdBy":{"displayName":"Amy"},"sourceRefName":"refs/heads/feature/login","targetRefName":"refs/heads/main","isDraft":false,"creationDate":"2026-10-02T08:00:00Z"}]}"""));
        var client = new AzureDevOpsClient(new HttpClient(server));
        var repository = new RemoteRepositoryRef(HostingProvider.AzureDevOps, "contoso", "web app", "My Project");

        var pull = Assert.Single(await client.GetPullRequestsAsync(Session(HostingProvider.AzureDevOps, secret: "pat", organization: "contoso"), repository));

        Assert.Equal("https://dev.azure.com/contoso/My%20Project/_apis/git/repositories/web%20app/pullrequests?searchCriteria.status=active&$top=200&api-version=7.1", server.Calls[0].Url);
        Assert.Equal(BasicOf("", "pat"), server.Calls[0].Authorization);
        Assert.Equal("feature/login", pull.SourceBranch);
        Assert.Equal("main", pull.TargetBranch);
        Assert.Equal("https://dev.azure.com/contoso/My%20Project/_git/web%20app/pullrequest/12", pull.WebUrl);
    }

    [Fact]
    public async Task AzureDevOps_HtmlSignInPage_IsReportedAsARefusedCredential()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.NonAuthoritativeInformation) { Content = new StringContent("<html>Sign in</html>") });
        var client = new AzureDevOpsClient(new HttpClient(server));

        var error = await Assert.ThrowsAsync<HostingException>(() => client.GetCurrentUserAsync(Session(HostingProvider.AzureDevOps, organization: "contoso")));

        Assert.Contains("refused", error.Message);
    }

    [Fact]
    public async Task AzureDevOps_CurrentUserFromConnectionData()
    {
        var server = new FakeServer(_ => Json("""{"authenticatedUser":{"providerDisplayName":"Amy Doe","properties":{"Account":{"$type":"System.String","$value":"amy@contoso.com"}}}}"""));
        var client = new AzureDevOpsClient(new HttpClient(server));

        var user = await client.GetCurrentUserAsync(Session(HostingProvider.AzureDevOps, organization: "contoso"));

        Assert.Equal(new HostingUser("amy@contoso.com", "Amy Doe"), user);
        Assert.Equal("https://dev.azure.com/contoso/_apis/connectionData", server.Calls[0].Url);
    }

    [Fact]
    public async Task Bitbucket_CredentialManager_FallsBackFromBearerToBasicOn401()
    {
        var server = new FakeServer(request => request.Headers.Authorization!.Scheme == "Bearer"
            ? Json("""{"type":"error","error":{"message":"Unauthorized"}}""", HttpStatusCode.Unauthorized)
            : Json("""{"username":"amy","display_name":"Amy"}"""));
        var client = new BitbucketClient(new HttpClient(server));

        var user = await client.GetCurrentUserAsync(Session(HostingProvider.Bitbucket, HostingAuthMethod.CredentialManager, "app-password", "amy"));

        Assert.Equal("amy", user.Login);
        Assert.Equal(2, server.Calls.Count);
        Assert.Equal(BasicOf("amy", "app-password"), server.Calls[1].Authorization);
    }

    [Fact]
    public async Task Bitbucket_Repositories_FollowNextAndDropTheUserFromCloneUrls()
    {
        var server = new FakeServer(request => request.RequestUri!.Query.Contains("page=2")
            ? Json("""{"values":[{"full_name":"team/two","name":"two","is_private":false,"links":{"clone":[{"name":"https","href":"https://amy@bitbucket.org/team/two.git"}]}}]}""")
            : Json("""{"next":"https://api.bitbucket.org/2.0/repositories?role=member&page=2","values":[{"full_name":"team/one","name":"one","is_private":true,"links":{"clone":[{"name":"ssh","href":"git@bitbucket.org:team/one.git"},{"name":"https","href":"https://amy@bitbucket.org/team/one.git"}]}}]}"""));
        var client = new BitbucketClient(new HttpClient(server));

        var repositories = await client.GetRepositoriesAsync(Session(HostingProvider.Bitbucket, username: "amy@example.com"));

        Assert.Equal(new[] { "team/one", "team/two" }, repositories.Select(r => r.FullName));
        Assert.Equal("https://bitbucket.org/team/one.git", repositories[0].CloneUrl);
        Assert.True(repositories[0].IsPrivate);
        Assert.Equal(BasicOf("amy@example.com", "secret"), server.Calls[0].Authorization);
    }

    [Fact]
    public async Task TokenStore_RoundTripsAndDeletes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gitui-tokens-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileHostingTokenStore(directory);
            await store.SaveAsync("abc123", "ghp_secret");

            Assert.Equal("ghp_secret", await store.GetAsync("abc123"));
            Assert.DoesNotContain("ghp_secret", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "abc123.token"))),
                StringComparison.Ordinal);

            await store.DeleteAsync("abc123");
            Assert.Null(await store.GetAsync("abc123"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync("..\\x"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
