using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using GitUi.Application.DTOs;
using SkiaSharp;

namespace GitUi.Presentation.Controls;

internal sealed class CommitGraphDrawOperation : ICustomDrawOperation
{
    // Ten well-separated hues that read on both light and dark backgrounds.
    private static readonly SKColor[] LanePalette =
    {
        SKColor.Parse("#15A0BF"),
        SKColor.Parse("#0669F7"),
        SKColor.Parse("#8E00C2"),
        SKColor.Parse("#C517B6"),
        SKColor.Parse("#D90171"),
        SKColor.Parse("#CD0101"),
        SKColor.Parse("#F25D2E"),
        SKColor.Parse("#F2CA33"),
        SKColor.Parse("#7BD938"),
        SKColor.Parse("#2ECE9D"),
    };

    private static readonly SKColor SelectedRowColor = SKColor.Parse("#335F8EED");
    private static readonly SKColor HoverRowColor = SKColor.Parse("#195F8EED");
    private static readonly SKColor TagColor = SKColor.Parse("#ECB91C");

    private static readonly SKTypeface RegularTypeface = SKTypeface.Default;
    private static readonly SKTypeface BoldTypeface =
        SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? SKTypeface.Default;
    private static readonly SKTypeface MonospaceTypeface =
        SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default;

    private readonly IReadOnlyList<CommitGraphRow> _rows;
    private readonly IReadOnlyList<CommitGraphColumnBounds> _columns;
    private readonly CommitGraphRow? _selectedRow;
    private readonly int _hoveredDisplayRow;
    private readonly bool _isDarkTheme;
    private readonly int _rowOffset;
    private readonly int _wipChangeCount;
    private readonly bool _isWipSelected;
    private readonly IReadOnlySet<string>? _searchMatches;
    private readonly CommitGraphRow? _compareRow;

    private static readonly SKColor SearchMatchColor = SKColor.Parse("#26ECB91C");

    private bool IsDimmed(int row) => _searchMatches is not null && !_searchMatches.Contains(_rows[row].Node.Commit.Sha);

    public CommitGraphDrawOperation(
        Rect bounds,
        IReadOnlyList<CommitGraphRow> rows,
        IReadOnlyList<CommitGraphColumnBounds> columns,
        CommitGraphRow? selectedRow,
        int hoveredDisplayRow,
        bool isDarkTheme,
        int wipChangeCount,
        bool isWipSelected,
        IReadOnlySet<string>? searchMatches = null,
        CommitGraphRow? compareRow = null)
    {
        _searchMatches = searchMatches;
        _compareRow = compareRow;
        Bounds = bounds;
        _rows = rows;
        _columns = columns;
        _selectedRow = selectedRow;
        _hoveredDisplayRow = hoveredDisplayRow;
        _isDarkTheme = isDarkTheme;
        _wipChangeCount = wipChangeCount;
        _isWipSelected = isWipSelected;
        // The WIP row, when shown, takes display row 0 and pushes every commit down by one.
        _rowOffset = wipChangeCount > 0 ? 1 : 0;
    }

    public Rect Bounds { get; }

    public void Dispose()
    {
    }

    public bool Equals(ICustomDrawOperation? other) => false;

