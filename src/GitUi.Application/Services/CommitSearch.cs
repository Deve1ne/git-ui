using GitUi.Application.DTOs;

namespace GitUi.Application.Services;

/// <summary>
/// Graph search (Ctrl+F): a row matches when its message, author or a branch / tag name contains
/// the query, or its SHA starts with it — case-insensitive, like GitKraken's commit search.
/// </summary>
public static class CommitSearch
{
    public static bool Matches(CommitGraphRow row, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var text = query.Trim();
        var commit = row.Node.Commit;
        return commit.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
            || commit.Author.Contains(text, StringComparison.OrdinalIgnoreCase)
            || commit.Sha.StartsWith(text, StringComparison.OrdinalIgnoreCase)
            || row.Labels.Any(label => label.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Indexes of the matching rows, top to bottom.</summary>
    public static IReadOnlyList<int> FindAll(IReadOnlyList<CommitGraphRow> rows, string query)
    {
        var matches = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (Matches(rows[i], query))
            {
                matches.Add(i);
            }
        }

        return matches;
    }
}
