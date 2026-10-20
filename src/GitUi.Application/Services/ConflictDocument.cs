using System.Text;

namespace GitUi.Application.Services;

/// <summary>Where a line of the merged output comes from, for highlighting it.</summary>
public enum MergeLineOrigin
{
    /// <summary>Merged by git without conflict (same on both sides).</summary>
    Stable,

    /// <summary>Taken from the checked-out side (A, current).</summary>
    Ours,

    /// <summary>Taken from the merged-in side (B, incoming).</summary>
    Theirs,

    /// <summary>Part of a conflict nobody resolved yet, conflict markers included.</summary>
    Unresolved,
}

/// <param name="ConflictIndex">Index of the conflict the line belongs to, or -1 for stable lines.</param>
/// <param name="LineNumber">1-based line number in the output file.</param>
public sealed record MergeOutputLine(string Text, MergeLineOrigin Origin, int ConflictIndex, int LineNumber = 0);

/// <summary>A run of lines git merged without conflict.</summary>
public sealed record StableSegment(IReadOnlyList<string> Lines);

/// <summary>
/// One conflict: the lines of the checked-out side ("ours", current) and of the merged-in side
/// ("theirs", incoming), plus the common ancestor's when git wrote diff3-style markers.
/// </summary>
public sealed record ConflictChunk(
    IReadOnlyList<string> Ours,
    IReadOnlyList<string> Theirs,
    IReadOnlyList<string>? Base,
    string OursLabel,
    string TheirsLabel);

/// <summary>Which lines of a conflict go to the output: the taken "ours" lines, then the taken "theirs" lines.</summary>
public sealed record ConflictResolution(IReadOnlySet<int> TakenOurs, IReadOnlySet<int> TakenTheirs)
{
    public static ConflictResolution None { get; } = new(new HashSet<int>(), new HashSet<int>());

    public static ConflictResolution AllOurs(ConflictChunk chunk) => new(Enumerable.Range(0, chunk.Ours.Count).ToHashSet(), new HashSet<int>());

    public static ConflictResolution AllTheirs(ConflictChunk chunk) => new(new HashSet<int>(), Enumerable.Range(0, chunk.Theirs.Count).ToHashSet());
}

/// <summary>
/// A conflicted file split on git's conflict markers (<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>, optional
/// <c>|||||||</c>, <c>=======</c>, <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>) into stable segments and
/// conflict chunks. <see cref="Compose"/> rebuilds the file from a choice per chunk.
/// </summary>
public sealed class ConflictDocument
{
    private const string OursMarker = "<<<<<<<";
    private const string BaseMarker = "|||||||";
    private const string Separator = "=======";
    private const string TheirsMarker = ">>>>>>>";

    private ConflictDocument(IReadOnlyList<object> segments, string newLine, bool endsWithNewLine)
    {
        Segments = segments;
        NewLine = newLine;
        EndsWithNewLine = endsWithNewLine;
    }

    /// <summary>In file order; each item is a <see cref="StableSegment"/> or a <see cref="ConflictChunk"/>.</summary>
    public IReadOnlyList<object> Segments { get; }

    public IReadOnlyList<ConflictChunk> Conflicts => Segments.OfType<ConflictChunk>().ToList();

    public string NewLine { get; }

    public bool EndsWithNewLine { get; }

    public static ConflictDocument Parse(string text)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewLine = text.EndsWith('\n');
        var body = endsWithNewLine ? text[..^(text.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : 1)] : text;
        var lines = body.Length == 0 ? Array.Empty<string>() : body.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        var segments = new List<object>();
        var stable = new List<string>();
        var index = 0;
        while (index < lines.Length)
        {
            if (!lines[index].StartsWith(OursMarker, StringComparison.Ordinal) || !TryReadConflict(lines, index, out var chunk, out var next))
            {
                stable.Add(lines[index]);
                index++;
                continue;
            }

            if (stable.Count > 0)
            {
                segments.Add(new StableSegment(stable.ToList()));
                stable.Clear();
            }

            segments.Add(chunk!);
            index = next;
        }

