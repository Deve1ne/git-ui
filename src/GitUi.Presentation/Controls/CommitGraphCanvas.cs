using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using GitUi.Application.DTOs;

namespace GitUi.Presentation.Controls;

public sealed class CommitGraphCanvas : Control
{
    public static readonly StyledProperty<IReadOnlyList<CommitGraphRow>?> RowsProperty =
        AvaloniaProperty.Register<CommitGraphCanvas, IReadOnlyList<CommitGraphRow>?>(nameof(Rows));

    public static readonly StyledProperty<CommitGraphRow?> SelectedRowProperty =
        AvaloniaProperty.Register<CommitGraphCanvas, CommitGraphRow?>(nameof(SelectedRow), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<CommitGraphColumns?> ColumnsProperty =
        AvaloniaProperty.Register<CommitGraphCanvas, CommitGraphColumns?>(nameof(Columns));

    public static readonly StyledProperty<int> WipChangeCountProperty =
        AvaloniaProperty.Register<CommitGraphCanvas, int>(nameof(WipChangeCount));

    public static readonly StyledProperty<IReadOnlySet<string>?> SearchMatchesProperty =
        AvaloniaProperty.Register<CommitGraphCanvas, IReadOnlySet<string>?>(nameof(SearchMatches));

    /// <summary>SHAs matching the graph search: they are highlighted and every other row is dimmed. Null when not searching.</summary>
    public IReadOnlySet<string>? SearchMatches
    {
        get => GetValue(SearchMatchesProperty);
        set => SetValue(SearchMatchesProperty, value);
    }

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private int _hoveredDisplayRow = -1;
    private bool _isWipSelected;

    /// <summary>Uncommitted file changes; when non-zero a "// WIP" row is shown above HEAD.</summary>
    public int WipChangeCount
    {
        get => GetValue(WipChangeCountProperty);
        set => SetValue(WipChangeCountProperty, value);
    }

    public event EventHandler? WipRowClicked;

    /// <summary>Ctrl+click on another commit while one is selected: (selected, clicked).</summary>
    public event EventHandler<(CommitGraphRow Selected, CommitGraphRow Other)>? CompareRequested;

    // The Ctrl+clicked commit, highlighted like the selection while the comparison shows.
    private CommitGraphRow? _compareRow;

    public CommitGraphColumns? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public IReadOnlyList<CommitGraphRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public CommitGraphRow? SelectedRow
    {
        get => GetValue(SelectedRowProperty);
        set => SetValue(SelectedRowProperty, value);
    }

    static CommitGraphCanvas()
    {
        AffectsRender<CommitGraphCanvas>(RowsProperty, SelectedRowProperty, ColumnsProperty, WipChangeCountProperty, SearchMatchesProperty, ThemeVariantScope.ActualThemeVariantProperty);
        // Row count drives the desired height, so the ScrollViewer needs a re-measure too.
        AffectsMeasure<CommitGraphCanvas>(RowsProperty, WipChangeCountProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Selection from outside (search, parent link, keyboard) must scroll the row into view.
        if (change.Property == SelectedRowProperty)
        {
            _compareRow = null;
        }

        if (change.Property == RowsProperty || change.Property == WipChangeCountProperty)
        {
            _expandedRefsRow = -1;
        }

        if (change.Property == SelectedRowProperty && change.NewValue is CommitGraphRow row && Rows is { } rows)
        {
            var index = -1;
            for (var i = 0; i < rows.Count && index < 0; i++)
            {
                index = ReferenceEquals(rows[i], row) ? i : -1;
            }

            if (index >= 0)
            {
                var top = (index + RowOffset) * CommitGraphMetrics.RowHeight;
                this.BringIntoView(new Rect(0, top, Math.Max(1, Bounds.Width), CommitGraphMetrics.RowHeight));
            }
        }

        if (change.Property == ColumnsProperty)
        {
            if (change.OldValue is CommitGraphColumns oldColumns)
            {
                oldColumns.Changed -= InvalidateVisual;
            }

            if (change.NewValue is CommitGraphColumns newColumns)
            {
                newColumns.Changed += InvalidateVisual;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var position = e.GetPosition(this);

        // Hovering "+N" unfolds every ref of the row; it stays open while the pointer is on the panel.
        var expanded = IsOnRefsOverflow(position) ? RowIndexAt(position)
            : IsOverExpandedRefs(position) ? _expandedRefsRow
            : -1;
        Cursor = expanded >= 0 ? HandCursor : null;
        if (expanded != _expandedRefsRow)
        {
            _expandedRefsRow = expanded;
            InvalidateVisual();
        }

        var displayRow = (int)(position.Y / CommitGraphMetrics.RowHeight);
        if (displayRow >= DisplayRowCount)
        {
            displayRow = -1;
        }

        if (displayRow != _hoveredDisplayRow)
        {
            _hoveredDisplayRow = displayRow;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredDisplayRow != -1 || _expandedRefsRow != -1)
        {
            _hoveredDisplayRow = -1;
            _expandedRefsRow = -1;
            InvalidateVisual();
        }
    }

    // Index in Rows of the row whose "+N" is unfolded, or -1.
    private int _expandedRefsRow = -1;

    private int RowIndexAt(Point point)
    {
        var rowIndex = (int)(point.Y / CommitGraphMetrics.RowHeight) - RowOffset;
        return Rows is not null && point.Y >= 0 && rowIndex >= 0 && rowIndex < Rows.Count ? rowIndex : -1;
    }

    /// <summary>True when <paramref name="point"/> is on the unfolded refs of a hovered "+N".</summary>
    public bool IsOverExpandedRefs(Point point)
    {
        if (Rows is not { } rows || _expandedRefsRow < 0 || _expandedRefsRow >= rows.Count || rows[_expandedRefsRow].Labels.Count < 2)
        {
            return false;
        }

        var layout = (Columns ?? new CommitGraphColumns()).Layout((float)Bounds.Width);
        if (!layout.Any(c => c.Column == CommitGraphColumn.Refs))
        {
            return false;
        }

        var refs = layout.First(c => c.Column == CommitGraphColumn.Refs);
        var centerY = (_expandedRefsRow + RowOffset) * CommitGraphMetrics.RowHeight + CommitGraphMetrics.RowHeight / 2f;
        return CommitGraphDrawOperation.ExpandedRefsPanel(rows[_expandedRefsRow].Labels, refs.Left, refs.Right, centerY)
            .Contains((float)point.X, (float)point.Y);
    }

    private int RowOffset => WipChangeCount > 0 ? 1 : 0;

    /// <summary>The commit row at <paramref name="point"/> (canvas coordinates); null on the WIP row or below the last commit.</summary>
    public CommitGraphRow? RowAt(Point point)
    {
        var rowIndex = (int)(point.Y / CommitGraphMetrics.RowHeight) - RowOffset;
        return Rows is not null && point.Y >= 0 && rowIndex >= 0 && rowIndex < Rows.Count ? Rows[rowIndex] : null;
    }

    /// <summary>True when <paramref name="point"/> is over the BRANCH / TAG column, where ref badges are drawn.</summary>
    public bool IsInRefsColumn(Point point)
    {
        var layout = (Columns ?? new CommitGraphColumns()).Layout((float)Bounds.Width);
        return layout.Any(c => c.Column == CommitGraphColumn.Refs && point.X >= c.Left && point.X < c.Right);
    }

    /// <summary>True when <paramref name="point"/> is over the "+N" counter of a row with several refs.</summary>
    public bool IsOnRefsOverflow(Point point)
    {
        if (RowAt(point) is not { Labels.Count: > 1 } row)
        {
            return false;
        }

        var layout = (Columns ?? new CommitGraphColumns()).Layout((float)Bounds.Width);
        if (!layout.Any(c => c.Column == CommitGraphColumn.Refs))
        {
            return false;
        }

        var refs = layout.First(c => c.Column == CommitGraphColumn.Refs);

        var displayRow = (int)(point.Y / CommitGraphMetrics.RowHeight);
        var centerY = displayRow * CommitGraphMetrics.RowHeight + CommitGraphMetrics.RowHeight / 2f;
        return CommitGraphDrawOperation.RefsOverflowBounds(row.Labels, refs.Left, refs.Right, centerY) is { } bounds
            && bounds.Contains((float)point.X, (float)point.Y);
    }

    /// <summary>Click on a row's "+N" counter: the caller lists every ref of that commit.</summary>
    public event EventHandler<CommitGraphRow>? RefsOverflowClicked;

    private Point? _pressedOnOverflow;

    private int DisplayRowCount => (Rows?.Count ?? 0) + RowOffset;

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = DisplayRowCount * CommitGraphMetrics.RowHeight;
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, height);
    }

    public CommitGraphCanvas() => Focusable = true;

    /// <summary>
    /// Keyboard navigation like GitKraken's graph: Up / Down move the selection, Page Up / Down by a
    /// screen, Home / End to the first / last commit. Up from the first commit selects the WIP row.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Rows is not { Count: > 0 } rows || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var page = Math.Max(1, (int)((Parent as Visual)?.Bounds.Height / CommitGraphMetrics.RowHeight ?? 10) - 1);
        var current = -1;
        for (var i = 0; i < rows.Count && SelectedRow is not null; i++)
        {
            if (ReferenceEquals(rows[i], SelectedRow))
            {
                current = i;
                break;
            }
        }

        int? target = e.Key switch
        {
            Key.Down => Math.Min(rows.Count - 1, current + 1),
            Key.Up => current - 1,
            Key.PageDown => Math.Min(rows.Count - 1, Math.Max(0, current) + page),
            Key.PageUp => Math.Max(0, current - page),
            Key.Home => 0,
            Key.End => rows.Count - 1,
            _ => null,
        };
        if (target is not { } index)
        {
            return;
        }

        e.Handled = true;
        if (index < 0)
        {
            if (WipChangeCount > 0)
            {
                SelectedRow = null;
                _isWipSelected = true;
                InvalidateVisual();
                this.BringIntoView(new Rect(0, 0, Math.Max(1, Bounds.Width), CommitGraphMetrics.RowHeight));
                WipRowClicked?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        _isWipSelected = false;
        SelectedRow = rows[index];
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var position = e.GetPosition(this);
        _pressedOnOverflow = null;

        // A press on the unfolded refs belongs to their commit, not to the row drawn underneath.
        if (IsOverExpandedRefs(position) && Rows is { } expandedRows)
        {
            _pressedOnOverflow = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? position : null;
            _compareRow = null;
            _isWipSelected = false;
            SelectedRow = expandedRows[_expandedRefsRow];
            return;
        }

        _pressedOnOverflow = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && IsOnRefsOverflow(position) ? position : null;

        var displayRow = (int)(position.Y / CommitGraphMetrics.RowHeight);
        if (RowOffset == 1 && displayRow == 0)
        {
            SelectedRow = null;
            _isWipSelected = true;
            InvalidateVisual();
            WipRowClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        var rowIndex = displayRow - RowOffset;
        if (Rows is not null && rowIndex >= 0 && rowIndex < Rows.Count)
        {
            // Ctrl+click a second commit: compare it with the selected one (GitKraken), keeping the selection.
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && SelectedRow is { } selected && !ReferenceEquals(selected, Rows[rowIndex]))
            {
                _compareRow = Rows[rowIndex];
                InvalidateVisual();
                CompareRequested?.Invoke(this, (selected, Rows[rowIndex]));
                return;
            }

            _compareRow = null;
            _isWipSelected = false;
            SelectedRow = Rows[rowIndex];
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // A click, not the start of a ref drag (which moves the pointer past a few pixels).
        var position = e.GetPosition(this);
        if (_pressedOnOverflow is { } pressed && Math.Abs(position.X - pressed.X) < 4 && Math.Abs(position.Y - pressed.Y) < 4)
        {
            var row = IsOverExpandedRefs(position) && Rows is { } rows ? rows[_expandedRefsRow]
                : IsOnRefsOverflow(position) ? RowAt(position)
                : null;
            if (row is not null)
            {
                RefsOverflowClicked?.Invoke(this, row);
            }
        }

        _pressedOnOverflow = null;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Rows is null || Rows.Count == 0)
        {
            return;
        }

        var isDarkTheme = ActualThemeVariant == ThemeVariant.Dark;
        var columns = (Columns ?? new CommitGraphColumns()).Layout((float)Bounds.Width);
        context.Custom(new CommitGraphDrawOperation(
            new Rect(Bounds.Size), Rows, columns, SelectedRow, _hoveredDisplayRow, isDarkTheme, WipChangeCount, _isWipSelected && WipChangeCount > 0, SearchMatches, _compareRow, _expandedRefsRow));
    }
}
