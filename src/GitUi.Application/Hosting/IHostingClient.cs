namespace GitUi.Application.Hosting;

/// <summary>One hosting service's REST API.</summary>
public interface IHostingClient
{
    HostingProvider Provider { get; }

    /// <summary>Who the credential belongs to; also how a new account is validated.</summary>
    Task<HostingUser> GetCurrentUserAsync(HostingSession session, CancellationToken cancellationToken = default);

    /// <summary>Repositories the account is a member of, most recently updated first.</summary>
    Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(HostingSession session, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default);

    Task<string?> GetDefaultBranchAsync(HostingSession session, RemoteRepositoryRef repository, CancellationToken cancellationToken = default);

    Task<PullRequest> CreatePullRequestAsync(HostingSession session, RemoteRepositoryRef repository, NewPullRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Keeps account tokens out of the settings file (encrypted on Windows, owner-only file elsewhere).</summary>
public interface IHostingTokenStore
{
    Task SaveAsync(string accountId, string token, CancellationToken cancellationToken = default);

    Task<string?> GetAsync(string accountId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string accountId, CancellationToken cancellationToken = default);
}

/// <summary>Git's credential helpers (Git Credential Manager), through <c>git credential</c>.</summary>
public interface IGitCredentialBridge
{
    /// <summary>
    /// <c>git credential fill</c> for <paramref name="url"/>: what the helper has stored, or, when
    /// <paramref name="interactive"/>, its sign-in (GCM opens the browser). Null when nothing was given.
    /// </summary>
    Task<HostingCredential?> FillAsync(Uri url, bool interactive, CancellationToken cancellationToken = default);

    /// <summary><c>git credential approve</c>: tells the helper the credential worked, so it keeps it.</summary>
    Task ApproveAsync(Uri url, HostingCredential credential, CancellationToken cancellationToken = default);
}
