namespace GitUi.Application.Abstractions;

public interface IGitRepositoryWriter
{
    Task CommitAsync(string path, string message, CancellationToken cancellationToken = default);
    Task CommitAsync(string path, string message, GitCommitOptions options, CancellationToken cancellationToken = default);
    Task StageAsync(string path, string filePath, CancellationToken cancellationToken = default);
    Task UnstageAsync(string path, string filePath, CancellationToken cancellationToken = default);
    Task CreateBranchAsync(string path, string branchName, string? startPoint = null, CancellationToken cancellationToken = default);
    Task DeleteBranchAsync(string path, string branchName, bool force, CancellationToken cancellationToken = default);
    Task CheckoutAsync(string path, string branchName, CancellationToken cancellationToken = default);
    Task PushAsync(string path, bool force, CancellationToken cancellationToken = default);
    Task PullAsync(string path, GitPullMode mode = GitPullMode.Merge, CancellationToken cancellationToken = default);
    Task FetchAsync(string path, bool prune, CancellationToken cancellationToken = default);
    Task StashSaveAsync(string path, string? message, CancellationToken cancellationToken = default);
    Task StashApplyAsync(string path, int stashIndex, CancellationToken cancellationToken = default);
    Task StashPopAsync(string path, int stashIndex, CancellationToken cancellationToken = default);
    Task StashDropAsync(string path, int stashIndex, CancellationToken cancellationToken = default);
    /// <summary>Applies a unified diff to the index only (partial staging); <paramref name="reverse"/> unstages it.</summary>
    Task ApplyPatchToIndexAsync(string path, string patch, bool reverse, CancellationToken cancellationToken = default);
    Task MergeAsync(string path, string fromRevision, GitMergeMode mode, CancellationToken cancellationToken = default);

