using GitUi.Application.Abstractions;
using GitUi.Domain.Entities;
using GitUi.Domain.ValueObjects;
using LibGit2Sharp;

namespace GitUi.Infrastructure.Git;

// LibGit2Sharp is synchronous and can take seconds on large repositories: every read runs on the
// thread pool so the UI thread (and animations such as the loading screen) never stalls on git.
public sealed class LibGit2SharpRepositoryReader : IGitRepositoryReader
{
    public bool IsValidRepository(string path) => Repository.IsValid(path);

    public Task<GitRepository> GetRepositoryAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<GitRepository>(() =>
        {
            using var repo = new Repository(path);
            var isDirty = repo.RetrieveStatus().IsDirty;
            return new GitRepository(path, repo.Head.FriendlyName, isDirty, repo.Head.Tip?.Sha);
        }, cancellationToken);

    public Task<IReadOnlyList<GitCommit>> GetCommitsAsync(string path, CancellationToken cancellationToken = default)
        => GetCommitsAsync(path, GraphRefFilter.Empty, cancellationToken);

    public Task<IReadOnlyList<GitCommit>> GetCommitsAsync(string path, GraphRefFilter graphFilter, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<GitCommit>>(() =>
        {
            using var repo = new Repository(path);

            // refs/stash would drag each stash's internal index and untracked-files commits into
            // the graph; stashes are added below as one node each, like GitKraken.
            var refs = repo.Refs.Where(r => r.CanonicalName != "refs/stash").ToList();
            var solo = refs.Where(r => graphFilter.Solo.Contains(r.CanonicalName)).ToList();
            var included = solo.Count > 0
                ? solo
                : refs.Where(r => !graphFilter.Hidden.Contains(r.CanonicalName)).ToList();
            if (included.Count == 0)
            {
                return Array.Empty<GitCommit>();
            }

            var filter = new CommitFilter
            {
                IncludeReachableFrom = included,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            };
            var commits = repo.Commits.QueryBy(filter)
                .Select(c => new GitCommit(c.Sha, c.MessageShort, c.Author.Name, c.Author.When, c.Parents.Select(p => p.Sha).ToList()))
                .ToList();

            foreach (var stash in repo.Stashes)
            {
                var stashCommit = new GitCommit(stash.WorkTree.Sha, stash.Message, stash.WorkTree.Author.Name, stash.WorkTree.Author.When, new[] { stash.Base.Sha }, IsStash: true);
                InsertAboveParent(commits, stashCommit);
            }

            return (IReadOnlyList<GitCommit>)commits;
        }, cancellationToken);

    /// <summary>
    /// Inserts a stash row where its date puts it, but always above the commit it was made on:
    /// the graph layout needs every child before its parent.
    /// </summary>
    private static void InsertAboveParent(List<GitCommit> commits, GitCommit stash)
    {
        var parentIndex = commits.FindIndex(c => c.Sha == stash.ParentShas[0]);
        if (parentIndex < 0)
        {
            return;
        }

        var olderIndex = commits.FindIndex(c => c.Date < stash.Date);
        commits.Insert(olderIndex < 0 ? parentIndex : Math.Min(olderIndex, parentIndex), stash);
    }

    public Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<GitBranch>>(() =>
        {
            using var repo = new Repository(path);
            IReadOnlyList<GitBranch> branches = repo.Branches
                .Select(b => new GitBranch(
                    b.FriendlyName,
                    !b.IsRemote,
                    b.IsRemote,
                    b.TrackedBranch?.FriendlyName,
                    b.Tip?.Sha ?? string.Empty,
                    b.IsCurrentRepositoryHead,
                    b.IsTracking ? b.TrackingDetails.AheadBy ?? 0 : 0,
                    b.IsTracking ? b.TrackingDetails.BehindBy ?? 0 : 0))
                .ToList();
            return branches;
        }, cancellationToken);

    public Task<IReadOnlyList<FileChange>> GetStatusAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<FileChange>>(() =>
        {
            using var repo = new Repository(path);

            // LibGit2Sharp includes ignored files by default; a working-directory view must not list bin/ or obj/.
            var options = new StatusOptions { IncludeIgnored = false, RecurseUntrackedDirs = true };

            // A file can have changes in the index *and* further changes in the working directory
            // (stage, then edit again): it then belongs to both the staged and the unstaged list.
            var changes = new List<FileChange>();
            foreach (var entry in repo.RetrieveStatus(options))
            {
                // An unresolved conflict is neither staged nor a plain modification: it must be resolved
                // in the working directory and then staged.
                if (entry.State.HasFlag(FileStatus.Conflicted))
                {
                    changes.Add(new FileChange(entry.FilePath, FileChangeType.Conflicted, IsStaged: false));
                    continue;
                }

                if (IsStagedState(entry.State))
                {
                    changes.Add(new FileChange(entry.FilePath, MapIndexChangeType(entry.State), IsStaged: true));
                }

                if (IsUnstagedState(entry.State))
                {
                    changes.Add(new FileChange(entry.FilePath, MapWorkdirChangeType(entry.State), IsStaged: false));
                }
            }

            return changes;
        }, cancellationToken);

    public Task<IReadOnlyList<DiffHunk>> GetDiffAsync(string path, string filePath, bool staged, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<DiffHunk>>(() =>
        {
            using var repo = new Repository(path);

            // includeUntracked: a new, never-staged file otherwise has no patch at all.
            var patch = staged
                ? repo.Diff.Compare<Patch>(repo.Head.Tip?.Tree, DiffTargets.Index, new[] { filePath })
                : repo.Diff.Compare<Patch>(new[] { filePath }, includeUntracked: true);

            var entry = patch[filePath];
            return entry is null ? Array.Empty<DiffHunk>() : ParsePatch(entry.Patch);
        }, cancellationToken);

    public Task<IReadOnlyList<GitStash>> GetStashesAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<GitStash>>(() =>
        {
            using var repo = new Repository(path);
            IReadOnlyList<GitStash> stashes = repo.Stashes
                .Select((s, index) => new GitStash(index, s.Message, s.WorkTree.Sha, s.WorkTree.Author.When))
                .ToList();
            return stashes;
        }, cancellationToken);

    public Task<GitAncestry> GetAncestryAsync(string path, string firstRevision, string secondRevision, CancellationToken cancellationToken = default)
        => Task.Run<GitAncestry>(() =>
        {
            using var repo = new Repository(path);
            var first = repo.Lookup<Commit>(firstRevision) ?? throw new InvalidOperationException($"Unknown revision '{firstRevision}'.");
            var second = repo.Lookup<Commit>(secondRevision) ?? throw new InvalidOperationException($"Unknown revision '{secondRevision}'.");

            if (first.Sha == second.Sha)
            {
                return GitAncestry.Same;
            }

            var mergeBase = repo.ObjectDatabase.FindMergeBase(first, second);
            var ancestry = mergeBase?.Sha == first.Sha ? GitAncestry.FirstIsAncestor
                : mergeBase?.Sha == second.Sha ? GitAncestry.SecondIsAncestor
                : GitAncestry.Diverged;
            return ancestry;
        }, cancellationToken);

    public Task<GitOperationState> GetOperationStateAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<GitOperationState>(() =>
        {
            using var repo = new Repository(path);
            var state = repo.Info.CurrentOperation switch
            {
                CurrentOperation.Merge => GitOperationState.Merge,
                CurrentOperation.Rebase or CurrentOperation.RebaseInteractive or CurrentOperation.RebaseMerge
                    or CurrentOperation.ApplyMailboxOrRebase => GitOperationState.Rebase,
                CurrentOperation.CherryPick or CurrentOperation.CherryPickSequence => GitOperationState.CherryPick,
                CurrentOperation.Revert or CurrentOperation.RevertSequence => GitOperationState.Revert,
                _ => GitOperationState.None,
            };
            return state;
        }, cancellationToken);

    public Task<ConflictVersions> GetConflictVersionsAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => Task.Run<ConflictVersions>(() =>
        {
            using var repo = new Repository(path);
            var conflict = repo.Index.Conflicts[filePath.Replace('\\', '/')];
            return (conflict is null
                ? new ConflictVersions(true, true, true)
                : new ConflictVersions(conflict.Ancestor is not null, conflict.Ours is not null, conflict.Theirs is not null));
        }, cancellationToken);

    public Task<ConflictStageContents> GetConflictStageContentsAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            using var repo = new Repository(path);
            var conflict = repo.Index.Conflicts[filePath.Replace('\\', '/')];
            byte[]? Read(IndexEntry? entry)
            {
                if (entry is null || repo.Lookup<Blob>(entry.Id) is not { } blob)
                {
                    return null;
                }

                using var stream = blob.GetContentStream();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }

            return conflict is null
                ? new ConflictStageContents(null, null, null)
                : new ConflictStageContents(Read(conflict.Ancestor), Read(conflict.Ours), Read(conflict.Theirs));
        }, cancellationToken);

