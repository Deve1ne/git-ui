using GitUi.Domain.Entities;

namespace GitUi.Application.Services;

/// <summary>
/// One line of the branch tree in the ref panel: a folder ("feature", "origin") or a branch. The
/// tree is flattened into rows with a depth so the panel stays a plain, virtualized list.
/// </summary>
/// <param name="Label">The last path segment shown on the line ("login" for "feature/login").</param>
/// <param name="FolderKey">Identifies a folder for expand/collapse; null on branch rows.</param>
public sealed record RefTreeRow(string Label, int Depth, string? FolderKey, GitBranch? Branch, bool IsExpanded = true)
{
    public bool IsFolder => Branch is null;
}

/// <summary>Builds GitKraken's folder view of branches: "feature/a" and "feature/b" share a "feature" folder.</summary>
public static class RefTree
{
    /// <param name="keyPrefix">Distinguishes the local and remote trees' folder keys ("local", "remote").</param>
    /// <param name="collapsed">Folder keys the user collapsed; ignored when <paramref name="expandAll"/> (e.g. while filtering).</param>
    /// <param name="rootFolders">Top-level folders to show even when empty (remotes without fetched branches).</param>
    public static IReadOnlyList<RefTreeRow> Build(IEnumerable<GitBranch> branches, string keyPrefix, IReadOnlySet<string> collapsed, bool expandAll = false, IEnumerable<string>? rootFolders = null)
    {
        var root = new Folder(string.Empty);
        foreach (var folder in rootFolders ?? Enumerable.Empty<string>())
        {
            root.Child(folder);
        }

        foreach (var branch in branches)
        {
            var segments = branch.Name.Split('/');
            var folder = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                folder = folder.Child(segments[i]);
            }

            folder.Branches.Add((segments[^1], branch));
        }

        var rows = new List<RefTreeRow>();
        Emit(root, depth: 0, path: keyPrefix, rows, collapsed, expandAll);
        return rows;
    }

    private static void Emit(Folder folder, int depth, string path, List<RefTreeRow> rows, IReadOnlySet<string> collapsed, bool expandAll)
    {
        // Folders first, then branches, each alphabetically — the order GitKraken uses.
        foreach (var child in folder.Folders.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var key = $"{path}/{child.Name}";
            var expanded = expandAll || !collapsed.Contains(key);
            rows.Add(new RefTreeRow(child.Name, depth, key, null, expanded));
            if (expanded)
            {
                Emit(child, depth + 1, key, rows, collapsed, expandAll);
            }
        }

        foreach (var (label, branch) in folder.Branches.OrderBy(b => b.Label, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new RefTreeRow(label, depth, null, branch));
        }
    }

    private sealed class Folder
    {
        public Folder(string name) => Name = name;

        public string Name { get; }

        public Dictionary<string, Folder> Folders { get; } = new(StringComparer.Ordinal);

        public List<(string Label, GitBranch Branch)> Branches { get; } = new();

        public Folder Child(string name)
        {
            if (!Folders.TryGetValue(name, out var child))
            {
                child = new Folder(name);
                Folders[name] = child;
            }

            return child;
        }
    }
}
