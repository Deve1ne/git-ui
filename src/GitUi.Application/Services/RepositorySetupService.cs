using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>Clone and initialize repositories, like the Clone / Init forms of the new-tab page.</summary>
public sealed class RepositorySetupService
{
    /// <summary>.gitignore templates offered when initializing a repository.</summary>
    public static readonly IReadOnlyDictionary<string, string> GitIgnoreTemplates = new Dictionary<string, string>
    {
        ["None"] = string.Empty,
        ["VisualStudio"] = "bin/\nobj/\n.vs/\n*.user\n*.suo\n*.userprefs\nTestResults/\npackages/\n*.nupkg\n",
        ["Node"] = "node_modules/\ndist/\ncoverage/\n.env\nnpm-debug.log*\nyarn-error.log*\n",
        ["Python"] = "__pycache__/\n*.py[cod]\n.venv/\nvenv/\n.env\nbuild/\ndist/\n*.egg-info/\n.pytest_cache/\n",
        ["Java"] = "target/\nbuild/\n*.class\n*.jar\n.gradle/\n.idea/\n",
    };

    private readonly IGitRepositoryWriter _writer;

    public RepositorySetupService(IGitRepositoryWriter writer) => _writer = writer;

    /// <summary>
    /// Folder name git would create for <paramref name="url"/>: the last path segment without ".git",
    /// for https URLs as well as scp-like SSH ("git@host:org/repo.git") and local paths.
    /// </summary>
    public static string DeriveFolderName(string url)
    {
        var trimmed = url.Trim().TrimEnd('/', '\\');
        var lastSeparator = trimmed.LastIndexOfAny(new[] { '/', '\\', ':' });
        var name = lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;
        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <returns>The path of the new repository.</returns>
    public async Task<string> CloneAsync(string url, string parentDirectory, string? folderName, CloneOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Enter the URL of the repository to clone.");
        }

        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidOperationException("Choose where to clone the repository.");
        }

        var name = string.IsNullOrWhiteSpace(folderName) ? DeriveFolderName(url) : folderName.Trim();
        var destination = Path.Combine(parentDirectory, name);
        EnsureEmptyOrMissing(destination);

        await _writer.CloneAsync(url.Trim(), destination, options, cancellationToken);
        return destination;
    }

    /// <returns>The path of the new repository.</returns>
    public async Task<string> InitAsync(string parentDirectory, string name, string defaultBranch, string gitIgnoreTemplate, bool createInitialCommit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidOperationException("Choose a location and a name for the repository.");
        }

        var path = Path.Combine(parentDirectory, name.Trim());
        EnsureEmptyOrMissing(path);
        await _writer.InitAsync(path, string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim(), cancellationToken);

        if (GitIgnoreTemplates.TryGetValue(gitIgnoreTemplate, out var template) && template.Length > 0)
        {
            await _writer.WriteWorkingFileAsync(path, ".gitignore", template, cancellationToken);
        }

        if (createInitialCommit)
        {
            await _writer.WriteWorkingFileAsync(path, "README.md", $"# {name.Trim()}\n", cancellationToken);
            await _writer.StageAsync(path, ".", cancellationToken);
            await _writer.CommitAsync(path, "Initial commit", cancellationToken);
        }

        return path;
    }

    private static void EnsureEmptyOrMissing(string path)
    {
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"'{path}' already exists and is not empty.");
        }
    }
}
