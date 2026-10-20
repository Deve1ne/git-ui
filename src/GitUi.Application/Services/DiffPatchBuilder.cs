using System.Text;
using GitUi.Domain.ValueObjects;

namespace GitUi.Application.Services;

/// <summary>
/// Builds a unified diff for one hunk — or a subset of its lines — so it can be fed to
/// <c>git apply --cached</c> for hunk/line staging.
/// </summary>
/// <remarks>
/// Lines that are not selected must still describe the side of the file git applies against:
/// <list type="bullet">
/// <item>Staging (patch applied forward on the index): the index holds the old side, so an
/// unselected removal stays as context and an unselected addition is dropped.</item>
/// <item>Unstaging (patch applied in reverse on the index): the index holds the new side, so an
/// unselected addition stays as context and an unselected removal is dropped.</item>
/// </list>
/// </remarks>
public static class DiffPatchBuilder
{
    /// <param name="filePath">Repository-relative path, forward slashes.</param>
    /// <param name="hunk">A hunk from the unstaged diff (staging) or the staged diff (unstaging).</param>
    /// <param name="selectedLineIndexes">Indexes into <see cref="DiffHunk.Lines"/>; null selects the whole hunk.</param>
    /// <param name="forUnstage">True when the patch will be applied with <c>--reverse</c>.</param>
    /// <returns>The patch text, or null when the selection contains no added or removed line.</returns>
    public static string? BuildPatch(string filePath, DiffHunk hunk, IReadOnlySet<int>? selectedLineIndexes, bool forUnstage)
    {
        var body = new StringBuilder();
        var oldCount = 0;
        var newCount = 0;
        var hasChange = false;

        for (var i = 0; i < hunk.Lines.Count; i++)
        {
            var line = hunk.Lines[i];
            var selected = selectedLineIndexes is null || selectedLineIndexes.Contains(i);

            switch (line.Kind)
            {
                case DiffLineKind.Context:
                    AppendLine(body, ' ', line);
                    oldCount++;
                    newCount++;
                    break;

                case DiffLineKind.Added when selected:
                    AppendLine(body, '+', line);
                    newCount++;
                    hasChange = true;
                    break;

                case DiffLineKind.Added when forUnstage:
                    // Already in the index and staying there: it is context for the reverse apply.
                    AppendLine(body, ' ', line);
                    oldCount++;
                    newCount++;
                    break;

                case DiffLineKind.Removed when selected:
                    AppendLine(body, '-', line);
                    oldCount++;
                    hasChange = true;
                    break;

                case DiffLineKind.Removed when !forUnstage:
                    // Still in the index and staying there: it is context for the forward apply.
                    AppendLine(body, ' ', line);
                    oldCount++;
                    newCount++;
                    break;

                default:
                    // Unselected addition while staging, or unselected removal while unstaging:
                    // not on the side of the file git applies against, so it is left out.
                    break;
            }
        }

        if (!hasChange)
        {
            return null;
        }

        var patch = new StringBuilder();
        patch.Append("diff --git a/").Append(filePath).Append(" b/").Append(filePath).Append('\n');
        patch.Append("--- a/").Append(filePath).Append('\n');
        patch.Append("+++ b/").Append(filePath).Append('\n');
        patch.Append("@@ -").Append(Range(hunk.OldStart, oldCount))
             .Append(" +").Append(Range(hunk.NewStart, newCount)).Append(" @@\n");
        patch.Append(body);
        return patch.ToString();
    }

    private static void AppendLine(StringBuilder builder, char prefix, DiffLine line)
    {
        builder.Append(prefix).Append(line.Content).Append('\n');
        if (line.NoNewlineAtEnd)
        {
            builder.Append("\\ No newline at end of file\n");
        }
    }

    private static string Range(int start, int count) => count == 1 ? $"{start}" : $"{start},{count}";
}
