using GitUi.Application.Abstractions;
using GitUi.Application.DTOs;
using GitUi.Application.Hosting;
using GitUi.Domain.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace GitUi.Application.Tests;

public class HostingServiceTests
{
    private readonly IHostingClient _github = Substitute.For<IHostingClient>();
    private readonly IHostingClient _azure = Substitute.For<IHostingClient>();
    private readonly IHostingTokenStore _tokens = Substitute.For<IHostingTokenStore>();
    private readonly IGitCredentialBridge _credentials = Substitute.For<IGitCredentialBridge>();
    private readonly IGitRepositoryReader _reader = Substitute.For<IGitRepositoryReader>();
    private readonly MemorySettingsStore _settings = new();
    private readonly HostingService _service;

    public HostingServiceTests()
    {
        _github.Provider.Returns(HostingProvider.GitHub);
        _azure.Provider.Returns(HostingProvider.AzureDevOps);
        _service = new HostingService(new[] { _github, _azure }, _tokens, _credentials, _settings, _reader);
    }

    [Fact]
    public async Task AddTokenAccount_ValidatesThenSavesTheAccountAndItsTokenSeparately()
    {
        _github.GetCurrentUserAsync(Arg.Is<HostingSession>(s => s.Credential.Secret == "ghp_x"), Arg.Any<CancellationToken>())
            .Returns(new HostingUser("octo", "Octo Cat"));

        var account = await _service.AddTokenAccountAsync(HostingProvider.GitHub, "  ghp_x ", null, null);

        Assert.Equal("octo", account.Login);
        Assert.Equal(HostingAuthMethod.PersonalAccessToken, account.AuthMethod);
        await _tokens.Received(1).SaveAsync(account.Id, "ghp_x", Arg.Any<CancellationToken>());
        Assert.Equal(account, Assert.Single(_settings.Current.HostingAccounts!));
    }

    [Fact]
    public async Task AddTokenAccount_RefusedToken_SavesNothing()
    {
        _github.GetCurrentUserAsync(Arg.Any<HostingSession>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HostingException("GitHub refused the credential (401).", 401));

        await Assert.ThrowsAsync<HostingException>(() => _service.AddTokenAccountAsync(HostingProvider.GitHub, "bad", null, null));

        await _tokens.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
        Assert.Null(_settings.Current.HostingAccounts);
    }

