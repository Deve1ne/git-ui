namespace GitUi.Application.Services;

/// <summary>
/// Line-based three-way merge, the diff3 algorithm GitKraken runs again on a conflicted file:
/// diff each side against the common ancestor, merge the changes that do not overlap, and keep a
/// conflict only where both sides changed the same lines differently. Git's own merge sometimes
/// reports a conflict that this recomputation resolves (other diff alignment, identical changes).
/// </summary>
public static class Diff3Merge
{
    /// <summary>Above this many edits per side, give up (no auto-resolution) rather than burn CPU.</summary>
    private const int MaxEdits = 2000;

    /// <summary>The merged lines, or null when a true conflict remains (or the files are too different to try).</summary>
    public static IReadOnlyList<string>? TryMerge(IReadOnlyList<string> baseLines, IReadOnlyList<string> ours, IReadOnlyList<string> theirs)
    {
        var oursHunks = Hunks(baseLines, ours);
        var theirsHunks = Hunks(baseLines, theirs);
        if (oursHunks is null || theirsHunks is null)
        {
            return null;
        }

        // Every change, by where it starts in the ancestor; side 0 = ours, 2 = theirs.
        var changes = oursHunks.Select(h => (h, Side: 0)).Concat(theirsHunks.Select(h => (h, Side: 2)))
            .OrderBy(c => c.h.BaseStart)
            .ThenBy(c => c.Side)
            .ToList();

        var result = new List<string>();
        var copied = 0;
        var i = 0;
        while (i < changes.Count)
        {
            // Group changes whose ancestor ranges overlap or touch.
            var first = i;
            var regionStart = changes[i].h.BaseStart;
            var regionEnd = regionStart + changes[i].h.BaseLength;
            while (i + 1 < changes.Count && changes[i + 1].h.BaseStart <= regionEnd)
            {
                i++;
                regionEnd = Math.Max(regionEnd, changes[i].h.BaseStart + changes[i].h.BaseLength);
            }

            for (var k = copied; k < regionStart; k++)
            {
                result.Add(baseLines[k]);
            }

            var group = changes.Skip(first).Take(i - first + 1).ToList();
            if (group.All(c => c.Side == group[0].Side))
            {
                // Only one side touched this region: take its version.
                var source = group[0].Side == 0 ? ours : theirs;
                var (start, end) = SideRange(group, regionStart, regionEnd);
                for (var k = start; k < end; k++)
                {
                    result.Add(source[k]);
                }
            }
            else
            {
                // Both sides changed it: fine only if they made the very same change.
                var (oursStart, oursEnd) = SideRange(group.Where(c => c.Side == 0).ToList(), regionStart, regionEnd);
                var (theirsStart, theirsEnd) = SideRange(group.Where(c => c.Side == 2).ToList(), regionStart, regionEnd);
                if (oursEnd - oursStart != theirsEnd - theirsStart)
                {
                    return null;
                }

                for (var k = 0; k < oursEnd - oursStart; k++)
                {
                    if (!string.Equals(ours[oursStart + k], theirs[theirsStart + k], StringComparison.Ordinal))
                    {
                        return null;
                    }
                }

                for (var k = oursStart; k < oursEnd; k++)
                {
                    result.Add(ours[k]);
                }
            }

            copied = regionEnd;
            i++;
        }

        for (var k = copied; k < baseLines.Count; k++)
        {
            result.Add(baseLines[k]);
        }

        return result;
    }

    /// <summary>
    /// The lines of one side covering the ancestor region [regionStart, regionEnd): the hunks give
    /// the mapped edges, untouched ancestor lines at either end map one to one.
    /// </summary>
    private static (int Start, int End) SideRange(IReadOnlyList<(Hunk h, int Side)> hunks, int regionStart, int regionEnd)
    {
        var firstHunk = hunks[0].h;
        var lastHunk = hunks[^1].h;
        var start = firstHunk.OtherStart - (firstHunk.BaseStart - regionStart);
        var end = lastHunk.OtherStart + lastHunk.OtherLength + (regionEnd - (lastHunk.BaseStart + lastHunk.BaseLength));
        return (start, end);
    }

    public readonly record struct Hunk(int BaseStart, int BaseLength, int OtherStart, int OtherLength);

    /// <summary>Differences between <paramref name="a"/> (ancestor) and <paramref name="b"/>, from a Myers diff; null when too different.</summary>
    public static IReadOnlyList<Hunk>? Hunks(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        // Trim the common prefix and suffix: most conflicts touch a small part of a large file.
        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix])
        {
            suffix++;
        }

        var n = a.Count - prefix - suffix;
        var m = b.Count - prefix - suffix;
        if (n == 0 && m == 0)
        {
            return Array.Empty<Hunk>();
        }

        var matches = MyersMatches(a, prefix, n, b, prefix, m);
        if (matches is null)
        {
            return null;
        }

        // Gaps between matched lines are the hunks.
        var hunks = new List<Hunk>();
        int ai = 0, bi = 0;
        foreach (var (ma, mb) in matches.Append((n, m)))
        {
            if (ma > ai || mb > bi)
            {
                hunks.Add(new Hunk(prefix + ai, ma - ai, prefix + bi, mb - bi));
            }

            ai = ma + 1;
            bi = mb + 1;
        }

        return hunks;
    }

    /// <summary>Matched (a, b) index pairs (relative to the offsets), in order; null past <see cref="MaxEdits"/>.</summary>
    private static List<(int A, int B)>? MyersMatches(IReadOnlyList<string> a, int aOff, int n, IReadOnlyList<string> b, int bOff, int m)
    {
        var max = n + m;
        var offset = max;
        var v = new int[2 * max + 2];
        var trace = new List<int[]>();
        for (var d = 0; d <= max; d++)
        {
            if (d > MaxEdits)
            {
                return null;
            }

            // Only k in [-d, d] is read when backtracking round d: keep that slice, not the whole array.
            var slice = new int[2 * d + 1];
            Array.Copy(v, offset - d, slice, 0, slice.Length);
            trace.Add(slice);
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[aOff + x] == b[bOff + y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    return Backtrack(trace, n, m, d);
                }
            }
        }

        return new List<(int, int)>();
    }

    private static List<(int A, int B)> Backtrack(List<int[]> trace, int n, int m, int dEnd)
    {
        var matches = new List<(int, int)>();
        int x = n, y = m;
        for (var d = dEnd; d > 0; d--)
        {
            var v = trace[d];
            int V(int k) => v[k + d];
            var k = x - y;
            var prevK = k == -d || (k != d && V(k - 1) < V(k + 1)) ? k + 1 : k - 1;
            var prevX = V(prevK);
            var prevY = prevX - prevK;
            while (x > prevX && y > prevY)
            {
                x--;
                y--;
                matches.Add((x, y));
            }

            x = prevX;
            y = prevY;
        }

        while (x > 0 && y > 0)
        {
            x--;
            y--;
            matches.Add((x, y));
        }

        matches.Reverse();
        return matches;
    }
}
