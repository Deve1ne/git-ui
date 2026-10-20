using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

public sealed class RemoteService
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(30);
    private readonly IGitRepositoryWriter _writer;
    private readonly IGitCommandRunner? _git;
    private readonly ISettingsStore? _settings;

    public RemoteService(IGitRepositoryWriter writer, IGitCommandRunner? git = null, ISettingsStore? settings = null)
    {
        _writer = writer;
        _git = git;
        _settings = settings;
    }

    public Task FetchAsync(string path, bool prune, CancellationToken cancellationToken = default)
        => _writer.FetchAsync(path, prune, cancellationToken);

    /// <summary>The saved fetch options (prune, tags, submodules); the defaults when none were saved.</summary>
    public async Task<GitFetchOptions> GetFetchOptionsAsync(CancellationToken cancellationToken = default) =>
        _settings is null ? GitFetchOptions.Default : (await _settings.LoadAsync(cancellationToken)).FetchOptions ?? GitFetchOptions.Default;

    public async Task SaveFetchOptionsAsync(GitFetchOptions options, CancellationToken cancellationToken = default)
    {
        if (_settings is not null)
        {
            await _settings.UpdateAsync(s => s with { FetchOptions = options }, cancellationToken);
        }
    }

    /// <summary>
    /// Fetches every remote, or <paramref name="remote"/>, with the saved fetch options. Returns the
    /// tags git refused to update because they were moved on the server ("would clobber existing
    /// tag"): everything else was fetched, so that is not a failure — <see cref="UpdateMovedTagsAsync"/>
    /// takes the server's version when the user agrees.
    /// </summary>
    public async Task<IReadOnlyList<string>> FetchWithOptionsAsync(string path, string? remote = null, CancellationToken cancellationToken = default)
    {
        var options = await GetFetchOptionsAsync(cancellationToken);
        if (_git is null)
        {
            await _writer.FetchAsync(path, options.Prune, cancellationToken);
            return Array.Empty<string>();
        }

        var arguments = options.ToArguments(remote);
        var result = await _git.TryRunAsync(path, arguments, NetworkTimeout, cancellationToken);
        if (result.Success)
        {
            return Array.Empty<string>();
        }

        var moved = MovedTags(result.Output + "\n" + result.Error);
        if (moved.Count > 0 && !HasOtherFailure(result.Output + "\n" + result.Error))
        {
            return moved;
        }

        throw _git.ToException(arguments, result);
    }

    /// <summary>Fetches again with --force, so local tags moved on the server take the server's version.</summary>
    public async Task UpdateMovedTagsAsync(string path, string? remote = null, CancellationToken cancellationToken = default)
    {
        if (_git is null)
        {
            return;
        }

        var options = await GetFetchOptionsAsync(cancellationToken);
        await _git.RunAsync(path, options.ToArguments(remote, force: true), NetworkTimeout, cancellationToken);
    }

    /// <summary>Tags of " ! [rejected]  3.11.2.2 -> 3.11.2.2  (would clobber existing tag)" lines.</summary>
    public static IReadOnlyList<string> MovedTags(string output) => output
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.StartsWith("! [rejected]", StringComparison.Ordinal) && line.Contains("would clobber existing tag", StringComparison.Ordinal))
        .Select(line => line["! [rejected]".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
        .Distinct()
        .ToList();

    /// <summary>A rejection or error other than moved tags (and git's "could not fetch" that follows them with --all).</summary>
    private static bool HasOtherFailure(string output) => output
        .Split('\n')
        .Select(line => line.Trim())
        .Any(line => line.StartsWith("fatal:", StringComparison.Ordinal)
            || (line.StartsWith("! ", StringComparison.Ordinal) && !line.Contains("would clobber existing tag", StringComparison.Ordinal))
            || (line.StartsWith("error:", StringComparison.Ordinal)
                && !line.StartsWith("error: could not fetch", StringComparison.Ordinal)
                && !line.StartsWith("error: some local refs could not be updated", StringComparison.Ordinal)));

    public Task PullAsync(string path, GitPullMode mode = GitPullMode.Merge, CancellationToken cancellationToken = default)
        => _writer.PullAsync(path, mode, cancellationToken);

    public Task PushAsync(string path, bool force, CancellationToken cancellationToken = default)
        => _writer.PushAsync(path, force, cancellationToken);
}