        if (stable.Count > 0)
        {
            segments.Add(new StableSegment(stable));
        }

        return new ConflictDocument(segments, newLine, endsWithNewLine);
    }

    /// <summary>
    /// Builds the resolved file. <paramref name="resolutions"/> has one entry per conflict, in order;
    /// a conflict without a resolution keeps its markers so nothing is silently lost.
    /// </summary>
    public string Compose(IReadOnlyList<ConflictResolution?> resolutions)
    {
        var output = ComposeLines(resolutions).Select(l => l.Text).ToList();
        var text = new StringBuilder(string.Join(NewLine, output));
        if (EndsWithNewLine && output.Count > 0)
        {
            text.Append(NewLine);
        }

        return text.ToString();
    }

    /// <summary>
    /// The lines <see cref="Compose"/> writes, each tagged with where it comes from, so the merge
    /// tool can highlight the output by side.
    /// </summary>
    public IReadOnlyList<MergeOutputLine> ComposeLines(IReadOnlyList<ConflictResolution?> resolutions)
    {
        var output = new List<MergeOutputLine>();
        var conflictIndex = 0;
        foreach (var segment in Segments)
        {
            if (segment is StableSegment stable)
            {
                output.AddRange(stable.Lines.Select(l => new MergeOutputLine(l, MergeLineOrigin.Stable, -1)));
                continue;
            }

            var chunk = (ConflictChunk)segment;
            var index = conflictIndex++;
            var resolution = index < resolutions.Count ? resolutions[index] : null;

            if (resolution is null)
            {
                MergeOutputLine Unresolved(string text) => new(text, MergeLineOrigin.Unresolved, index);
                output.Add(Unresolved($"{OursMarker} {chunk.OursLabel}".TrimEnd()));
                output.AddRange(chunk.Ours.Select(Unresolved));
                output.Add(Unresolved(Separator));
                output.AddRange(chunk.Theirs.Select(Unresolved));
                output.Add(Unresolved($"{TheirsMarker} {chunk.TheirsLabel}".TrimEnd()));
                continue;
            }

            output.AddRange(chunk.Ours.Where((_, i) => resolution.TakenOurs.Contains(i)).Select(l => new MergeOutputLine(l, MergeLineOrigin.Ours, index)));
            output.AddRange(chunk.Theirs.Where((_, i) => resolution.TakenTheirs.Contains(i)).Select(l => new MergeOutputLine(l, MergeLineOrigin.Theirs, index)));
        }

        return output.Select((line, i) => line with { LineNumber = i + 1 }).ToList();
    }

    private static bool TryReadConflict(string[] lines, int start, out ConflictChunk? chunk, out int next)
    {
        chunk = null;
        next = start;
        var ours = new List<string>();
        var theirs = new List<string>();
        List<string>? baseLines = null;
        var section = 0; // 0 ours, 1 base, 2 theirs

        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (section == 0 && line.StartsWith(BaseMarker, StringComparison.Ordinal))
            {
                section = 1;
                baseLines = new List<string>();
            }
            else if (section < 2 && line == Separator)
            {
                section = 2;
            }
            else if (section == 2 && line.StartsWith(TheirsMarker, StringComparison.Ordinal))
            {
                chunk = new ConflictChunk(ours, theirs, baseLines, Label(lines[start], OursMarker), Label(line, TheirsMarker));
                next = i + 1;
                return true;
            }
            else
            {
                (section switch { 0 => ours, 1 => baseLines!, _ => theirs }).Add(line);
            }
        }

        // An opening marker without its separator and closing marker is just text.
        return false;
    }

    private static string Label(string markerLine, string marker) => markerLine.Length > marker.Length ? markerLine[marker.Length..].Trim() : string.Empty;
}
