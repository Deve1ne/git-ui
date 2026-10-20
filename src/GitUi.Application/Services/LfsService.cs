using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>Git LFS in a repository: whether git-lfs is installed, and the patterns it tracks.</summary>
/// <param name="FileCount">Files of the checked-out commit stored in LFS.</param>
public sealed record LfsStatus(bool IsInstalled, IReadOnlyList<string> TrackedPatterns, int FileCount)
{
    public static readonly LfsStatus NotInstalled = new(false, Array.Empty<string>(), 0);

    public bool IsUsed => TrackedPatterns.Count > 0 || FileCount > 0;
}

/// <summary>
/// Git LFS: track / untrack file patterns (written to .gitattributes, which is staged), and pull,
/// fetch or prune the large files. Clone, checkout and push already go through LFS by themselves
/// once its hooks and filters are installed.
/// </summary>
public sealed class LfsService
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(30);
    private readonly IGitCommandRunner _git;
    private bool? _installed;

    public LfsService(IGitCommandRunner git) => _git = git;

    public async Task<bool> IsInstalledAsync(string path, CancellationToken cancellationToken = default)
    {
        _installed ??= (await _git.TryRunAsync(path, new[] { "lfs", "version" }, cancellationToken: cancellationToken)).Success;
        return _installed.Value;
    }

    public async Task<LfsStatus> GetStatusAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!await IsInstalledAsync(path, cancellationToken))
        {
            return LfsStatus.NotInstalled;
        }

        var track = await _git.TryRunAsync(path, new[] { "lfs", "track" }, cancellationToken: cancellationToken);
        var files = await _git.TryRunAsync(path, new[] { "lfs", "ls-files", "--name-only" }, cancellationToken: cancellationToken);
        var fileCount = files.Success ? files.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : 0;
        return new LfsStatus(true, track.Success ? ParseTrackedPatterns(track.Output) : Array.Empty<string>(), fileCount);
    }

    /// <summary>
    /// The patterns of <c>git lfs track</c>'s listing: "Listing tracked patterns" then indented
    /// "    *.psd (.gitattributes)" lines, up to "Listing excluded patterns".
    /// </summary>
    public static IReadOnlyList<string> ParseTrackedPatterns(string output)
    {
        var patterns = new List<string>();
        var inTracked = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("Listing tracked", StringComparison.Ordinal))
            {
                inTracked = true;
                continue;
            }

            if (line.StartsWith("Listing excluded", StringComparison.Ordinal))
            {
                inTracked = false;
                continue;
            }

            if (inTracked && line.StartsWith(' ') && line.Trim().Length > 0)
            {
                var text = line.Trim();
                var source = text.LastIndexOf(" (", StringComparison.Ordinal);
                patterns.Add(source > 0 ? text[..source] : text);
            }
        }

        return patterns;
    }

    /// <summary>Installs the LFS hooks in this repository, then tracks the pattern and stages .gitattributes.</summary>
    public async Task TrackAsync(string path, string pattern, CancellationToken cancellationToken = default)
    {
        await _git.RunAsync(path, new[] { "lfs", "install", "--local" }, cancellationToken: cancellationToken);
        await _git.RunAsync(path, new[] { "lfs", "track", "--", pattern.Trim() }, cancellationToken: cancellationToken);
        await _git.RunAsync(path, new[] { "add", "--", ".gitattributes" }, cancellationToken: cancellationToken);
    }

    public async Task UntrackAsync(string path, string pattern, CancellationToken cancellationToken = default)
    {
        await _git.RunAsync(path, new[] { "lfs", "untrack", "--", pattern }, cancellationToken: cancellationToken);
        await _git.RunAsync(path, new[] { "add", "--", ".gitattributes" }, cancellationToken: cancellationToken);
    }

    /// <summary>Installs the hooks (pre-push, post-checkout…) git-lfs needs in this repository.</summary>
    public Task InstallAsync(string path, CancellationToken cancellationToken = default)
        => _git.RunAsync(path, new[] { "lfs", "install", "--local" }, cancellationToken: cancellationToken);

    /// <summary>Downloads the large files of the checked-out commit and puts them in the working tree.</summary>
    public Task PullAsync(string path, CancellationToken cancellationToken = default)
        => _git.RunAsync(path, new[] { "lfs", "pull" }, NetworkTimeout, cancellationToken);

    public Task FetchAsync(string path, CancellationToken cancellationToken = default)
        => _git.RunAsync(path, new[] { "lfs", "fetch" }, NetworkTimeout, cancellationToken);

    /// <summary>Deletes local copies of old large files that are already on the server.</summary>
    public Task<string> PruneAsync(string path, CancellationToken cancellationToken = default)
        => _git.RunAsync(path, new[] { "lfs", "prune" }, NetworkTimeout, cancellationToken);
}