    public bool HitTest(Point p) => Bounds.Contains(p);

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null)
        {
            return;
        }

        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;

        // The canvas sits in a ScrollViewer and can be thousands of rows tall: only draw rows that
        // intersect the visible clip. Edges are still checked for every row because a line from a
        // row above the viewport can cross it.
        var clip = canvas.LocalClipBounds;
        var firstVisible = Math.Max(0, (int)(clip.Top / CommitGraphMetrics.RowHeight) - _rowOffset - 1);
        var lastVisible = Math.Min(_rows.Count - 1, (int)(clip.Bottom / CommitGraphMetrics.RowHeight) - _rowOffset + 1);

        var shaToRow = new Dictionary<string, int>(_rows.Count);
        for (var i = 0; i < _rows.Count; i++)
        {
            shaToRow[_rows[i].Node.Commit.Sha] = i;
        }

        var graph = Column(CommitGraphColumn.Graph);
        var width = (float)Bounds.Width;

        DrawRowBackgrounds(canvas, firstVisible, lastVisible, width);
        DrawGraph(canvas, graph, shaToRow, firstVisible, lastVisible);
        // The BRANCH / TAG column can be turned off in the column menu.
        if (_columns.Any(c => c.Column == CommitGraphColumn.Refs))
        {
            DrawRefs(canvas, Column(CommitGraphColumn.Refs), graph, firstVisible, lastVisible);
        }

        DrawTextColumns(canvas, firstVisible, lastVisible);

        if (_wipChangeCount > 0 && clip.Top < CommitGraphMetrics.RowHeight)
        {
            DrawWipRow(canvas, graph, width);
        }
    }

    private CommitGraphColumnBounds Column(CommitGraphColumn column) => _columns.First(c => c.Column == column);

    private float RowTop(int row) => (row + _rowOffset) * CommitGraphMetrics.RowHeight;

    private float RowCenter(int row) => RowTop(row) + CommitGraphMetrics.RowHeight / 2f;

    private static float LaneX(CommitGraphColumnBounds graph, int lane) =>
        graph.Left + CommitGraphMetrics.GraphPaddingLeft + lane * CommitGraphMetrics.LaneWidth;

    private static SKColor LaneColor(int lane) => LanePalette[lane % LanePalette.Length];

    private void DrawRowBackgrounds(SKCanvas canvas, int firstVisible, int lastVisible, float width)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Fill };
        for (var i = firstVisible; i <= lastVisible; i++)
        {
            var isSelected = (_selectedRow is not null && ReferenceEquals(_selectedRow.Node, _rows[i].Node))
                || (_compareRow is not null && ReferenceEquals(_compareRow.Node, _rows[i].Node));
            var isMatch = _searchMatches is not null && !IsDimmed(i);
            if (!isSelected && !isMatch && i + _rowOffset != _hoveredDisplayRow)
            {
                continue;
            }

            paint.Color = isSelected ? SelectedRowColor : isMatch ? SearchMatchColor : HoverRowColor;
            canvas.DrawRect(new SKRect(0, RowTop(i), width, RowTop(i) + CommitGraphMetrics.RowHeight), paint);
        }
    }

    private void DrawGraph(SKCanvas canvas, CommitGraphColumnBounds graph, Dictionary<string, int> shaToRow, int firstVisible, int lastVisible)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(graph.Left, canvas.LocalClipBounds.Top, graph.Right, canvas.LocalClipBounds.Bottom));

        using var linePaint = new SKPaint { IsAntialias = true, StrokeWidth = 2f, Style = SKPaintStyle.Stroke };
        for (var i = 0; i <= lastVisible; i++)
        {
            var node = _rows[i].Node;
            foreach (var parentSha in node.Commit.ParentShas)
            {
                if (!shaToRow.TryGetValue(parentSha, out var parentRow) || parentRow < firstVisible)
                {
                    continue;
                }

                // The lane the parent is actually drawn in — not node.ParentLanes[i], which is only
                // the lane the layout builder reserved and may not be where the parent ends up.
                var parentLane = _rows[parentRow].Node.Lane;
                var x = LaneX(graph, node.Lane);
                var parentX = LaneX(graph, parentLane);
                var y = RowCenter(i);
                var parentY = RowCenter(parentRow);

                linePaint.Color = LaneColor(node.Lane == parentLane ? node.Lane : Math.Max(node.Lane, parentLane));
                if (x == parentX)
                {
                    canvas.DrawLine(x, y, parentX, parentY, linePaint);
                    continue;
                }

                // Branch/merge edges bend within one row next to the commit that changes lane, then
                // run straight, instead of cutting diagonally across every row in between.
                using var builder = new SKPathBuilder();
                builder.MoveTo(x, y);
                if (parentLane > node.Lane)
                {
                    // Parent sits further right: leave the child, curve over, then drop straight down.
                    var bendEnd = y + CommitGraphMetrics.RowHeight;
                    builder.CubicTo(x, y + CommitGraphMetrics.RowHeight / 2f, parentX, y + CommitGraphMetrics.RowHeight / 2f, parentX, bendEnd);
                    builder.LineTo(parentX, parentY);
                }
                else
                {
                    // Parent sits further left: drop straight down, then curve into the parent.
                    var bendStart = parentY - CommitGraphMetrics.RowHeight;
                    builder.LineTo(x, bendStart);
                    builder.CubicTo(x, parentY - CommitGraphMetrics.RowHeight / 2f, parentX, parentY - CommitGraphMetrics.RowHeight / 2f, parentX, parentY);
                }

                using var path = builder.Detach();
                canvas.DrawPath(path, linePaint);
            }
        }

        var background = _isDarkTheme ? SKColor.Parse("#1C1E23") : SKColor.Parse("#FCFCFC");
        using var nodePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ringPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var avatarFont = new SKFont(BoldTypeface, 9f);
        using var avatarTextPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        for (var i = firstVisible; i <= lastVisible; i++)
        {
            var node = _rows[i].Node;
            var x = LaneX(graph, node.Lane);
            var color = LaneColor(node.Lane);

            if (node.Commit.IsStash)
            {
                // Stashes are hollow squares, like GitKraken's stash box, so they never read as commits.
                var half = CommitGraphMetrics.NodeRadius - 1f;
                var box = new SKRect(x - half, RowCenter(i) - half, x + half, RowCenter(i) + half);
                nodePaint.Color = background;
                ringPaint.Color = color;
                canvas.DrawRoundRect(box, 2f, 2f, nodePaint);
                canvas.DrawRoundRect(box, 2f, 2f, ringPaint);
            }
            else if (node.Commit.ParentShas.Count > 1)
            {
                // Merge commits are drawn hollow and smaller, so regular commits stand out.
                nodePaint.Color = background;
                ringPaint.Color = color;
                canvas.DrawCircle(x, RowCenter(i), CommitGraphMetrics.NodeRadius - 1f, nodePaint);
                canvas.DrawCircle(x, RowCenter(i), CommitGraphMetrics.NodeRadius - 1f, ringPaint);
            }
            else
            {
                // GitKraken draws the author's avatar on each commit; offline, the initials on a colour of
                // their own (so authors are told apart), ringed with the lane colour.
                nodePaint.Color = AuthorColor(node.Commit.Author);
                canvas.DrawCircle(x, RowCenter(i), CommitGraphMetrics.AvatarRadius, nodePaint);
                ringPaint.Color = color;
                canvas.DrawCircle(x, RowCenter(i), CommitGraphMetrics.AvatarRadius, ringPaint);
                var initials = Initials(node.Commit.Author);
                var textWidth = avatarFont.MeasureText(initials);
                canvas.DrawText(initials, x - textWidth / 2f, RowCenter(i) + 3.5f, SKTextAlign.Left, avatarFont, avatarTextPaint);
            }
        }

        canvas.Restore();
    }

    // Muted tones that keep white initials readable on both themes.
    private static readonly SKColor[] AuthorPalette =
    {
        SKColor.Parse("#3B6E8F"), SKColor.Parse("#7A4E8C"), SKColor.Parse("#8C5A3B"), SKColor.Parse("#3B8C6A"),
        SKColor.Parse("#8C3B4E"), SKColor.Parse("#5A6B2E"), SKColor.Parse("#2E5A8C"), SKColor.Parse("#8C7A2E"),
    };

    /// <summary>A stable colour per author (string hash, not randomized per run).</summary>
    private static SKColor AuthorColor(string author)
    {
        var hash = 0;
        foreach (var c in author)
        {
            hash = unchecked(hash * 31 + c);
        }

        return AuthorPalette[(hash & int.MaxValue) % AuthorPalette.Length];
    }

    private static readonly Dictionary<string, string> InitialsCache = new();

    /// <summary>"Jane Doe" → "JD", "jdoe" → "J", "CONTOSO\JaneDOE" → "JD" (domain prefixes dropped).</summary>
    private static string Initials(string author)
    {
        lock (InitialsCache)
        {
            if (InitialsCache.TryGetValue(author, out var cached))
            {
                return cached;
            }

            var name = author.Contains('\\') ? author[(author.LastIndexOf('\\') + 1)..] : author;
            var words = name.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            string initials;
            if (words.Length >= 2)
            {
                initials = $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[^1][0])}";
            }
            else if (words.Length == 1)
            {
                // A single CamelCase word ("JaneDOE") still gives two letters.
                var capitals = words[0].Where(char.IsUpper).ToList();
                initials = capitals.Count >= 2 && char.IsUpper(words[0][0])
                    ? $"{capitals[0]}{capitals[1]}"
                    : char.ToUpperInvariant(words[0][0]).ToString();
            }
            else
            {
                initials = "?";
            }

            InitialsCache[author] = initials;
            return initials;
        }
    }

    /// <summary>
    /// "// WIP" row for uncommitted changes, drawn above HEAD with a dashed node and a dashed
    /// link down to the checked-out commit.
    /// </summary>
    private void DrawWipRow(SKCanvas canvas, CommitGraphColumnBounds graph, float width)
    {
        const float top = 0f;
        var centerY = CommitGraphMetrics.RowHeight / 2f;

        if (_isWipSelected || _hoveredDisplayRow == 0)
        {
            using var backgroundPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = _isWipSelected ? SelectedRowColor : HoverRowColor };
            canvas.DrawRect(new SKRect(0, top, width, top + CommitGraphMetrics.RowHeight), backgroundPaint);
        }

        var headIndex = -1;
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Labels.Any(l => l.IsHead))
            {
                headIndex = i;
                break;
            }
        }

        var lane = headIndex >= 0 ? _rows[headIndex].Node.Lane : 0;
        var x = LaneX(graph, lane);
        var color = _isDarkTheme ? SKColor.Parse("#99FFFFFF") : SKColor.Parse("#99000000");

        canvas.Save();
        canvas.ClipRect(new SKRect(graph.Left, 0, graph.Right, Math.Max(CommitGraphMetrics.RowHeight, headIndex >= 0 ? RowCenter(headIndex) : 0)));
        using var dashEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0);
        using var dashPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = color, PathEffect = dashEffect };
        if (headIndex >= 0)
        {
            canvas.DrawLine(x, centerY + CommitGraphMetrics.AvatarRadius, x, RowCenter(headIndex) - CommitGraphMetrics.AvatarRadius, dashPaint);
        }

        canvas.DrawCircle(x, centerY, CommitGraphMetrics.AvatarRadius, dashPaint);
        canvas.Restore();

        var message = Column(CommitGraphColumn.Message);
        using var italic = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic) ?? RegularTypeface, 12);
        using var textPaint = new SKPaint { IsAntialias = true, Color = _isDarkTheme ? SKColor.Parse("#E6FFFFFF") : SKColor.Parse("#E6000000") };
        var label = _wipChangeCount == 1 ? "// WIP  ·  1 file change" : $"// WIP  ·  {_wipChangeCount} file changes";
        canvas.DrawText(Ellipsize(label, italic, message.Width - CommitGraphMetrics.CellPadding * 2), message.Left + CommitGraphMetrics.CellPadding, centerY + 4f, SKTextAlign.Left, italic, textPaint);
    }

    private void DrawRefs(SKCanvas canvas, CommitGraphColumnBounds refs, CommitGraphColumnBounds graph, int firstVisible, int lastVisible)
    {
        using var font = new SKFont(RegularTypeface, 11);
        using var headFont = new SKFont(BoldTypeface, 11);
        using var textPaint = new SKPaint { IsAntialias = true, Color = _isDarkTheme ? SKColors.White : SKColors.Black };
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var borderPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var linkPaint = new SKPaint { IsAntialias = true, StrokeWidth = 1f };

        for (var i = firstVisible; i <= lastVisible; i++)
        {
            var row = _rows[i];
            if (row.Labels.Count == 0)
            {
                continue;
            }

            var laneColor = LaneColor(row.Node.Lane);
            var centerY = RowCenter(i);
            var layout = LayoutRefs(row.Labels, refs.Left, refs.Right, centerY, font, headFont);
            var first = layout.First;
            var left = refs.Left + 4f;

            if (layout.Badge is { } rect)
            {
                var labelFont = first.IsHead ? headFont : font;
                var color = first.Kind == RefLabelKind.Tag ? TagColor : laneColor;
                fillPaint.Color = color.WithAlpha(first.Kind == RefLabelKind.RemoteBranch ? (byte)0x30 : (byte)0x55);
                borderPaint.Color = color;
                canvas.DrawRoundRect(rect, 3, 3, fillPaint);
                canvas.DrawRoundRect(rect, 3, 3, borderPaint);
                canvas.DrawText(layout.BadgeText, rect.Left + 4f, centerY + 4f, SKTextAlign.Left, labelFont, textPaint);
                left = rect.Right + 4f;
            }

            if (layout.Overflow is { } overflow)
            {
                // The "+N" counter reads as a button: clicking it lists every ref of the commit.
                fillPaint.Color = laneColor.WithAlpha(0x26);
                canvas.DrawRoundRect(overflow, 3, 3, fillPaint);
                canvas.DrawText(layout.OverflowText!, overflow.Left + 4f, centerY + 4f, SKTextAlign.Left, font, textPaint);
                left = overflow.Right + 2f;
            }

            // Connector from the badge to the commit node, so a label is readable across the gap.
            linkPaint.Color = laneColor.WithAlpha(0x99);
            canvas.DrawLine(left, centerY, Math.Min(LaneX(graph, row.Node.Lane), graph.Right), centerY, linkPaint);
        }
    }

    private void DrawTextColumns(SKCanvas canvas, int firstVisible, int lastVisible)
    {
        using var messageFont = new SKFont(RegularTypeface, 12);
        using var metaFont = new SKFont(RegularTypeface, 11);
        using var shaFont = new SKFont(MonospaceTypeface, 11);
        using var messagePaint = new SKPaint { IsAntialias = true, Color = _isDarkTheme ? SKColor.Parse("#E6FFFFFF") : SKColor.Parse("#E6000000") };
        using var metaPaint = new SKPaint { IsAntialias = true, Color = _isDarkTheme ? SKColor.Parse("#99FFFFFF") : SKColor.Parse("#99000000") };
        // Rows that do not match the graph search fade out so the matches stand out.
        using var dimPaint = new SKPaint { IsAntialias = true, Color = _isDarkTheme ? SKColor.Parse("#40FFFFFF") : SKColor.Parse("#40000000") };

        for (var i = firstVisible; i <= lastVisible; i++)
        {
            var commit = _rows[i].Node.Commit;
            var baseline = RowCenter(i) + 4f;
            var dimmed = IsDimmed(i);

            foreach (var column in _columns)
            {
                var (text, font, paint) = column.Column switch
                {
                    CommitGraphColumn.Message => (FirstLine(commit.Message), messageFont, dimmed ? dimPaint : messagePaint),
                    CommitGraphColumn.Author => (commit.Author, metaFont, dimmed ? dimPaint : metaPaint),
                    CommitGraphColumn.Date => (commit.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), metaFont, dimmed ? dimPaint : metaPaint),
                    CommitGraphColumn.Sha => (commit.Sha[..Math.Min(7, commit.Sha.Length)], shaFont, dimmed ? dimPaint : metaPaint),
                    _ => (null, null, null),
                };

                if (text is null || font is null || paint is null)
                {
                    continue;
                }

                var available = column.Width - CommitGraphMetrics.CellPadding * 2;
                if (available <= 8f)
                {
                    continue;
                }

                canvas.DrawText(Ellipsize(text, font, available), column.Left + CommitGraphMetrics.CellPadding, baseline, SKTextAlign.Left, font, paint);
            }
        }
    }

    internal sealed record RefsLayout(RefLabel First, SKRect? Badge, string BadgeText, SKRect? Overflow, string? OverflowText);

    /// <summary>
    /// Where a row's refs go in the BRANCH / TAG column: one badge (the checked-out branch first)
    /// plus a "+N" counter for the others. Shared by drawing and hit testing.
    /// </summary>
    internal static RefsLayout LayoutRefs(IReadOnlyList<RefLabel> labels, float columnLeft, float columnRight, float centerY, SKFont font, SKFont headFont)
    {
        const float badgeHeight = 16f;
        var left = columnLeft + 4f;
        var maxRight = columnRight - 4f;

        var first = labels.FirstOrDefault(l => l.IsHead) ?? labels[0];
        var extra = labels.Count - 1;
        var extraText = extra > 0 ? $"+{extra}" : null;
        var extraWidth = extraText is null ? 0f : font.MeasureText(extraText) + 8f;

        SKRect? badge = null;
        var text = string.Empty;
        var labelFont = first.IsHead ? headFont : font;
        var available = maxRight - left - extraWidth - 8f;
        if (available > 12f)
        {
            text = Ellipsize(first.Name, labelFont, available);
            var badgeWidth = labelFont.MeasureText(text) + 8f;
            badge = new SKRect(left, centerY - badgeHeight / 2f, left + badgeWidth, centerY + badgeHeight / 2f);
            left = badge.Value.Right + 4f;
        }

        SKRect? overflow = extraText is null
            ? null
            : new SKRect(left, centerY - badgeHeight / 2f, left + extraWidth, centerY + badgeHeight / 2f);
        return new RefsLayout(first, badge, text, overflow, extraText);
    }

    /// <summary>The "+N" counter of <paramref name="labels"/>, or null when every ref fits in one badge.</summary>
    internal static SKRect? RefsOverflowBounds(IReadOnlyList<RefLabel> labels, float columnLeft, float columnRight, float centerY)
    {
        if (labels.Count < 2)
        {
            return null;
        }

        using var font = new SKFont(RegularTypeface, 11);
        using var headFont = new SKFont(BoldTypeface, 11);
        return LayoutRefs(labels, columnLeft, columnRight, centerY, font, headFont).Overflow;
    }

    private static string FirstLine(string message)
    {
        var newline = message.IndexOfAny(new[] { '\r', '\n' });
        return newline < 0 ? message : message[..newline];
    }

    private static string Ellipsize(string text, SKFont font, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (font.MeasureText(text[..mid] + ellipsis) <= maxWidth)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low == 0 ? string.Empty : text[..low] + ellipsis;
    }
}
