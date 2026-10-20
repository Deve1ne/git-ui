using GitUi.Application.Abstractions;
using GitUi.Domain.Exceptions;

namespace GitUi.Application.Services;

public enum PushOutcome
{
    Pushed,

    /// <summary>The branch has no upstream: ask where to push (see <see cref="PushService.SuggestUpstreamAsync"/>).</summary>
    NeedsUpstream,

    /// <summary>The remote has commits this branch does not: offer a force push with lease.</summary>
    Rejected,
}

/// <summary>
/// The toolbar Push, GitKraken style: a branch without upstream asks where to go instead of
/// failing, and a rejected push (the remote moved on) offers a force push with lease.
/// </summary>
public sealed class PushService
{
    private readonly IGitRepositoryReader _reader;
    private readonly IGitRepositoryWriter _writer;

    public PushService(IGitRepositoryReader reader, IGitRepositoryWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public async Task<PushOutcome> PushAsync(string path, CancellationToken cancellationToken = default)
    {
        var head = (await _reader.GetBranchesAsync(path, cancellationToken)).FirstOrDefault(b => b.IsLocal && b.IsHead)
            ?? throw new InvalidOperationException("HEAD is detached: check out a branch to push it.");
        if (head.TrackingBranch is null)
        {
            return PushOutcome.NeedsUpstream;
        }

        try
        {
            await _writer.PushAsync(path, force: false, cancellationToken);
            return PushOutcome.Pushed;
        }
        catch (GitOperationException ex) when (IsRejection(ex.Details))
        {
            return PushOutcome.Rejected;
        }
    }

    /// <summary>"origin/&lt;branch&gt;" (or the first remote), the default GitKraken proposes.</summary>
    public async Task<string> SuggestUpstreamAsync(string path, CancellationToken cancellationToken = default)
    {
        var remotes = await _reader.GetRemotesAsync(path, cancellationToken);
        var remote = remotes.FirstOrDefault()?.Name
            ?? throw new InvalidOperationException("This repository has no remote to push to: add one first.");
        var branch = (await _reader.GetBranchesAsync(path, cancellationToken)).First(b => b.IsLocal && b.IsHead).Name;
        return $"{remote}/{branch}";
    }

    /// <summary>Pushes the current branch to <paramref name="upstream"/> ("remote/branch") and tracks it.</summary>
    public async Task PushWithUpstreamAsync(string path, string upstream, CancellationToken cancellationToken = default)
    {
        var remotes = await _reader.GetRemotesAsync(path, cancellationToken);
        var slash = upstream.IndexOf('/');
        var remote = slash > 0 ? remotes.FirstOrDefault(r => r.Name == upstream[..slash]) : null;
        if (remote is null || slash == upstream.Length - 1)
        {
            throw new InvalidOperationException($"'{upstream}' is not <remote>/<branch> for a remote of this repository ({string.Join(", ", remotes.Select(r => r.Name))}).");
        }

        var local = (await _reader.GetBranchesAsync(path, cancellationToken)).First(b => b.IsLocal && b.IsHead).Name;
        await _writer.PushBranchAsync(path, remote.Name, local, upstream[(slash + 1)..], setUpstream: true, cancellationToken);
    }

    public Task ForcePushAsync(string path, CancellationToken cancellationToken = default)
        => _writer.PushAsync(path, force: true, cancellationToken);

    private static bool IsRejection(string? details) =>
        details is not null
        && (details.Contains("[rejected]", StringComparison.Ordinal)
            || details.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
            || details.Contains("fetch first", StringComparison.OrdinalIgnoreCase));
}
