using Avalonia;
using GitUi.Application.Services;
using GitUi.Domain.Entities;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// One line of a changed-files list: a file, or (in Tree view) a folder that folds. In Path view
/// every line is a file labelled with its full path.
/// </summary>
public class FileRow<T> where T : class
{
    public FileRow(string label, int depth, string? folderKey, T? item, bool isExpanded)
    {
        Label = label;
        Depth = depth;
        FolderKey = folderKey;
        Item = item;
        IsExpanded = isExpanded;
    }

    public string Label { get; }

    public int Depth { get; }

    public string? FolderKey { get; }

    public T? Item { get; }

    public bool IsExpanded { get; }

    public bool IsFolder => Item is null;

    public bool IsFile => Item is not null;

    public string Chevron => IsExpanded ? "▾" : "▸";

    /// <summary>14 px per tree level.</summary>
    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    /// <summary>Rows for a list, as a tree or as plain paths.</summary>
    public static IEnumerable<(string Label, int Depth, string? FolderKey, T? Item, bool IsExpanded)> Layout(
        IEnumerable<T> items, Func<T, string> pathOf, bool asTree, IReadOnlySet<string> collapsed) =>
        asTree
            ? FileTree.Build(items, pathOf, collapsed).Select(r => (r.Label, r.Depth, r.FolderKey, r.Item, r.IsExpanded))
            : items.Select(i => (pathOf(i), 0, (string?)null, (T?)i, true));
}

public sealed class CommitFileRow : FileRow<CommitFileChange>
{
    public CommitFileRow(string label, int depth, string? folderKey, CommitFileChange? item, bool isExpanded)
        : base(label, depth, folderKey, item, isExpanded)
    {
    }
}

public sealed class ChangeFileRow : FileRow<FileChange>
{
    public ChangeFileRow(string label, int depth, string? folderKey, FileChange? item, bool isExpanded)
        : base(label, depth, folderKey, item, isExpanded)
    {
    }
}
