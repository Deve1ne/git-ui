using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>
/// Sorts a changed file of a repository into what it invalidates. Git's own bookkeeping that never
/// changes what the app shows (objects, logs, lock files, hooks, LFS cache) is ignored: objects are
/// written before the ref that points to them, which is the change that matters.
/// </summary>
public static class RepositoryChangeClassifier
{
    private static readonly string[] IgnoredGitDirectories = { "objects", "logs", "hooks", "lfs", "modules", "info", "fsmonitor--daemon" };

    private static readonly string[] IgnoredGitFiles = { "FETCH_HEAD", "COMMIT_EDITMSG", "gc.log", "description", "gitk.cache" };

    /// <summary>
    /// The kind of change for <paramref name="fullPath"/>, or null when it does not matter.
    /// <paramref name="gitDirectories"/> are the repository's git directory and, for a linked
    /// worktree, its common directory; anything else under <paramref name="workingDirectory"/> is a work tree file.
    /// </summary>
    public static RepositoryChangeKind? Classify(string fullPath, string? workingDirectory, IReadOnlyList<string> gitDirectories)
    {
        foreach (var gitDirectory in gitDirectories)
        {
            if (RelativeTo(fullPath, gitDirectory) is { } relative)
            {
                return ClassifyGitFile(relative);
            }
        }

        if (workingDirectory is not null && RelativeTo(fullPath, workingDirectory) is { Length: > 0 } inWorkTree)
        {
            // A nested ".git" that is not ours belongs to a submodule or a nested repository.
            var first = FirstSegment(inWorkTree);
            return string.Equals(first, ".git", StringComparison.OrdinalIgnoreCase) ? null : RepositoryChangeKind.WorkingTree;
        }

        return null;
    }

    /// <summary>The path of <paramref name="fullPath"/> under <paramref name="workingDirectory"/> with '/' separators, for ignore checks.</summary>
    public static string? WorkTreeRelativePath(string fullPath, string workingDirectory) =>
        RelativeTo(fullPath, workingDirectory)?.Replace('\\', '/');

    private static RepositoryChangeKind? ClassifyGitFile(string relative)
    {
        if (relative.Length == 0 || relative.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var first = FirstSegment(relative);
        if (IgnoredGitDirectories.Contains(first, StringComparer.OrdinalIgnoreCase)
            || IgnoredGitFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)
            || relative.StartsWith("tmp_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.Equals(relative, "index", StringComparison.OrdinalIgnoreCase)
            ? RepositoryChangeKind.WorkingTree
            : RepositoryChangeKind.References;
    }

    private static string FirstSegment(string relative)
    {
        var separator = relative.IndexOfAny(new[] { '/', '\\' });
        return separator < 0 ? relative : relative[..separator];
    }

    private static string? RelativeTo(string fullPath, string directory)
    {
        var root = directory.TrimEnd('/', '\\');
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (fullPath.Length == root.Length)
        {
            return string.Empty;
        }

        return fullPath[root.Length] is '/' or '\\' ? fullPath[(root.Length + 1)..] : null;
    }
}