    public Task<string> ReadWorkingFileAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => File.ReadAllTextAsync(Path.Combine(path, filePath), cancellationToken);

    public Task<MergePrediction> PredictMergeAsync(string path, string ours, string theirs, CancellationToken cancellationToken = default)
        => Task.Run<MergePrediction>(() =>
        {
            using var repo = new Repository(path);
            var oursCommit = repo.Lookup<Commit>(ours) ?? throw new InvalidOperationException($"Unknown revision '{ours}'.");
            var theirsCommit = repo.Lookup<Commit>(theirs) ?? throw new InvalidOperationException($"Unknown revision '{theirs}'.");

            // In-memory three-way merge: the repository, index and working directory are untouched.
            var result = repo.ObjectDatabase.MergeCommits(oursCommit, theirsCommit, new MergeTreeOptions { FailOnConflict = false });
            if (result.Status != MergeTreeStatus.Conflicts)
            {
                return MergePrediction.Clean;
            }

            var files = result.Conflicts
                .Select(c => c.Ours?.Path ?? c.Theirs?.Path ?? c.Ancestor?.Path)
                .Where(p => p is not null)
                .Select(p => p!)
                .Distinct()
                .ToList();
            return new MergePrediction(true, files);
        }, cancellationToken);

    public Task<CommitDetails> GetCommitDetailsAsync(string path, string sha, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            using var repo = new Repository(path);
            var commit = repo.Lookup<Commit>(sha) ?? throw new InvalidOperationException($"Unknown commit '{sha}'.");
            var parentTree = commit.Parents.FirstOrDefault()?.Tree;

            // Like GitKraken, a merge commit is shown against its first parent.
            using var patch = repo.Diff.Compare<Patch>(parentTree, commit.Tree, RenameDetection);
            var files = patch
                .Select(entry => new CommitFileChange(
                    entry.Path,
                    entry.Status == ChangeKind.Renamed ? entry.OldPath : null,
                    MapChangeKind(entry.Status),
                    entry.LinesAdded,
                    entry.LinesDeleted))
                .ToList();

            // A stash made with --include-untracked keeps its new files in a third parent.
            if (StashUntrackedTree(commit) is { } untrackedTree)
            {
                using var untracked = repo.Diff.Compare<Patch>(null, untrackedTree);
                files.AddRange(untracked.Select(entry => new CommitFileChange(entry.Path, null, FileChangeType.Added, entry.LinesAdded, 0)));
            }

            files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));

            var (summary, body) = SplitMessage(commit.Message);
            return new CommitDetails(
                commit.Sha,
                summary,
                body,
                commit.Author.Name,
                commit.Author.Email,
                commit.Author.When,
                commit.Committer.Name,
                commit.Committer.Email,
                commit.Committer.When,
                commit.Parents.Select(p => p.Sha).ToList(),
                files);
        }, cancellationToken);

    public Task<IReadOnlyList<DiffHunk>> GetCommitFileDiffAsync(string path, string sha, string filePath, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<DiffHunk>>(() =>
        {
            using var repo = new Repository(path);
            var commit = repo.Lookup<Commit>(sha) ?? throw new InvalidOperationException($"Unknown commit '{sha}'.");
            using var patch = repo.Diff.Compare<Patch>(commit.Parents.FirstOrDefault()?.Tree, commit.Tree, RenameDetection);
            var entry = patch.FirstOrDefault(e => e.Path == filePath || e.OldPath == filePath);
            if (entry is not null)
            {
                return ParsePatch(entry.Patch);
            }

            if (StashUntrackedTree(commit) is { } untrackedTree)
            {
                using var untracked = repo.Diff.Compare<Patch>(null, untrackedTree);
                var added = untracked.FirstOrDefault(e => e.Path == filePath);
                if (added is not null)
                {
                    return ParsePatch(added.Patch);
                }
            }

            return Array.Empty<DiffHunk>();
        }, cancellationToken);

    /// <summary>
    /// The tree of untracked files of a stash commit created with <c>--include-untracked</c>: its
    /// third parent, a root commit git names "untracked files on ...". Null for any other commit.
    /// </summary>
    private static Tree? StashUntrackedTree(Commit commit)
    {
        var parents = commit.Parents.ToList();
        return parents.Count == 3 && !parents[2].Parents.Any() && parents[2].Message.StartsWith("untracked files on ", StringComparison.Ordinal)
            ? parents[2].Tree
            : null;
    }

    private static readonly CompareOptions RenameDetection = new() { Similarity = SimilarityOptions.Renames };

    public Task<IReadOnlyList<GitCommit>> GetCommitsSinceAsync(string path, string ontoSha, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<GitCommit>>(() =>
        {
            using var repo = new Repository(path);
            var filter = new CommitFilter
            {
                IncludeReachableFrom = repo.Head,
                ExcludeReachableFrom = ontoSha,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse,
            };
            return repo.Commits.QueryBy(filter)
                .Select(c => new GitCommit(c.Sha, c.Message.TrimEnd(), c.Author.Name, c.Author.When, c.Parents.Select(p => p.Sha).ToList()))
                .ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<GitSubmodule>> GetSubmodulesAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<GitSubmodule>>(() =>
        {
            using var repo = new Repository(path);
            return repo.Submodules
                .Select(s =>
                {
                    var status = s.RetrieveStatus();
                    var initialized = !status.HasFlag(SubmoduleStatus.WorkDirUninitialized);
                    // A submodule added but not committed yet has no recorded commit to differ from.
                    var modified = status.HasFlag(SubmoduleStatus.WorkDirModified)
                        || (initialized && s.HeadCommitId is not null && s.WorkDirCommitId != s.HeadCommitId);
                    return new GitSubmodule(s.Name, s.Path, s.Url, initialized, modified);
                })
                .OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<GitWorktree>> GetWorktreesAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<GitWorktree>>(() =>
        {
            using var repo = new Repository(path);
            var worktrees = new List<GitWorktree>();

            // From a linked worktree, the main one is the common git directory's parent.
            var commonDir = repo.Info.Path.TrimEnd('\\', '/');
            var mainPath = repo.Info.IsBare ? null : Directory.GetParent(ResolveCommonDir(commonDir))?.FullName;
            if (mainPath is not null)
            {
                using var main = new Repository(mainPath);
                worktrees.Add(new GitWorktree(main.Info.WorkingDirectory.TrimEnd('\\', '/'), main.Info.IsHeadDetached ? null : main.Head.FriendlyName, IsMain: true, IsLocked: false));

                foreach (var worktree in main.Worktrees)
                {
                    using var linked = worktree.WorktreeRepository;
                    worktrees.Add(new GitWorktree(
                        linked.Info.WorkingDirectory.TrimEnd('\\', '/'),
                        linked.Info.IsHeadDetached ? null : linked.Head.FriendlyName,
                        IsMain: false,
                        worktree.IsLocked));
                }
            }

            return worktrees;
        }, cancellationToken);

    // A linked worktree's .git dir is <common>/worktrees/<name>; its "commondir" file points back.
    private static string ResolveCommonDir(string gitDir)
    {
        var commonDirFile = Path.Combine(gitDir, "commondir");
        return File.Exists(commonDirFile)
            ? Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonDirFile).Trim())).TrimEnd('\\', '/')
            : gitDir;
    }

    public Task<IReadOnlyList<GitRemote>> GetRemotesAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<GitRemote>>(() =>
        {
            using var repo = new Repository(path);
            return repo.Network.Remotes
                .Select(r => new GitRemote(r.Name, r.Url, r.PushUrl ?? r.Url))
                .OrderBy(r => r.Name == "origin" ? 0 : 1)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<CommitFileChange>> GetComparisonAsync(string path, string fromSha, string toSha, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<CommitFileChange>>(() =>
        {
            using var repo = new Repository(path);
            var from = repo.Lookup<Commit>(fromSha) ?? throw new InvalidOperationException($"Unknown commit '{fromSha}'.");
            var to = repo.Lookup<Commit>(toSha) ?? throw new InvalidOperationException($"Unknown commit '{toSha}'.");
            using var patch = repo.Diff.Compare<Patch>(from.Tree, to.Tree, RenameDetection);
            return patch
                .Select(entry => new CommitFileChange(
                    entry.Path,
                    entry.Status == ChangeKind.Renamed ? entry.OldPath : null,
                    MapChangeKind(entry.Status),
                    entry.LinesAdded,
                    entry.LinesDeleted))
                .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<DiffHunk>> GetComparisonFileDiffAsync(string path, string fromSha, string toSha, string filePath, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<DiffHunk>>(() =>
        {
            using var repo = new Repository(path);
            var from = repo.Lookup<Commit>(fromSha) ?? throw new InvalidOperationException($"Unknown commit '{fromSha}'.");
            var to = repo.Lookup<Commit>(toSha) ?? throw new InvalidOperationException($"Unknown commit '{toSha}'.");
            using var patch = repo.Diff.Compare<Patch>(from.Tree, to.Tree, RenameDetection);
            var entry = patch.FirstOrDefault(e => e.Path == filePath || e.OldPath == filePath);
            return entry is null ? Array.Empty<DiffHunk>() : ParsePatch(entry.Patch);
        }, cancellationToken);

    public Task<IReadOnlyList<FileHistoryEntry>> GetFileHistoryAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<FileHistoryEntry>>(() =>
        {
            using var repo = new Repository(path);
            if (repo.Head.Tip is null)
            {
                return Array.Empty<FileHistoryEntry>();
            }

            // QueryBy(path) walks from HEAD and follows the file through renames.
            var filter = new CommitFilter { SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
            return repo.Commits.QueryBy(filePath.Replace('\\', '/'), filter)
                .Select(entry => new FileHistoryEntry(
                    new GitCommit(entry.Commit.Sha, entry.Commit.MessageShort, entry.Commit.Author.Name, entry.Commit.Author.When, entry.Commit.Parents.Select(p => p.Sha).ToList()),
                    entry.Path))
                .ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<BlameLine>> GetBlameAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<BlameLine>>(() =>
        {
            using var repo = new Repository(path);
            var gitPath = filePath.Replace('\\', '/');
            if (repo.Head.Tip?[gitPath]?.Target is not Blob blob)
            {
                throw new InvalidOperationException($"'{filePath}' is not committed on the current branch, so it has no blame.");
            }

            var lines = blob.GetContentText().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            if (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            var result = new List<BlameLine>(lines.Count);
            foreach (var hunk in repo.Blame(gitPath))
            {
                var commit = hunk.FinalCommit;
                for (var i = 0; i < hunk.LineCount; i++)
                {
                    var index = hunk.FinalStartLineNumber + i;
                    if (index >= lines.Count)
                    {
                        break;
                    }

                    result.Add(new BlameLine(index + 1, lines[index], commit.Sha, hunk.FinalSignature.Name, hunk.FinalSignature.When, commit.MessageShort, StartsHunk: i == 0));
                }
            }

            result.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));
            return result;
        }, cancellationToken);

    private static FileChangeType MapChangeKind(ChangeKind kind) => kind switch
    {
        ChangeKind.Added or ChangeKind.Copied => FileChangeType.Added,
        ChangeKind.Deleted => FileChangeType.Deleted,
        ChangeKind.Renamed => FileChangeType.Renamed,
        ChangeKind.Conflicted => FileChangeType.Conflicted,
        _ => FileChangeType.Modified,
    };

    private static (string Summary, string Body) SplitMessage(string message)
    {
        var normalized = message.Replace("\r\n", "\n").TrimEnd();
        var newline = normalized.IndexOf('\n');
        return newline < 0 ? (normalized, string.Empty) : (normalized[..newline].Trim(), normalized[(newline + 1)..].Trim());
    }

    public Task<IReadOnlyList<GitTag>> GetTagsAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<GitTag>>(() =>
        {
            using var repo = new Repository(path);
            IReadOnlyList<GitTag> tags = repo.Tags
                .Select(t => new GitTag(t.FriendlyName, t.PeeledTarget.Sha))
                .ToList();
            return tags;
        }, cancellationToken);

    private static bool IsStagedState(FileStatus status) =>
        status.HasFlag(FileStatus.NewInIndex) || status.HasFlag(FileStatus.ModifiedInIndex) || status.HasFlag(FileStatus.DeletedFromIndex) || status.HasFlag(FileStatus.RenamedInIndex);

    private static bool IsUnstagedState(FileStatus status) =>
        status.HasFlag(FileStatus.NewInWorkdir) || status.HasFlag(FileStatus.ModifiedInWorkdir) || status.HasFlag(FileStatus.DeletedFromWorkdir) || status.HasFlag(FileStatus.RenamedInWorkdir) || status.HasFlag(FileStatus.TypeChangeInWorkdir);

    private static FileChangeType MapIndexChangeType(FileStatus status) =>
        status.HasFlag(FileStatus.NewInIndex) ? FileChangeType.Added
        : status.HasFlag(FileStatus.DeletedFromIndex) ? FileChangeType.Deleted
        : status.HasFlag(FileStatus.RenamedInIndex) ? FileChangeType.Renamed
        : FileChangeType.Modified;

    private static FileChangeType MapWorkdirChangeType(FileStatus status) =>
        status.HasFlag(FileStatus.NewInWorkdir) ? FileChangeType.Added
        : status.HasFlag(FileStatus.DeletedFromWorkdir) ? FileChangeType.Deleted
        : status.HasFlag(FileStatus.RenamedInWorkdir) ? FileChangeType.Renamed
        : FileChangeType.Modified;

    private static List<DiffHunk> ParsePatch(string patchText)
    {
        var hunks = new List<DiffHunk>();
        List<DiffLine>? currentLines = null;
        var oldStart = 0;
        var newStart = 0;

        foreach (var line in patchText.Split('\n'))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (currentLines is not null)
                {
                    hunks.Add(new DiffHunk(oldStart, newStart, currentLines));
                }

                currentLines = new List<DiffLine>();
                var header = line.Split("@@")[1].Trim();
                var parts = header.Split(' ');
                oldStart = ParseHunkStart(parts[0]);
                newStart = ParseHunkStart(parts[1]);
            }
            else if (currentLines is { Count: > 0 } && line.StartsWith('\\'))
            {
                // "\ No newline at end of file" applies to the line just before it; patches built
                // from this hunk must repeat it or git apply will not match the file.
                currentLines[^1] = currentLines[^1] with { NoNewlineAtEnd = true };
            }
            else if (currentLines is not null && line.Length > 0 && !line.StartsWith('\\'))
            {
                var kind = line[0] switch
                {
                    '+' => DiffLineKind.Added,
                    '-' => DiffLineKind.Removed,
                    _ => DiffLineKind.Context,
                };
                currentLines.Add(new DiffLine(kind, line.Length > 1 ? line[1..] : string.Empty));
            }
        }

        if (currentLines is not null)
        {
            hunks.Add(new DiffHunk(oldStart, newStart, currentLines));
        }

        return hunks;
    }

    private static int ParseHunkStart(string rangeText) =>
        int.Parse(rangeText.TrimStart('-', '+').Split(',')[0]);
}
