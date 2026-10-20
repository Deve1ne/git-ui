using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace GitUi.Infrastructure.Git;

/// <summary>
/// <see cref="IRepositoryWatcher"/> over <see cref="FileSystemWatcher"/>: the work tree and the git
/// directory (plus the common directory of a linked worktree) are watched recursively. A burst of
/// events is reported once it has been quiet for a moment. Ref changes are confirmed against a
/// snapshot of every ref, so a rewritten file with the same content (or the app's own operation,
/// see <see cref="Rebaseline"/>) does not reload everything; work tree changes in ignored files
/// (build output, node_modules...) are dropped.
/// </summary>
public sealed class FileSystemRepositoryWatcher : IRepositoryWatcher
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(2);
    private const int MaxIgnoreChecks = 200;

    private readonly ILogger<FileSystemRepositoryWatcher> _logger;
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _pendingWorkTreePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;

    private string? _repositoryPath;
    private string? _workingDirectory;
    private IReadOnlyList<string> _gitDirectories = Array.Empty<string>();
    private string? _refsSnapshot;
    private bool _pendingReferences;
    private bool _pendingIndex;
    private bool _pendingOverflow;
    private DateTime _firstPending;
    private int _generation;

    public FileSystemRepositoryWatcher(ILogger<FileSystemRepositoryWatcher> logger)
    {
        _logger = logger;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action<RepositoryChangeKind>? Changed;

    public void Watch(string? repositoryPath)
    {
        lock (_gate)
        {
            if (string.Equals(repositoryPath, _repositoryPath, StringComparison.OrdinalIgnoreCase) && _watchers.Count > 0)
            {
                return;
            }

            StopWatching();
            _repositoryPath = repositoryPath;
            if (repositoryPath is null)
            {
                return;
            }

            try
            {
                using var repo = new Repository(repositoryPath);
                _workingDirectory = repo.Info.IsBare ? null : Path.GetFullPath(repo.Info.WorkingDirectory);
                var gitDirectory = Path.GetFullPath(repo.Info.Path);
                var commonFile = Path.Combine(gitDirectory, "commondir");
                var gitDirectories = new List<string> { gitDirectory };
                if (File.Exists(commonFile))
                {
                    gitDirectories.Add(Path.GetFullPath(Path.Combine(gitDirectory, File.ReadAllText(commonFile).Trim())));
                }

                _gitDirectories = gitDirectories;
                _refsSnapshot = RefsSnapshot(repo);
            }
            catch (Exception ex) when (ex is RepositoryNotFoundException or LibGit2SharpException or IOException)
            {
                _logger.LogWarning(ex, "Cannot watch {Path}", repositoryPath);
                return;
            }

            var roots = new List<string>();
            if (_workingDirectory is not null)
            {
                roots.Add(_workingDirectory);
            }

            roots.AddRange(_gitDirectories.Where(d => !roots.Any(r => IsUnder(d, r))));
            foreach (var root in roots.Where(Directory.Exists))
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Changed += OnEvent;
                watcher.Created += OnEvent;
                watcher.Deleted += OnEvent;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }
    }

    public void Rebaseline()
    {
        string? path;
        lock (_gate)
        {
            path = _repositoryPath;
        }

        if (path is null)
        {
            return;
        }

        var snapshot = TryRefsSnapshot(path);
        lock (_gate)
        {
            if (snapshot is not null && string.Equals(path, _repositoryPath, StringComparison.OrdinalIgnoreCase))
            {
                _refsSnapshot = snapshot;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopWatching();
        }

        _timer.Dispose();
    }

    private void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _pendingWorkTreePaths.Clear();
        _pendingReferences = _pendingIndex = _pendingOverflow = false;
        _workingDirectory = null;
        _gitDirectories = Array.Empty<string>();
        _refsSnapshot = null;
        _generation++;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => Record(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Record(e.OldFullPath);
        Record(e.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow (a huge checkout, a build): we lost events, so refresh everything.
        lock (_gate)
        {
            _pendingOverflow = true;
            Schedule();
        }
    }

    private void Record(string fullPath)
    {
        lock (_gate)
        {
            switch (RepositoryChangeClassifier.Classify(fullPath, _workingDirectory, _gitDirectories))
            {
                case RepositoryChangeKind.References:
                    _pendingReferences = true;
                    break;
                case RepositoryChangeKind.WorkingTree when _gitDirectories.Any(d => IsUnder(fullPath, d)):
                    _pendingIndex = true;
                    break;
                case RepositoryChangeKind.WorkingTree:
                    _pendingWorkTreePaths.Add(fullPath);
                    break;
                default:
                    return;
            }

            Schedule();
        }
    }

    // Called under the lock: wait for a quiet moment, but never longer than MaxDelay overall.
    private void Schedule()
    {
        var now = DateTime.UtcNow;
        if (!HasPendingBefore())
        {
            _firstPending = now;
        }

        var wait = Quiet;
        var deadline = _firstPending + MaxDelay - now;
        if (deadline < wait)
        {
            wait = deadline < TimeSpan.Zero ? TimeSpan.Zero : deadline;
        }

        _timer.Change(wait, Timeout.InfiniteTimeSpan);
    }

    private bool HasPendingBefore() => _firstPending != default;

    private void Flush()
    {
        string? path;
        string? workingDirectory;
        string? previousSnapshot;
        bool references, index, overflow;
        List<string> workTreePaths;
        int generation;
        lock (_gate)
        {
            path = _repositoryPath;
            workingDirectory = _workingDirectory;
            previousSnapshot = _refsSnapshot;
            references = _pendingReferences;
            index = _pendingIndex;
            overflow = _pendingOverflow;
            workTreePaths = _pendingWorkTreePaths.ToList();
            generation = _generation;
            _pendingReferences = _pendingIndex = _pendingOverflow = false;
            _pendingWorkTreePaths.Clear();
            _firstPending = default;
        }

        if (path is null)
        {
            return;
        }

        RepositoryChangeKind? change = null;
        try
        {
            using var repo = new Repository(path);
            if (references || overflow)
            {
                var snapshot = RefsSnapshot(repo);
                if (overflow || snapshot != previousSnapshot)
                {
                    lock (_gate)
                    {
                        if (generation == _generation)
                        {
                            _refsSnapshot = snapshot;
                        }
                    }

                    change = RepositoryChangeKind.References;
                }
            }

            if (change is null && (index || HasVisibleChange(repo, workingDirectory, workTreePaths)))
            {
                change = RepositoryChangeKind.WorkingTree;
            }
        }
        catch (Exception ex) when (ex is RepositoryNotFoundException or LibGit2SharpException or IOException)
        {
            // Mid-operation (a lock held, a rebase rewriting refs): the next event will retry.
            _logger.LogDebug(ex, "Repository change check failed for {Path}", path);
            change = references || overflow ? RepositoryChangeKind.References : RepositoryChangeKind.WorkingTree;
        }

        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }
        }

        if (change is { } kind)
        {
            Changed?.Invoke(kind);
        }
    }

    private static bool HasVisibleChange(Repository repo, string? workingDirectory, List<string> paths)
    {
        if (workingDirectory is null || paths.Count == 0)
        {
            return false;
        }

        // Past a few hundred files this is a checkout or a build: refreshing is cheaper than checking.
        if (paths.Count > MaxIgnoreChecks)
        {
            return true;
        }

        foreach (var path in paths)
        {
            if (RepositoryChangeClassifier.WorkTreeRelativePath(path, workingDirectory) is { Length: > 0 } relative
                && !repo.Ignore.IsPathIgnored(relative)
                && !(Directory.Exists(path) && repo.Ignore.IsPathIgnored(relative + "/")))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryRefsSnapshot(string path)
    {
        try
        {
            using var repo = new Repository(path);
            return RefsSnapshot(repo);
        }
        catch (Exception ex) when (ex is RepositoryNotFoundException or LibGit2SharpException or IOException)
        {
            return null;
        }
    }

    // Every ref with its target, HEAD, and the operation in progress (merge, rebase, cherry-pick...).
    private static string RefsSnapshot(Repository repo)
    {
        var refs = repo.Refs
            .Select(r => r.CanonicalName + "=" + r.TargetIdentifier)
            .OrderBy(s => s, StringComparer.Ordinal);
        var head = repo.Info.IsHeadDetached ? repo.Head.Tip?.Sha : repo.Refs.Head?.TargetIdentifier;
        return $"HEAD={head};state={repo.Info.CurrentOperation};" + string.Join(";", refs);
    }

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(directory.TrimEnd('/', '\\') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.TrimEnd('/', '\\'), directory.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
}
