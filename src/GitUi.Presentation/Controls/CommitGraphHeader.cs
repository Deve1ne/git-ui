using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GitUi.Presentation.Controls;

/// <summary>
/// Column header row for <see cref="CommitGraphCanvas"/>. Columns left of the message are resized
/// from their right edge, columns right of it from their left edge (the message absorbs the rest).
/// </summary>
public sealed class CommitGraphHeader : Control
{
    private const float EdgeHitWidth = 4f;

    public static readonly StyledProperty<CommitGraphColumns?> ColumnsProperty =
        AvaloniaProperty.Register<CommitGraphHeader, CommitGraphColumns?>(nameof(Columns));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<CommitGraphHeader, IBrush?>(nameof(Foreground), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> SeparatorBrushProperty =
        AvaloniaProperty.Register<CommitGraphHeader, IBrush?>(nameof(SeparatorBrush), Brushes.LightGray);

    private CommitGraphColumn? _dragColumn;
    private double _dragStartX;
    private float _dragStartWidth;

    public CommitGraphColumns? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? SeparatorBrush
    {
        get => GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    static CommitGraphHeader()
    {
        AffectsRender<CommitGraphHeader>(ColumnsProperty, ForegroundProperty, SeparatorBrushProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

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

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, CommitGraphMetrics.HeaderHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Transparent fill so the whole strip receives pointer events.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        if (Columns is null)
        {
            return;
        }

        var separatorPen = new Pen(SeparatorBrush, 1);
        foreach (var column in Columns.Layout((float)Bounds.Width))
        {
            var text = new FormattedText(
                CommitGraphColumns.HeaderLabel(column.Column),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
                10.5,
                Foreground)
            {
                MaxTextWidth = Math.Max(1, column.Width - CommitGraphMetrics.CellPadding * 2),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };

            var y = (Bounds.Height - text.Height) / 2;
            context.DrawText(text, new Point(column.Left + CommitGraphMetrics.CellPadding, y));

            if (column.Left > 0)
            {
                context.DrawLine(separatorPen, new Point(column.Left + 0.5, 4), new Point(column.Left + 0.5, Bounds.Height - 4));
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var x = e.GetPosition(this).X;
        if (_dragColumn is { } dragging && Columns is not null)
        {
            var delta = (float)(x - _dragStartX);
            var grows = IsLeftOfMessage(dragging) ? delta : -delta;
            Columns.SetWidth(dragging, _dragStartWidth + grows);
            return;
        }

        Cursor = HitEdge(x) is null ? Cursor.Default : new Cursor(StandardCursorType.SizeWestEast);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var x = e.GetPosition(this).X;
        if (Columns is null || HitEdge(x) is not { } column)
        {
            return;
        }

        _dragColumn = column;
        _dragStartX = x;
        _dragStartWidth = Columns.GetWidth(column);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>Right-click: GitKraken's column menu, a check item per optional column.</summary>
    public CommitGraphHeader() => ContextRequested += OnColumnMenuRequested;

    private void OnColumnMenuRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Columns is not { } columns)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (var column in CommitGraphColumns.Optional)
        {
            var item = new MenuItem
            {
                Header = CommitGraphColumns.HeaderLabel(column),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = columns.IsVisible(column),
            };
            item.Click += (_, _) => columns.SetVisible(column, !columns.IsVisible(column));
            menu.Items.Add(item);
        }

        menu.Open(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragColumn is not null)
        {
            _dragColumn = null;
            e.Pointer.Capture(null);
        }
    }

    private static bool IsLeftOfMessage(CommitGraphColumn column) =>
        column is CommitGraphColumn.Refs or CommitGraphColumn.Graph;

    private CommitGraphColumn? HitEdge(double x)
    {
        if (Columns is null)
        {
            return null;
        }

        foreach (var column in Columns.Layout((float)Bounds.Width))
        {
            if (column.Column == CommitGraphColumn.Message)
            {
                continue;
            }

            var edge = IsLeftOfMessage(column.Column) ? column.Right : column.Left;
            if (Math.Abs(x - edge) <= EdgeHitWidth)
            {
                return column.Column;
            }
        }

        return null;
    }
}
