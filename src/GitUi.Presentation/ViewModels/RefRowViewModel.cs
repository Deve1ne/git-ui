using Avalonia;
using GitUi.Application.Services;
using GitUi.Domain.Entities;

namespace GitUi.Presentation.ViewModels;

/// <summary>A folder or branch line of the LOCAL / REMOTE trees, with its graph visibility.</summary>
public sealed class RefRowViewModel
{
    public RefRowViewModel(RefTreeRow row, bool isHidden, bool isSolo)
    {
        Row = row;
        IsHidden = isHidden;
        IsSolo = isSolo;
    }

    public RefTreeRow Row { get; }

    public string Label => Row.Label;

    public GitBranch? Branch => Row.Branch;

    public bool IsFolder => Row.IsFolder;

    public bool IsBranch => !Row.IsFolder;

    /// <summary>A remote's own row in the REMOTE tree ("origin"): it gets the remote's actions.</summary>
    public bool IsRemoteRoot => Row.IsFolder && Row.Depth == 0 && Row.FolderKey?.StartsWith("remote/", StringComparison.Ordinal) == true;

    public bool IsHead => Row.Branch?.IsHead == true;

    /// <summary>The checked-out branch is bold, as in GitKraken.</summary>
    public Avalonia.Media.FontWeight Weight => IsHead ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;

    public int AheadBy => Row.Branch?.AheadBy ?? 0;

    public int BehindBy => Row.Branch?.BehindBy ?? 0;

    public string Chevron => Row.IsExpanded ? "▾" : "▸";

    /// <summary>12 px per tree level.</summary>
    public Thickness Indent => new(Row.Depth * 12, 0, 0, 0);

    /// <summary>Hidden from the graph (eye toggle); the row is drawn dimmed.</summary>
    public bool IsHidden { get; }

    /// <summary>Soloed: the graph shows only soloed branches.</summary>
    public bool IsSolo { get; }

    public double Opacity => IsHidden ? 0.45 : 1.0;

    public string HideGlyph => IsHidden ? "◌" : "◉";

    public string HideTip => IsHidden ? "Show in graph" : "Hide from graph";

    public string SoloTip => IsSolo ? "Stop soloing" : "Solo: show only this branch in the graph";
}
