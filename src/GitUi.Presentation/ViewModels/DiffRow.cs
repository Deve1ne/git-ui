using GitUi.Application.Services;
using GitUi.Domain.ValueObjects;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// One displayed row of a file diff: either a hunk header (with its stage/unstage action) or a
/// diff line with its old/new line numbers. <see cref="LineIndex"/> indexes the hunk's lines and
/// is what partial staging selects on.
/// </summary>
public sealed record DiffRow(
    bool IsHunkHeader,
    int HunkIndex,
    int LineIndex,
    DiffLineKind Kind,
    string Text,
    string OldLineNumber,
    string NewLineNumber)
{
    public bool IsChange => !IsHunkHeader && Kind != DiffLineKind.Context;

    /// <summary>For a modified line: the text before the changed part, the changed part (highlighted) and the rest.</summary>
    public string Prefix { get; init; } = Text;

    public string Changed { get; init; } = string.Empty;

    public string Suffix { get; init; } = string.Empty;

    public static IReadOnlyList<DiffRow> FromHunks(IReadOnlyList<DiffHunk> hunks)
    {
        var rows = new List<DiffRow>();
        for (var hunkIndex = 0; hunkIndex < hunks.Count; hunkIndex++)
        {
            var hunk = hunks[hunkIndex];
            var oldCount = hunk.Lines.Count(l => l.Kind != DiffLineKind.Added);
            var newCount = hunk.Lines.Count(l => l.Kind != DiffLineKind.Removed);
            rows.Add(new DiffRow(true, hunkIndex, -1, DiffLineKind.Context,
                $"@@ -{hunk.OldStart},{oldCount} +{hunk.NewStart},{newCount} @@", string.Empty, string.Empty));

            var first = rows.Count;
            var oldLine = hunk.OldStart;
            var newLine = hunk.NewStart;
            for (var lineIndex = 0; lineIndex < hunk.Lines.Count; lineIndex++)
            {
                var line = hunk.Lines[lineIndex];
                var oldNumber = line.Kind == DiffLineKind.Added ? string.Empty : (oldLine++).ToString();
                var newNumber = line.Kind == DiffLineKind.Removed ? string.Empty : (newLine++).ToString();
                rows.Add(new DiffRow(false, hunkIndex, lineIndex, line.Kind, line.Content.TrimEnd('\r'), oldNumber, newNumber));
            }

            HighlightInlineChanges(rows, first);
        }

        return rows;
    }

    /// <summary>
    /// A block of removed lines followed by as many added lines is a modification: pair them and
    /// mark the part of each line that changed, as GitKraken highlights it.
    /// </summary>
    private static void HighlightInlineChanges(List<DiffRow> rows, int start)
    {
        var i = start;
        while (i < rows.Count)
        {
            if (rows[i].Kind != DiffLineKind.Removed)
            {
                i++;
                continue;
            }

            var removedStart = i;
            while (i < rows.Count && rows[i].Kind == DiffLineKind.Removed)
            {
                i++;
            }

            var addedStart = i;
            while (i < rows.Count && rows[i].Kind == DiffLineKind.Added)
            {
                i++;
            }

            var removedCount = addedStart - removedStart;
            if (removedCount != i - addedStart)
            {
                continue;
            }

            for (var k = 0; k < removedCount; k++)
            {
                var before = rows[removedStart + k];
                var after = rows[addedStart + k];
                if (DiffLayout.ChangedSpan(before.Text, after.Text) is not { } span || (span.OldLength == 0 && span.NewLength == 0))
                {
                    continue;
                }

                rows[removedStart + k] = Split(before, span.Start, span.OldLength);
                rows[addedStart + k] = Split(after, span.Start, span.NewLength);
            }
        }
    }

    private static DiffRow Split(DiffRow row, int start, int length) => row with
    {
        Prefix = row.Text[..start],
        Changed = row.Text.Substring(start, length),
        Suffix = row.Text[(start + length)..],
    };
}
