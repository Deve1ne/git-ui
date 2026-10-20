using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

public partial class MergeToolView : UserControl
{
    /// <summary>Height of every pane / output row (set in the XAML templates).</summary>
    private const double RowHeight = 22;

    /// <summary>Rows kept visible above a conflict when jumping to it.</summary>
    private const int ContextRows = 3;

    private MergeToolViewModel? _wired;
    private bool _syncing;

    public MergeToolView()
    {
        InitializeComponent();

        // A and B scroll together, like GitKraken's merge tool: their rows are aligned (fillers pad
        // the shorter side of each conflict), so equal offsets show the same lines face to face.
        var ours = this.FindControl<ScrollViewer>("OursScroll")!;
        var theirs = this.FindControl<ScrollViewer>("TheirsScroll")!;
        ours.ScrollChanged += (_, _) => Mirror(ours, theirs);
        theirs.ScrollChanged += (_, _) => Mirror(theirs, ours);

        DataContextChanged += (_, _) =>
        {
            if (_wired is not null)
            {
                _wired.ScrollToConflictRequested -= ScrollToConflict;
            }

            _wired = DataContext as MergeToolViewModel;
            if (_wired is not null)
            {
                _wired.ScrollToConflictRequested += ScrollToConflict;
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Mirror(ScrollViewer source, ScrollViewer target)
    {
        if (_syncing || target.Offset == source.Offset)
        {
            return;
        }

        _syncing = true;
        try
        {
            target.Offset = source.Offset;
        }
        finally
        {
            _syncing = false;
        }
    }

    // Brings the current conflict into view in A/B (one offset, they are synced) and in the output.
    private void ScrollToConflict(int paneRow, int outputRow)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ScrollToRow("OursScroll", paneRow);
            ScrollToRow("OutputScroll", outputRow);
        }, DispatcherPriority.Background);
    }

    private void ScrollToRow(string scrollViewerName, int row)
    {
        var scroll = this.FindControl<ScrollViewer>(scrollViewerName)!;
        var y = Math.Max(0, (row - ContextRows) * RowHeight);

        // On the tool's first appearance the rows are not laid out yet: an offset set now would be
        // clamped to the empty extent. Apply it again once layout has given the content its height.
        void Apply() => scroll.Offset = new Vector(scroll.Offset.X, Math.Min(y, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));

        if (scroll.Extent.Height - scroll.Viewport.Height >= y)
        {
            Apply();
            return;
        }

        void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (scroll.Extent.Height - scroll.Viewport.Height >= y || scroll.Extent.Height > 0)
            {
                scroll.LayoutUpdated -= OnLayoutUpdated;
                Apply();
            }
        }

        scroll.LayoutUpdated += OnLayoutUpdated;
    }
}
