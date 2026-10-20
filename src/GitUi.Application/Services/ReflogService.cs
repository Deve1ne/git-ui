using System.Globalization;
using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>One move of HEAD: where it went, how (commit, checkout, reset, rebase…) and when.</summary>
/// <param name="Selector">git's name for the entry, e.g. <c>HEAD@{3}</c>.</param>
public sealed record ReflogEntry(string Sha, string Selector, string Message, DateTimeOffset? Date)
{
    public string ShortSha => Sha.Length > 7 ? Sha[..7] : Sha;

    /// <summary>The kind of move ("commit", "checkout", "reset"...), the message's first word.</summary>
    public string Action
    {
        get
        {
            var colon = Message.IndexOf(':');
            return colon > 0 ? Message[..colon] : Message;
        }
    }
}

/// <summary>
/// The reflog: every position HEAD has had, including commits no branch reaches any more (after a
/// reset, a rebase, a deleted branch) — the way to get lost work back.
/// </summary>
public sealed class ReflogService
{
    private const char Separator = '\u001f';
    private readonly IGitCommandRunner _git;

    public ReflogService(IGitCommandRunner git) => _git = git;

    public async Task<IReadOnlyList<ReflogEntry>> GetAsync(string path, string reference = "HEAD", int maxCount = 500, CancellationToken cancellationToken = default)
    {
        // With --date, %gd is "HEAD@{<when the move happened>}": the reflog's own time, not the commit's.
        var result = await _git.TryRunAsync(path, new[]
        {
            "reflog", "show", $"--max-count={maxCount}", "--date=iso-strict", "--format=%H%x1f%gd%x1f%gs", reference, "--",
        }, cancellationToken: cancellationToken);

        // An empty repository, or a ref without a reflog, has nothing to show.
        return result.Success ? Parse(result.Output, reference) : Array.Empty<ReflogEntry>();
    }

    public static IReadOnlyList<ReflogEntry> Parse(string output, string reference = "HEAD") => output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\r').Split(Separator))
        .Where(parts => parts.Length >= 3)
        .Select((parts, index) => new ReflogEntry(parts[0], $"{reference}@{{{index}}}", parts[2], DateIn(parts[1])))
        .ToList();

    private static DateTimeOffset? DateIn(string selector)
    {
        var open = selector.IndexOf('{');
        var close = selector.LastIndexOf('}');
        return open >= 0 && close > open
            && DateTimeOffset.TryParse(selector[(open + 1)..close], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