    /// <summary>Applies a unified diff to the working tree only; <paramref name="reverse"/> discards it.</summary>
    Task ApplyPatchToWorkingTreeAsync(string path, string patch, bool reverse, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws away unstaged changes: tracked files go back to their staged (or committed) content,
    /// untracked files are deleted. Staged changes are kept.
    /// </summary>
    Task DiscardWorkingChangesAsync(string path, IReadOnlyCollection<string> trackedFiles, IReadOnlyCollection<string> untrackedFiles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stashes every change (staged, unstaged and untracked), then drops the stash entry so the
    /// working directory is clean. Returns the SHA of the dropped stash commit, which
    /// <see cref="RestoreDiscardedAsync"/> can apply again; null when there was nothing to discard.
    /// </summary>
    Task<string?> DiscardAllAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Re-applies a stash commit returned by <see cref="DiscardAllAsync"/>, restoring the index too.</summary>
    Task RestoreDiscardedAsync(string path, string stashSha, CancellationToken cancellationToken = default);

    /// <summary>Moves a branch that is not checked out forward to <paramref name="toRevision"/>; refuses anything but a fast-forward.</summary>
    Task FastForwardBranchAsync(string path, string branchName, string toRevision, CancellationToken cancellationToken = default);

    /// <summary>Rebases <paramref name="branchName"/> (or HEAD when null) onto <paramref name="ontoRevision"/>.</summary>
    Task RebaseAsync(string path, string ontoRevision, string? branchName, CancellationToken cancellationToken = default);

    /// <summary>Runs <c>git rebase -i</c> onto <paramref name="ontoSha"/> with a prepared todo list (no editor opens).</summary>
    Task InteractiveRebaseAsync(string path, string ontoSha, string todo, CancellationToken cancellationToken = default);
    Task CherryPickAsync(string path, string sha, CancellationToken cancellationToken = default);
    Task RevertAsync(string path, string sha, CancellationToken cancellationToken = default);
    Task RenameBranchAsync(string path, string oldName, string newName, CancellationToken cancellationToken = default);
    Task CheckoutCommitAsync(string path, string sha, CancellationToken cancellationToken = default);

    /// <summary>Creates a local branch tracking <paramref name="remoteBranch"/> (e.g. <c>origin/feature</c>) and checks it out.</summary>
    Task CheckoutRemoteBranchAsync(string path, string remoteBranch, CancellationToken cancellationToken = default);
    Task SetUpstreamAsync(string path, string branchName, string upstream, CancellationToken cancellationToken = default);
    Task PushBranchAsync(string path, string remote, string localBranch, string remoteBranch, bool setUpstream, CancellationToken cancellationToken = default);
    Task DeleteRemoteBranchAsync(string path, string remote, string branchName, CancellationToken cancellationToken = default);
    Task CreateTagAsync(string path, string tagName, string sha, string? message, CancellationToken cancellationToken = default);
    Task DeleteTagAsync(string path, string tagName, CancellationToken cancellationToken = default);
    Task PushTagAsync(string path, string remote, string tagName, CancellationToken cancellationToken = default);
    Task ContinueOperationAsync(string path, GitOperationState operation, CancellationToken cancellationToken = default);
    Task AbortOperationAsync(string path, GitOperationState operation, CancellationToken cancellationToken = default);
    /// <summary>Clones <paramref name="url"/> into <paramref name="destinationPath"/> (which must not exist or be empty).</summary>
    Task CloneAsync(string url, string destinationPath, CloneOptions options, CancellationToken cancellationToken = default);
    Task InitAsync(string path, string defaultBranch, CancellationToken cancellationToken = default);
    /// <summary>Writes exact bytes (encoding, BOM and line endings already decided by the caller).</summary>
    Task WriteWorkingFileBytesAsync(string path, string filePath, byte[] content, CancellationToken cancellationToken = default);
    Task WriteWorkingFileAsync(string path, string filePath, string content, CancellationToken cancellationToken = default);

    /// <summary>Replaces a conflicted file with one side's version and marks it resolved.</summary>
    Task TakeConflictSideAsync(string path, string filePath, ConflictSide side, CancellationToken cancellationToken = default);

    /// <summary>Stages a file as resolved (or removed when it no longer exists).</summary>
    Task MarkResolvedAsync(string path, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Recreates the conflict markers of a file that was marked resolved.</summary>
    Task MarkConflictedAsync(string path, string filePath, CancellationToken cancellationToken = default);
    Task RemoveFileAsync(string path, string filePath, CancellationToken cancellationToken = default);
    Task ResetAsync(string path, string toSha, GitResetMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the user's configured diff tool (<c>diff.tool</c>) on a working-directory file, its
    /// staged version, or — with <paramref name="commitSha"/> — that commit's change. Completes when
    /// the tool is closed.
    /// </summary>
    Task OpenDiffToolAsync(string path, string filePath, bool staged, string? commitSha, CancellationToken cancellationToken = default, string? compareFromSha = null);

    /// <summary>Opens the configured merge tool (<c>merge.tool</c>) on a conflicted file; git marks it resolved when the tool reports success.</summary>
    Task OpenMergeToolAsync(string path, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="filePath"/> as it is in <paramref name="sha"/> into the working directory (unstaged).</summary>
    Task RestoreFileFromCommitAsync(string path, string sha, string filePath, CancellationToken cancellationToken = default);

    /// <summary>The commit as a mailbox patch (<c>git format-patch -1 --stdout</c>), ready to save as a .patch file.</summary>
    Task<string> CreatePatchAsync(string path, string sha, CancellationToken cancellationToken = default);

    /// <summary>Applies a patch file to the working directory and index (three-way, so it can stop on conflicts).</summary>
    Task ApplyPatchFileAsync(string path, string patchFile, CancellationToken cancellationToken = default);

    /// <summary>Adds <paramref name="pattern"/> to the repository's root .gitignore (created if needed); false when it is already there.</summary>
    Task<bool> AddToGitignoreAsync(string path, string pattern, CancellationToken cancellationToken = default);

    Task AddRemoteAsync(string path, string name, string url, CancellationToken cancellationToken = default);
    Task RemoveRemoteAsync(string path, string name, CancellationToken cancellationToken = default);
    Task RenameRemoteAsync(string path, string oldName, string newName, CancellationToken cancellationToken = default);
    Task SetRemoteUrlAsync(string path, string name, string url, CancellationToken cancellationToken = default);
    Task FetchRemoteAsync(string path, string remote, CancellationToken cancellationToken = default);

    /// <summary><c>git submodule update --init --recursive</c>, for one submodule or all of them.</summary>
    Task UpdateSubmodulesAsync(string path, string? submodulePath, CancellationToken cancellationToken = default);

    /// <summary>Creates a linked working tree at <paramref name="worktreePath"/>; <paramref name="newBranch"/> creates that branch from <paramref name="commitish"/>.</summary>
    Task AddWorktreeAsync(string path, string worktreePath, string commitish, string? newBranch, CancellationToken cancellationToken = default);
    Task RemoveWorktreeAsync(string path, string worktreePath, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a git config value from the user's global config (<paramref name="global"/>) or the
    /// repository's own config; null when it is not set there.
    /// </summary>
    Task<string?> GetConfigValueAsync(string? repositoryPath, string key, bool global, CancellationToken cancellationToken = default);

    /// <summary>Writes a git config value; a null or blank value removes the key.</summary>
    Task SetConfigValueAsync(string? repositoryPath, string key, string? value, bool global, CancellationToken cancellationToken = default);
}
