using GitUi.Domain.ValueObjects;

namespace GitUi.Application.Services;

/// <summary>One row of a side-by-side diff: the old line on the left, the new one on the right.</summary>
/// <param name="Header">The hunk header text on a header row; null on line rows.</param>
/// <param name="Left">Null where the old side has no line (an added line, or padding).</param>
public sealed record SideBySideRow(string? Header, int HunkIndex, SideBySideCell? Left, SideBySideCell? Right)
{
    public bool IsHunkHeader => Header is not null;
}

public sealed record SideBySideCell(int LineNumber, DiffLineKind Kind, string Text);

/// <summary>Diff presentation options GitKraken offers: ignore whitespace and the split view.</summary>
public static class DiffLayout
{
    /// <summary>
    /// Approximates <c>git diff -w</c>: a block of removed lines followed by as many added lines that
    /// only differ in whitespace becomes context (showing the new text); hunks left with no change
    /// are dropped. The result is for reading only — patches must be built from the real hunks.
    /// </summary>
    public static IReadOnlyList<DiffHunk> IgnoreWhitespace(IReadOnlyList<DiffHunk> hunks)
    {
        var result = new List<DiffHunk>(hunks.Count);
        foreach (var hunk in hunks)
        {
            var lines = new List<DiffLine>(hunk.Lines.Count);
            var i = 0;
            while (i < hunk.Lines.Count)
            {
                if (hunk.Lines[i].Kind != DiffLineKind.Removed)
                {
                    lines.Add(hunk.Lines[i++]);
                    continue;
                }

                var removed = TakeRun(hunk.Lines, ref i, DiffLineKind.Removed);
                var added = TakeRun(hunk.Lines, ref i, DiffLineKind.Added);
                if (removed.Count == added.Count && removed.Zip(added).All(pair => Squash(pair.First.Content) == Squash(pair.Second.Content)))
                {
                    lines.AddRange(added.Select(a => a with { Kind = DiffLineKind.Context }));
                }
                else
                {
                    lines.AddRange(removed);
                    lines.AddRange(added);
                }
            }

            if (lines.Any(l => l.Kind != DiffLineKind.Context))
            {
                result.Add(hunk with { Lines = lines });
            }
        }

        return result;
    }

    /// <summary>Lays hunks out in two columns; a change block pairs its removed and added lines row by row.</summary>
    public static IReadOnlyList<SideBySideRow> SideBySide(IReadOnlyList<DiffHunk> hunks)
    {
        var rows = new List<SideBySideRow>();
        for (var hunkIndex = 0; hunkIndex < hunks.Count; hunkIndex++)
        {
            var hunk = hunks[hunkIndex];
            var oldCount = hunk.Lines.Count(l => l.Kind != DiffLineKind.Added);
            var newCount = hunk.Lines.Count(l => l.Kind != DiffLineKind.Removed);
            rows.Add(new SideBySideRow($"@@ -{hunk.OldStart},{oldCount} +{hunk.NewStart},{newCount} @@", hunkIndex, null, null));

            var oldLine = hunk.OldStart;
            var newLine = hunk.NewStart;
            var i = 0;
            while (i < hunk.Lines.Count)
            {
                var line = hunk.Lines[i];
                if (line.Kind == DiffLineKind.Context)
                {
                    var text = line.Content.TrimEnd('\r');
                    rows.Add(new SideBySideRow(null, hunkIndex,
                        new SideBySideCell(oldLine++, DiffLineKind.Context, text),
                        new SideBySideCell(newLine++, DiffLineKind.Context, text)));
                    i++;
                    continue;
                }

                var removed = TakeRun(hunk.Lines, ref i, DiffLineKind.Removed);
                var added = TakeRun(hunk.Lines, ref i, DiffLineKind.Added);
                for (var row = 0; row < Math.Max(removed.Count, added.Count); row++)
                {
                    var left = row < removed.Count ? new SideBySideCell(oldLine++, DiffLineKind.Removed, removed[row].Content.TrimEnd('\r')) : null;
                    var right = row < added.Count ? new SideBySideCell(newLine++, DiffLineKind.Added, added[row].Content.TrimEnd('\r')) : null;
                    rows.Add(new SideBySideRow(null, hunkIndex, left, right));
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// The part of a modified line that actually changed, for GitKraken-style inline highlighting:
    /// everything between the common prefix and the common suffix of the old and new text.
    /// </summary>
    /// <returns>Start index (same in both texts) and the changed length in each; null when the lines are unrelated (nothing in common).</returns>
    public static (int Start, int OldLength, int NewLength)? ChangedSpan(string oldText, string newText)
    {
        var prefix = 0;
        var max = Math.Min(oldText.Length, newText.Length);
        while (prefix < max && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < max - prefix && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
        {
            suffix++;
        }

        // Highlighting a whole line says nothing more than the red / green background already does.
        if (prefix + suffix == 0)
        {
            return null;
        }

        return (prefix, oldText.Length - prefix - suffix, newText.Length - prefix - suffix);
    }

    private static List<DiffLine> TakeRun(IReadOnlyList<DiffLine> lines, ref int index, DiffLineKind kind)
    {
        var run = new List<DiffLine>();
        while (index < lines.Count && lines[index].Kind == kind)
        {
            run.Add(lines[index++]);
        }

        return run;
    }

    private static string Squash(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
}
