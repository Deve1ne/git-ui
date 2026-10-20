namespace GitUi.Application.Services;

/// <summary>
/// Command palette filtering: every word typed must appear in the label (any order, any case).
/// Matches at the start of the label or of a word rank first, then shorter labels.
/// </summary>
public static class FuzzyMatcher
{
    /// <returns>Null when <paramref name="label"/> does not match; otherwise a score, higher is better.</returns>
    public static int? Score(string label, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return 0;
        }

        var score = 0;
        foreach (var word in words)
        {
            var index = label.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            score += index == 0 ? 30 : char.IsLetterOrDigit(label[index - 1]) ? 5 : 15;
        }

        return score * 100 - label.Length;
    }

    /// <summary>The items matching <paramref name="query"/>, best first (stable for equal scores).</summary>
    public static IReadOnlyList<T> Filter<T>(IEnumerable<T> items, Func<T, string> label, string query) =>
        items.Select((item, order) => (item, order, score: Score(label(item), query)))
            .Where(x => x.score is not null)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.order)
            .Select(x => x.item)
            .ToList();
}
