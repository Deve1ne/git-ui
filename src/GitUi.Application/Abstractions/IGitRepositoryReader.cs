using GitUi.Domain.Entities;
using GitUi.Domain.ValueObjects;

namespace GitUi.Application.Abstractions;

public interface IGitRepositoryReader
{
    bool IsValidRepository(string path);
    Task<GitRepository> GetRepositoryAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitCommit>> GetCommitsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The commits reachable from the refs <paramref name="filter"/> lets through (see <see cref="GraphRefFilter"/>).</summary>
    Task<IReadOnlyList<GitCommit>> GetCommitsAsync(string path, GraphRefFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileChange>> GetStatusAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiffHunk>> GetDiffAsync(string path, string filePath, bool staged, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitStash>> GetStashesAsync(string path, CancellationToken cancellationToken = default);
    Task<GitAncestry> GetAncestryAsync(string path, string firstRevision, string secondRevision, CancellationToken cancellationToken = default);
    Task<GitOperationState> GetOperationStateAsync(string path, CancellationToken cancellationToken = default);
    Task<ConflictStageContents> GetConflictStageContentsAsync(string path, string filePath, CancellationToken cancellationToken = default);

    Task<ConflictVersions> GetConflictVersionsAsync(string path, string filePath, CancellationToken cancellationToken = default);

    /// <summary>The working-directory content of a file (UTF-8), e.g. a conflicted file with its markers.</summary>
    Task<string> ReadWorkingFileAsync(string path, string filePath, CancellationToken cancellationToken = default);
    /// <summary>Merges <paramref name="ours"/> and <paramref name="theirs"/> in memory to tell whether a real merge would conflict.</summary>
    Task<MergePrediction> PredictMergeAsync(string path, string ours, string theirs, CancellationToken cancellationToken = default);
    Task<CommitDetails> GetCommitDetailsAsync(string path, string sha, CancellationToken cancellationToken = default);

    /// <summary>Diff of one file in a commit against its first parent (empty parent for a root commit).</summary>
    Task<IReadOnlyList<DiffHunk>> GetCommitFileDiffAsync(string path, string sha, string filePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitTag>> GetTagsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The commits HEAD has on top of <paramref name="ontoSha"/>, oldest first: what an interactive rebase replays.</summary>
    Task<IReadOnlyList<GitCommit>> GetCommitsSinceAsync(string path, string ontoSha, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitSubmodule>> GetSubmodulesAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The main working tree first, then the linked ones (git worktree).</summary>
    Task<IReadOnlyList<GitWorktree>> GetWorktreesAsync(string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitRemote>> GetRemotesAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Files that differ between two commits (renames detected), as a diff from <paramref name="fromSha"/> to <paramref name="toSha"/>.</summary>
    Task<IReadOnlyList<CommitFileChange>> GetComparisonAsync(string path, string fromSha, string toSha, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiffHunk>> GetComparisonFileDiffAsync(string path, string fromSha, string toSha, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Commits that changed <paramref name="filePath"/>, newest first, following renames.</summary>
    Task<IReadOnlyList<FileHistoryEntry>> GetFileHistoryAsync(string path, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Line-by-line authorship of <paramref name="filePath"/> as committed in HEAD.</summary>
    Task<IReadOnlyList<BlameLine>> GetBlameAsync(string path, string filePath, CancellationToken cancellationToken = default);
}
