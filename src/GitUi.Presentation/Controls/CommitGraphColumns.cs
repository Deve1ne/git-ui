namespace GitUi.Presentation.Controls;

public enum CommitGraphColumn { Refs, Graph, Message, Author, Date, Sha }

public readonly record struct CommitGraphColumnBounds(CommitGraphColumn Column, float Left, float Width)
{
    public float Right => Left + Width;
}

/// <summary>
/// Shared column widths for the graph header and the graph canvas. The message column takes
/// whatever width is left, so it is the only column without a stored width.
/// </summary>
public sealed class CommitGraphColumns
{
    public const float MinimumMessageWidth = 120f;
    public const float PreferredMessageWidth = 250f;

    private static readonly CommitGraphColumn[] DropOrder =
    {
        CommitGraphColumn.Sha,
        CommitGraphColumn.Date,
        CommitGraphColumn.Author,
    };

    private readonly Dictionary<CommitGraphColumn, float> _widths = new()
    {
        [CommitGraphColumn.Refs] = 130f,
        [CommitGraphColumn.Graph] = 150f,
        [CommitGraphColumn.Author] = 130f,
        [CommitGraphColumn.Date] = 130f,
        [CommitGraphColumn.Sha] = 70f,
    };

    private static readonly Dictionary<CommitGraphColumn, (float Min, float Max)> Limits = new()
    {
        [CommitGraphColumn.Refs] = (32f, 300f),
        [CommitGraphColumn.Graph] = (40f, 600f),
        [CommitGraphColumn.Author] = (32f, 250f),
        [CommitGraphColumn.Date] = (60f, 200f),
        [CommitGraphColumn.Sha] = (50f, 130f),
    };

    public static readonly IReadOnlyList<CommitGraphColumn> Order = new[]
    {
        CommitGraphColumn.Refs,
        CommitGraphColumn.Graph,
        CommitGraphColumn.Message,
        CommitGraphColumn.Author,
        CommitGraphColumn.Date,
        CommitGraphColumn.Sha,
    };

    public event Action? Changed;

    /// <summary>Columns the user can turn off (the graph and the message always show).</summary>
    public static readonly IReadOnlyList<CommitGraphColumn> Optional = new[]
    {
        CommitGraphColumn.Refs,
        CommitGraphColumn.Author,
        CommitGraphColumn.Date,
        CommitGraphColumn.Sha,
    };

    private readonly HashSet<CommitGraphColumn> _hidden = new();

    public bool IsVisible(CommitGraphColumn column) => !_hidden.Contains(column);

    public void SetVisible(CommitGraphColumn column, bool visible)
    {
        if (!Optional.Contains(column) || (visible ? !_hidden.Remove(column) : !_hidden.Add(column)))
        {
            return;
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<string> ExportHidden() => _hidden.Select(c => c.ToString()).ToList();

    public void ImportHidden(IReadOnlyList<string>? hidden)
    {
        _hidden.Clear();
        foreach (var name in hidden ?? Array.Empty<string>())
        {
            if (Enum.TryParse<CommitGraphColumn>(name, out var column) && Optional.Contains(column))
            {
                _hidden.Add(column);
            }
        }

        Changed?.Invoke();
    }

    public float GetWidth(CommitGraphColumn column) => _widths[column];

    public void SetWidth(CommitGraphColumn column, float width)
    {
        var (min, max) = Limits[column];
        var clamped = Math.Clamp(width, min, max);
        if (Math.Abs(_widths[column] - clamped) < 0.5f)
        {
            return;
        }

        _widths[column] = clamped;
        Changed?.Invoke();
    }

    /// <summary>Stored widths keyed by column name, for persisting in settings.</summary>
    public IReadOnlyDictionary<string, float> Export() => _widths.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

    public void Import(IReadOnlyDictionary<string, float>? widths)
    {
        if (widths is null)
        {
            return;
        }

        foreach (var (name, width) in widths)
        {
            if (Enum.TryParse<CommitGraphColumn>(name, out var column) && _widths.ContainsKey(column))
            {
                var (min, max) = Limits[column];
                _widths[column] = Math.Clamp(width, min, max);
            }
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<CommitGraphColumnBounds> Layout(float totalWidth)
    {
        // When the panel is narrow the message keeps a readable width and the trailing metadata
        // columns drop out instead, least useful first.
        var visible = Order.Where(c => !_hidden.Contains(c)).ToList();
        foreach (var droppable in DropOrder)
        {
            if (totalWidth - visible.Where(c => c != CommitGraphColumn.Message).Sum(c => _widths[c]) >= PreferredMessageWidth)
            {
                break;
            }

            visible.Remove(droppable);
        }

        var fixedWidth = visible.Where(c => c != CommitGraphColumn.Message).Sum(c => _widths[c]);
        var messageWidth = Math.Max(MinimumMessageWidth, totalWidth - fixedWidth);

        var bounds = new List<CommitGraphColumnBounds>(visible.Count);
        var left = 0f;
        foreach (var column in visible)
        {
            var width = column == CommitGraphColumn.Message ? messageWidth : _widths[column];
            bounds.Add(new CommitGraphColumnBounds(column, left, width));
            left += width;
        }

        return bounds;
    }

    public static string HeaderLabel(CommitGraphColumn column) => column switch
    {
        CommitGraphColumn.Refs => "BRANCH / TAG",
        CommitGraphColumn.Graph => "GRAPH",
        CommitGraphColumn.Message => "COMMIT MESSAGE",
        CommitGraphColumn.Author => "AUTHOR",
        CommitGraphColumn.Date => "DATE / TIME",
        CommitGraphColumn.Sha => "SHA",
        _ => string.Empty,
    };
}
