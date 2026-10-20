namespace GitUi.Application.Services;

/// <summary>A folder or file line of a changed-files tree; <see cref="Item"/> is null on folders.</summary>
/// <param name="FolderKey">Identifies a folder for expand/collapse ("src/app"); null on file rows.</param>
public sealed record FileTreeRow<T>(string Label, int Depth, string? FolderKey, T? Item, bool IsExpanded = true) where T : class
{
    public bool IsFolder => Item is null;
}

/// <summary>
/// GitKraken's "Tree" view of changed files: files grouped under their folders, folders first.
/// A folder holding a single sub-folder and nothing else is merged with it ("src/app") to keep
/// deep paths short.
/// </summary>
public static class FileTree
{
    public static IReadOnlyList<FileTreeRow<T>> Build<T>(IEnumerable<T> items, Func<T, string> pathOf, IReadOnlySet<string> collapsed) where T : class
    {
        var root = new Folder<T>(string.Empty);
        foreach (var item in items)
        {
            var segments = pathOf(item).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var folder = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                folder = folder.Child(segments[i]);
            }

            folder.Files.Add((segments.Length == 0 ? string.Empty : segments[^1], item));
        }

        var rows = new List<FileTreeRow<T>>();
        Emit(root, 0, string.Empty, rows, collapsed);
        return rows;
    }

    private static void Emit<T>(Folder<T> folder, int depth, string path, List<FileTreeRow<T>> rows, IReadOnlySet<string> collapsed) where T : class
    {
        foreach (var child in folder.Folders.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            // Merge single-child chains: "src" > "app" > files becomes "src/app" > files.
            var label = child.Name;
            var current = child;
            while (current.Files.Count == 0 && current.Folders.Count == 1)
            {
                current = current.Folders.Values.First();
                label += "/" + current.Name;
            }

            var key = path.Length == 0 ? label : $"{path}/{label}";
            var expanded = !collapsed.Contains(key);
            rows.Add(new FileTreeRow<T>(label, depth, key, null, expanded));
            if (expanded)
            {
                Emit(current, depth + 1, key, rows, collapsed);
            }
        }

        foreach (var (name, item) in folder.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new FileTreeRow<T>(name, depth, null, item));
        }
    }

    private sealed class Folder<T>
    {
        public Folder(string name) => Name = name;

        public string Name { get; }

        public Dictionary<string, Folder<T>> Folders { get; } = new(StringComparer.Ordinal);

        public List<(string Name, T Item)> Files { get; } = new();

        public Folder<T> Child(string name)
        {
            if (!Folders.TryGetValue(name, out var child))
            {
                child = new Folder<T>(name);
                Folders[name] = child;
            }

            return child;
        }
    }
}