    [Fact]
    public async Task AddingTheSameAccountAgain_ReplacesIt()
    {
        _github.GetCurrentUserAsync(Arg.Any<HostingSession>(), Arg.Any<CancellationToken>()).Returns(new HostingUser("octo", null));

        var first = await _service.AddTokenAccountAsync(HostingProvider.GitHub, "one", null, null);
        var second = await _service.AddTokenAccountAsync(HostingProvider.GitHub, "two", null, null);

        Assert.Equal(second.Id, Assert.Single(_settings.Current.HostingAccounts!).Id);
        await _tokens.Received(1).DeleteAsync(first.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("contoso")]
    [InlineData("https://dev.azure.com/contoso/Project")]
    public async Task AzureDevOps_TakesTheOrganizationOrAPastedUrl(string organization)
    {
        _azure.GetCurrentUserAsync(Arg.Any<HostingSession>(), Arg.Any<CancellationToken>()).Returns(new HostingUser("me@contoso.com", "Me"));

        var account = await _service.AddTokenAccountAsync(HostingProvider.AzureDevOps, "pat", organization, null);

        Assert.Equal("contoso", account.Organization);
    }

    [Fact]
    public async Task AzureDevOps_WithoutOrganization_IsRefused() =>
        await Assert.ThrowsAsync<HostingException>(() => _service.AddTokenAccountAsync(HostingProvider.AzureDevOps, "pat", " ", null));

    [Fact]
    public async Task CredentialManagerAccount_UsesGcmCredentialAndApprovesIt()
    {
        var credential = new HostingCredential("gho_token", "octo");
        _credentials.FillAsync(new Uri("https://github.com/"), true, Arg.Any<CancellationToken>()).Returns(credential);
        _github.GetCurrentUserAsync(Arg.Any<HostingSession>(), Arg.Any<CancellationToken>()).Returns(new HostingUser("octo", null));

        var account = await _service.AddCredentialManagerAccountAsync(HostingProvider.GitHub, null);

        Assert.Equal(HostingAuthMethod.CredentialManager, account.AuthMethod);
        await _credentials.Received(1).ApproveAsync(new Uri("https://github.com/"), credential, Arg.Any<CancellationToken>());
        await _tokens.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
    }

    [Fact]
    public async Task CredentialManagerAccount_NoHelper_ExplainsTheTokenAlternative()
    {
        _credentials.FillAsync(Arg.Any<Uri>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns((HostingCredential?)null);

        var error = await Assert.ThrowsAsync<HostingException>(() => _service.AddCredentialManagerAccountAsync(HostingProvider.GitHub, null));

        Assert.Contains("personal access token", error.Message);
    }

    [Fact]
    public async Task FindHosting_PrefersOriginAndMatchesTheAzureOrganization()
    {
        _settings.Current = _settings.Current with
        {
            HostingAccounts = new[]
            {
                new HostingAccount("a1", HostingProvider.AzureDevOps, HostingAuthMethod.PersonalAccessToken, "me", Organization: "other"),
                new HostingAccount("a2", HostingProvider.AzureDevOps, HostingAuthMethod.PersonalAccessToken, "me", Organization: "Contoso"),
            },
        };
        _reader.GetRemotesAsync("/repo", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new GitRemote("fork", "https://github.com/me/app.git", "https://github.com/me/app.git"),
            new GitRemote("origin", "https://contoso@dev.azure.com/contoso/Web/_git/app", "https://contoso@dev.azure.com/contoso/Web/_git/app"),
        });

        var hosting = await _service.FindHostingAsync("/repo");

        Assert.NotNull(hosting);
        Assert.Equal("origin", hosting!.RemoteName);
        Assert.Equal(HostingProvider.AzureDevOps, hosting.Repository.Provider);
        Assert.Equal("a2", hosting.Account?.Id);
    }

    [Fact]
    public async Task FindHosting_LocalOnlyRemotes_IsNull()
    {
        _reader.GetRemotesAsync("/repo", Arg.Any<CancellationToken>()).Returns(new[] { new GitRemote("origin", "/srv/git/app.git", "/srv/git/app.git") });

        Assert.Null(await _service.FindHostingAsync("/repo"));
    }

    [Fact]
    public async Task PullRequests_WithoutAccount_SayWhatToConnect()
    {
        var hosting = new RepositoryHosting("origin", new RemoteRepositoryRef(HostingProvider.GitHub, "o", "r"), null);

        var error = await Assert.ThrowsAsync<HostingException>(() => _service.GetPullRequestsAsync(hosting));

        Assert.Contains("Preferences > Accounts", error.Message);
    }

    [Fact]
    public async Task PullRequests_UseTheStoredToken()
    {
        var account = new HostingAccount("id1", HostingProvider.GitHub, HostingAuthMethod.PersonalAccessToken, "octo");
        var repository = new RemoteRepositoryRef(HostingProvider.GitHub, "o", "r");
        _tokens.GetAsync("id1", Arg.Any<CancellationToken>()).Returns("ghp_saved");
        _github.GetPullRequestsAsync(Arg.Is<HostingSession>(s => s.Credential.Secret == "ghp_saved"), repository, Arg.Any<CancellationToken>())
            .Returns(new[] { new PullRequest(7, "Fix", "octo", "fix", "main", "https://github.com/o/r/pull/7", false, null) });

        var pulls = await _service.GetPullRequestsAsync(new RepositoryHosting("origin", repository, account));

        Assert.Equal(7, Assert.Single(pulls).Number);
    }

    [Fact]
    public async Task CreatePullRequest_SameSourceAndTarget_IsRefusedBeforeAnyCall()
    {
        var hosting = new RepositoryHosting("origin", new RemoteRepositoryRef(HostingProvider.GitHub, "o", "r"),
            new HostingAccount("id1", HostingProvider.GitHub, HostingAuthMethod.PersonalAccessToken, "octo"));

        await Assert.ThrowsAsync<HostingException>(() => _service.CreatePullRequestAsync(hosting, new NewPullRequest("main", "main", "T", "", false)));
        await _github.DidNotReceiveWithAnyArgs().CreatePullRequestAsync(default!, default!, default!);
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        public AppSettings Current { get; set; } = AppSettings.CreateDefault();

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
        {
            Current = update(Current);
            return Task.FromResult(Current);
        }
    }
}
