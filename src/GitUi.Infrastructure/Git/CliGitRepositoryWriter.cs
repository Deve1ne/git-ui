using GitUi.Application.Abstractions;

namespace GitUi.Infrastructure.Git;

public sealed class CliGitRepositoryWriter : IGitRepositoryWriter
{
    private readonly GitCliRunner _runner;

    public CliGitRepositoryWriter(GitCliRunner runner) => _runner = runner;

    public Task CommitAsync(string path, string message, CancellationToken cancellationToken = default)
        => CommitAsync(path, message, GitCommitOptions.Default, cancellationToken);

    public Task CommitAsync(string path, string message, GitCommitOptions options, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "commit" };
        if (options.Amend)
        {
            arguments.Add("--amend");
        }

        if (options.SkipHooks)
        {
            arguments.Add("--no-verify");
        }

        arguments.Add("-m");
        arguments.Add(message);
        // Hooks (lint, tests) can take a while: give them more than the default 30 seconds.
        return _runner.RunAsync(path, arguments, TimeSpan.FromMinutes(10), cancellationToken);
    }

    public Task StageAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "add", "--", filePath }, cancellationToken);

    public Task UnstageAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "restore", "--staged", "--", filePath }, cancellationToken);

    public Task CreateBranchAsync(string path, string branchName, string? startPoint = null, CancellationToken cancellationToken = default)
        => _runner.RunAsync(
            path,
            string.IsNullOrEmpty(startPoint) ? new[] { "branch", "--", branchName } : new[] { "branch", "--", branchName, startPoint },
            cancellationToken);

    public Task DeleteBranchAsync(string path, string branchName, bool force, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "branch", force ? "-D" : "-d", "--", branchName }, cancellationToken);

    public Task CheckoutAsync(string path, string branchName, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "switch", "--", branchName }, cancellationToken);

    public Task PushAsync(string path, bool force, CancellationToken cancellationToken = default)
        // --force-with-lease: refuses to overwrite remote commits this clone has not fetched yet.
        => _runner.RunAsync(path, force ? new[] { "push", "--force-with-lease" } : new[] { "push" }, GitCliRunner.NetworkTimeout, cancellationToken);

    public Task PullAsync(string path, GitPullMode mode = GitPullMode.Merge, CancellationToken cancellationToken = default)
        // --autostash: uncommitted changes no longer block a pull; they are set aside and put back.
        => _runner.RunAsync(path, mode switch
        {
            GitPullMode.FastForwardOnly => new[] { "pull", "--ff-only", "--autostash" },
            GitPullMode.Rebase => new[] { "pull", "--rebase", "--autostash" },
            _ => new[] { "pull", "--no-rebase", "--autostash" },
        }, GitCliRunner.NetworkTimeout, cancellationToken);

    public Task FetchAsync(string path, bool prune, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, prune ? new[] { "fetch", "--prune" } : new[] { "fetch" }, GitCliRunner.NetworkTimeout, cancellationToken);

    public Task StashSaveAsync(string path, string? message, CancellationToken cancellationToken = default)
        // Like GitKraken, new files go into the stash too instead of staying behind in the working directory.
        => _runner.RunAsync(
            path,
            string.IsNullOrWhiteSpace(message)
                ? new[] { "stash", "push", "--include-untracked" }
                : new[] { "stash", "push", "--include-untracked", "-m", message },
            cancellationToken);

    public Task StashApplyAsync(string path, int stashIndex, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "stash", "apply", $"stash@{{{stashIndex}}}" }, cancellationToken);

    public Task StashPopAsync(string path, int stashIndex, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "stash", "pop", $"stash@{{{stashIndex}}}" }, cancellationToken);

    public Task StashDropAsync(string path, int stashIndex, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "stash", "drop", $"stash@{{{stashIndex}}}" }, cancellationToken);

    public Task ApplyPatchToIndexAsync(string path, string patch, bool reverse, CancellationToken cancellationToken = default)
        => ApplyPatchAsync(path, patch, reverse, toIndex: true, cancellationToken);

    public Task ApplyPatchToWorkingTreeAsync(string path, string patch, bool reverse, CancellationToken cancellationToken = default)
        => ApplyPatchAsync(path, patch, reverse, toIndex: false, cancellationToken);

    private async Task ApplyPatchAsync(string path, string patch, bool reverse, bool toIndex, CancellationToken cancellationToken)
    {
        // git apply reads the patch from a file; a temp file keeps the runner's argv-only contract.
        var patchFile = Path.Combine(Path.GetTempPath(), $"gitui-{Guid.NewGuid():N}.patch");
        await File.WriteAllTextAsync(patchFile, patch, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        try
        {
            var arguments = new List<string> { "apply", "--whitespace=nowarn" };
            if (toIndex)
            {
                arguments.Add("--cached");
            }

            if (reverse)
            {
                arguments.Add("--reverse");
            }

            arguments.Add(patchFile);
            await _runner.RunAsync(path, arguments, cancellationToken);
        }
        finally
        {
            File.Delete(patchFile);
        }
    }

    public async Task DiscardWorkingChangesAsync(string path, IReadOnlyCollection<string> trackedFiles, IReadOnlyCollection<string> untrackedFiles, CancellationToken cancellationToken = default)
    {
        if (trackedFiles.Count > 0)
        {
            // Without --source, restore takes the index: staged changes survive the discard.
            await _runner.RunAsync(path, new[] { "restore", "--worktree", "--" }.Concat(trackedFiles).ToList(), cancellationToken);
        }

        if (untrackedFiles.Count > 0)
        {
            await _runner.RunAsync(path, new[] { "clean", "-f", "--" }.Concat(untrackedFiles).ToList(), cancellationToken);
        }
    }

    public async Task<string?> DiscardAllAsync(string path, CancellationToken cancellationToken = default)
    {
        var before = await StashTipAsync(path, cancellationToken);
        await _runner.RunAsync(path, new[] { "stash", "push", "--include-untracked", "-m", "Git UI: discard all changes" }, cancellationToken);
        var after = await StashTipAsync(path, cancellationToken);
        if (after.Length == 0 || after == before)
        {
            return null;
        }

        // The stash commit stays reachable by SHA until gc, which is all undo needs; dropping the
        // entry keeps the user's stash list as it was.
        await _runner.RunAsync(path, new[] { "stash", "drop", "stash@{0}" }, cancellationToken);
        return after;
    }

    public Task RestoreDiscardedAsync(string path, string stashSha, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "stash", "apply", "--index", stashSha }, cancellationToken);

    private Task<string> StashTipAsync(string path, CancellationToken cancellationToken)
        => _runner.RunAsync(path, new[] { "for-each-ref", "--format=%(objectname)", "refs/stash" }, cancellationToken);

    public Task MergeAsync(string path, string fromRevision, GitMergeMode mode, CancellationToken cancellationToken = default)
        => _runner.RunAsync(
            path,
            mode == GitMergeMode.FastForwardOnly
                ? new[] { "merge", "--ff-only", "--autostash", fromRevision }
                : new[] { "merge", "--no-ff", "--no-edit", "--autostash", fromRevision },
            cancellationToken);

    // `git fetch . src:dst` updates a local branch without checking it out and refuses non-fast-forwards.
    public Task FastForwardBranchAsync(string path, string branchName, string toRevision, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "fetch", ".", $"{toRevision}:refs/heads/{branchName}" }, cancellationToken);

    public Task RebaseAsync(string path, string ontoRevision, string? branchName, CancellationToken cancellationToken = default)
        => _runner.RunAsync(
            path,
            branchName is null ? new[] { "rebase", "--autostash", ontoRevision } : new[] { "rebase", "--autostash", ontoRevision, branchName },
            cancellationToken);

    public async Task InteractiveRebaseAsync(string path, string ontoSha, string todo, CancellationToken cancellationToken = default)
    {
        // git hands the todo file to sequence.editor through its shell: "cp <ours>" overwrites it with
        // the prepared list. core.editor=true accepts squash messages as git combines them.
        var todoFile = Path.Combine(Path.GetTempPath(), $"gitui-rebase-todo-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(todoFile, todo, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        try
        {
            await _runner.RunAsync(
                path,
                // --autostash: uncommitted changes are set aside and restored, like GitKraken does.
                new[] { "-c", $"sequence.editor=cp '{todoFile.Replace('\\', '/')}'", "-c", "core.editor=true", "rebase", "-i", "--autostash", ontoSha },
                TimeSpan.FromMinutes(10),
                cancellationToken);
        }
        finally
        {
            File.Delete(todoFile);
        }
    }

    public Task CherryPickAsync(string path, string sha, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "cherry-pick", sha }, cancellationToken);

    public Task RevertAsync(string path, string sha, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "revert", "--no-edit", sha }, cancellationToken);

    public Task RenameBranchAsync(string path, string oldName, string newName, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "branch", "-m", oldName, newName }, cancellationToken);

    public Task CheckoutCommitAsync(string path, string sha, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "switch", "--detach", sha }, cancellationToken);

    public Task CheckoutRemoteBranchAsync(string path, string remoteBranch, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "switch", "--track", remoteBranch }, cancellationToken);

    public Task SetUpstreamAsync(string path, string branchName, string upstream, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "branch", $"--set-upstream-to={upstream}", branchName }, cancellationToken);

    public Task PushBranchAsync(string path, string remote, string localBranch, string remoteBranch, bool setUpstream, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "push" };
        if (setUpstream)
        {
            arguments.Add("--set-upstream");
        }

        arguments.Add(remote);
        arguments.Add($"refs/heads/{localBranch}:refs/heads/{remoteBranch}");
        return _runner.RunAsync(path, arguments, GitCliRunner.NetworkTimeout, cancellationToken);
    }

    public Task DeleteRemoteBranchAsync(string path, string remote, string branchName, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "push", remote, "--delete", branchName }, GitCliRunner.NetworkTimeout, cancellationToken);

    public Task CreateTagAsync(string path, string tagName, string sha, string? message, CancellationToken cancellationToken = default)
        => _runner.RunAsync(
            path,
            string.IsNullOrWhiteSpace(message) ? new[] { "tag", tagName, sha } : new[] { "tag", "-a", tagName, "-m", message, sha },
            cancellationToken);

    public Task DeleteTagAsync(string path, string tagName, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "tag", "-d", tagName }, cancellationToken);

    public Task PushTagAsync(string path, string remote, string tagName, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "push", remote, $"refs/tags/{tagName}" }, GitCliRunner.NetworkTimeout, cancellationToken);

    // core.editor=true accepts the prepared message instead of opening an editor nobody can see.
    public Task ContinueOperationAsync(string path, GitOperationState operation, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "-c", "core.editor=true", OperationCommand(operation), "--continue" }, cancellationToken);

    public Task AbortOperationAsync(string path, GitOperationState operation, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { OperationCommand(operation), "--abort" }, cancellationToken);

    private static string OperationCommand(GitOperationState operation) => operation switch
    {
        GitOperationState.Merge => "merge",
        GitOperationState.Rebase => "rebase",
        GitOperationState.CherryPick => "cherry-pick",
        GitOperationState.Revert => "revert",
        _ => throw new InvalidOperationException("No merge, rebase, cherry-pick or revert is in progress."),
    };

    public Task CloneAsync(string url, string destinationPath, CloneOptions options, CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(destinationPath))
            ?? throw new InvalidOperationException($"'{destinationPath}' has no parent directory to clone into.");
        Directory.CreateDirectory(parent);

        var arguments = new List<string> { "clone" };
        if (!string.IsNullOrWhiteSpace(options.Branch))
        {
            arguments.Add("--branch");
            arguments.Add(options.Branch);
        }

        if (options.Depth is > 0)
        {
            arguments.Add("--depth");
            arguments.Add(options.Depth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (options.RecurseSubmodules)
        {
            arguments.Add("--recurse-submodules");
        }

        // "--" keeps a URL starting with "-" from being read as an option.
        arguments.Add("--");
        arguments.Add(url);
        arguments.Add(Path.GetFullPath(destinationPath));
        return _runner.RunAsync(parent, arguments, GitCliRunner.NetworkTimeout, cancellationToken);
    }

    public Task InitAsync(string path, string defaultBranch, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(path);
        return _runner.RunAsync(path, new[] { "init", "--initial-branch", defaultBranch }, cancellationToken);
    }

    public Task WriteWorkingFileBytesAsync(string path, string filePath, byte[] content, CancellationToken cancellationToken = default)
        => File.WriteAllBytesAsync(Path.Combine(path, filePath), content, cancellationToken);

    public Task WriteWorkingFileAsync(string path, string filePath, string content, CancellationToken cancellationToken = default)
        => File.WriteAllTextAsync(Path.Combine(path, filePath), content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

    public async Task TakeConflictSideAsync(string path, string filePath, ConflictSide side, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(path, new[] { "checkout", side == ConflictSide.Ours ? "--ours" : "--theirs", "--", filePath }, cancellationToken);
        await MarkResolvedAsync(path, filePath, cancellationToken);
    }

    // "add -A" stages a deletion too, so a conflict resolved by deleting the file is also marked resolved.
    public Task MarkResolvedAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "add", "-A", "--", filePath }, cancellationToken);

    public Task MarkConflictedAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "checkout", "-m", "--", filePath }, cancellationToken);

    public Task RemoveFileAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "rm", "-f", "--", filePath }, cancellationToken);

    public Task ResetAsync(string path, string toSha, GitResetMode mode, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "reset", $"--{mode.ToString().ToLowerInvariant()}", toSha }, cancellationToken);

    // Tools stay open as long as the user works in them.
    private static readonly TimeSpan ExternalToolTimeout = TimeSpan.FromHours(8);

    public async Task OpenDiffToolAsync(string path, string filePath, bool staged, string? commitSha, CancellationToken cancellationToken = default, string? compareFromSha = null)
    {
        // Without a configured tool git falls back to a terminal tool (vimdiff) nobody can see.
        if (await GetConfiguredToolAsync(path, "diff.tool") is null && await GetConfiguredToolAsync(path, "merge.tool") is null)
        {
            throw new InvalidOperationException("No external diff tool is configured. Set one with, e.g., git config --global diff.tool vscode (and difftool.vscode.cmd).");
        }

        var arguments = new List<string> { "difftool", "--no-prompt" };
        if (commitSha is not null && compareFromSha is not null)
        {
            arguments.Add(compareFromSha);
            arguments.Add(commitSha);
        }
        else if (commitSha is not null)
        {
            arguments.Add($"{commitSha}^!");
        }
        else if (staged)
        {
            arguments.Add("--cached");
        }

        arguments.Add("--");
        arguments.Add(filePath);
        await _runner.RunAsync(path, arguments, ExternalToolTimeout, cancellationToken);
    }

    public async Task OpenMergeToolAsync(string path, string filePath, CancellationToken cancellationToken = default)
    {
        if (await GetConfiguredToolAsync(path, "merge.tool") is null)
        {
            throw new InvalidOperationException("No external merge tool is configured. Set one with, e.g., git config --global merge.tool vscode (and mergetool.vscode.cmd).");
        }

        await _runner.RunAsync(path, new[] { "-c", "mergetool.keepBackup=false", "mergetool", "--no-prompt", "--", filePath }, ExternalToolTimeout, cancellationToken);
    }

    private async Task<string?> GetConfiguredToolAsync(string path, string key)
    {
        try
        {
            var value = await _runner.RunAsync(path, new[] { "config", "--get", key });
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Domain.Exceptions.GitOperationException)
        {
            return null;
        }
    }

    public Task RestoreFileFromCommitAsync(string path, string sha, string filePath, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "restore", $"--source={sha}", "--worktree", "--", filePath }, cancellationToken);

    public async Task<string> CreatePatchAsync(string path, string sha, CancellationToken cancellationToken = default)
        // The runner trims its output; a patch must end with a newline for git apply.
        => await _runner.RunAsync(path, new[] { "format-patch", "-1", "--stdout", sha }, cancellationToken) + "\n";

    public Task ApplyPatchFileAsync(string path, string patchFile, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "apply", "--3way", "--whitespace=nowarn", Path.GetFullPath(patchFile) }, cancellationToken);

    public async Task<bool> AddToGitignoreAsync(string path, string pattern, CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(path, ".gitignore");
        var existing = File.Exists(file) ? await File.ReadAllTextAsync(file, cancellationToken) : string.Empty;
        if (existing.Split('\n').Any(line => line.TrimEnd('\r').Trim() == pattern.Trim()))
        {
            return false;
        }

        // Keep the file's own line endings and never glue the pattern onto an unterminated last line.
        var newline = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var prefix = existing.Length > 0 && !existing.EndsWith('\n') ? newline : string.Empty;
        await File.AppendAllTextAsync(file, prefix + pattern.Trim() + newline, new System.Text.UTF8Encoding(false), cancellationToken);
        return true;
    }

    public Task AddRemoteAsync(string path, string name, string url, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "remote", "add", "--", name, url }, cancellationToken);

    public Task RemoveRemoteAsync(string path, string name, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "remote", "remove", name }, cancellationToken);

    public Task RenameRemoteAsync(string path, string oldName, string newName, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "remote", "rename", oldName, newName }, cancellationToken);

    public Task SetRemoteUrlAsync(string path, string name, string url, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "remote", "set-url", "--", name, url }, cancellationToken);

    public Task FetchRemoteAsync(string path, string remote, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, new[] { "fetch", "--prune", remote }, GitCliRunner.NetworkTimeout, cancellationToken);

    public Task UpdateSubmodulesAsync(string path, string? submodulePath, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "submodule", "update", "--init", "--recursive" };
        if (submodulePath is not null)
        {
            arguments.Add("--");
            arguments.Add(submodulePath);
        }

        return _runner.RunAsync(path, arguments, GitCliRunner.NetworkTimeout, cancellationToken);
    }

    public Task AddWorktreeAsync(string path, string worktreePath, string commitish, string? newBranch, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "worktree", "add" };
        if (!string.IsNullOrWhiteSpace(newBranch))
        {
            arguments.Add("-b");
            arguments.Add(newBranch);
        }

        arguments.Add(Path.GetFullPath(worktreePath));
        arguments.Add(commitish);
        return _runner.RunAsync(path, arguments, TimeSpan.FromMinutes(10), cancellationToken);
    }

    public Task RemoveWorktreeAsync(string path, string worktreePath, bool force, CancellationToken cancellationToken = default)
        => _runner.RunAsync(path, force ? new[] { "worktree", "remove", "--force", worktreePath } : new[] { "worktree", "remove", worktreePath }, cancellationToken);

    public async Task<string?> GetConfigValueAsync(string? repositoryPath, string key, bool global, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _runner.RunAsync(ConfigDirectory(repositoryPath, global), new[] { "config", global ? "--global" : "--local", "--get", key }, cancellationToken);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Domain.Exceptions.GitOperationException)
        {
            // git config --get exits with 1 when the key is not set.
            return null;
        }
    }

    public async Task SetConfigValueAsync(string? repositoryPath, string key, string? value, bool global, CancellationToken cancellationToken = default)
    {
        var scope = global ? "--global" : "--local";
        var directory = ConfigDirectory(repositoryPath, global);
        if (!string.IsNullOrWhiteSpace(value))
        {
            await _runner.RunAsync(directory, new[] { "config", scope, key, value.Trim() }, cancellationToken);
            return;
        }

        if (await GetConfigValueAsync(repositoryPath, key, global, cancellationToken) is not null)
        {
            await _runner.RunAsync(directory, new[] { "config", scope, "--unset", key }, cancellationToken);
        }
    }

    // Global config does not need a repository: run from the user's home directory.
    private static string ConfigDirectory(string? repositoryPath, bool global) =>
        global
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : repositoryPath ?? throw new InvalidOperationException("A repository is needed for its own config.");
}
