using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

public enum BisectMark { Good, Bad, Skip }

/// <summary>Where a <c>git bisect</c> session stands.</summary>
/// <param name="Bad">The commit marked bad (the newest known broken one), once marked.</param>
/// <param name="Good">The commits marked good.</param>
/// <param name="Current">The commit checked out for testing (HEAD).</param>
/// <param name="FirstBad">Set when bisect has narrowed it down to one commit.</param>
/// <param name="RevisionsLeft">Commits still to test, once a bad and a good commit are known.</param>
/// <param name="StepsLeft">Roughly how many more marks it takes.</param>
public sealed record BisectState(
    bool IsActive,
    string? Bad,
    IReadOnlyList<string> Good,
    string? Current,
    string? FirstBad,
    int? RevisionsLeft,
    int? StepsLeft)
{
    public static readonly BisectState Inactive = new(false, null, Array.Empty<string>(), null, null, null, null);

    public bool IsFinished => FirstBad is not null;
}

/// <summary>
/// <c>git bisect</c>: mark commits good or bad (the first mark starts the session), git checks out
/// the next commit to test until one commit is left — the first bad one — then reset.
/// </summary>
public sealed class BisectService
{
    private readonly IGitCommandRunner _git;

    public BisectService(IGitCommandRunner git) => _git = git;

    public async Task<BisectState> GetStateAsync(string path, CancellationToken cancellationToken = default)
    {
        var startFile = await _git.RunAsync(path, new[] { "rev-parse", "--git-path", "BISECT_START" }, cancellationToken: cancellationToken);
        if (!File.Exists(Path.IsPathRooted(startFile) ? startFile : Path.Combine(path, startFile)))
        {
            return BisectState.Inactive;
        }

        var bad = await RevisionAsync(path, "refs/bisect/bad", cancellationToken);
        var goodRefs = await _git.RunAsync(path, new[] { "for-each-ref", "--format=%(objectname)", "refs/bisect/good-*" }, cancellationToken: cancellationToken);
        var good = goodRefs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var current = await RevisionAsync(path, "HEAD", cancellationToken);

        string? firstBad = null;
        int? revisionsLeft = null;
        int? stepsLeft = null;
        if (bad is not null && good.Count > 0)
        {
            // The candidates: commits reachable from the bad one but from no good one, skipped ones left out.
            var arguments = new List<string> { "rev-list", bad, "--not" };
            arguments.AddRange(good);
            var candidates = await _git.TryRunAsync(path, arguments, cancellationToken: cancellationToken);
            var skipped = (await _git.RunAsync(path, new[] { "for-each-ref", "--format=%(objectname)", "refs/bisect/skip-*" }, cancellationToken: cancellationToken))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
            var count = candidates.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Count(sha => !skipped.Contains(sha) || sha == bad);
            if (count <= 1)
            {
                // Only the bad commit is left: it is the first bad one.
                firstBad = bad;
                revisionsLeft = 0;
                stepsLeft = 0;
            }
            else
            {
                // git's own figures, as "git bisect" prints them: revisions left after the one being tested.
                arguments.Insert(1, "--bisect-vars");
                var vars = await _git.TryRunAsync(path, arguments, cancellationToken: cancellationToken);
                var values = vars.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Trim('\'', '"'));
                revisionsLeft = values.TryGetValue("bisect_nr", out var nr) && int.TryParse(nr, out var n) ? n : count - 1;
                stepsLeft = values.TryGetValue("bisect_steps", out var steps) && int.TryParse(steps, out var s) ? s : (int)Math.Ceiling(Math.Log2(count));
            }
        }
        return new BisectState(true, bad, good, current, firstBad, revisionsLeft, stepsLeft);
    }

    /// <summary>
    /// Marks <paramref name="sha"/> (starting the session when none is active) and returns git's
    /// summary line ("Bisecting: 3 revisions left…" or "… is the first bad commit").
    /// </summary>
    public async Task<string> MarkAsync(string path, string sha, BisectMark mark, CancellationToken cancellationToken = default)
    {
        if (!(await GetStateAsync(path, cancellationToken)).IsActive)
        {
            await _git.RunAsync(path, new[] { "bisect", "start" }, cancellationToken: cancellationToken);
        }

        var output = await _git.RunAsync(path, new[] { "bisect", mark.ToString().ToLowerInvariant(), sha }, cancellationToken: cancellationToken);
        return Summary(output);
    }

    /// <summary>Ends the session and goes back to the branch it started from.</summary>
    public Task ResetAsync(string path, CancellationToken cancellationToken = default)
        => _git.RunAsync(path, new[] { "bisect", "reset" }, cancellationToken: cancellationToken);

    /// <summary>The line of git's output worth showing.</summary>
    public static string Summary(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault(l => l.Contains("is the first bad commit", StringComparison.Ordinal))
            ?? lines.FirstOrDefault(l => l.StartsWith("Bisecting:", StringComparison.Ordinal))
            ?? lines.FirstOrDefault()
            ?? "Marked.";
    }

    private async Task<string?> RevisionAsync(string path, string revision, CancellationToken cancellationToken)
    {
        var result = await _git.TryRunAsync(path, new[] { "rev-parse", "--verify", "--quiet", revision }, cancellationToken: cancellationToken);
        return result.Success && result.Output.Length > 0 ? result.Output : null;
    }
}
