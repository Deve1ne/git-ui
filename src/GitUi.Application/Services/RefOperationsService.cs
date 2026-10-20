using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>
/// Runs the actions planned by <see cref="RefMenuPlanner"/>. Checkout, branch creation and branch
/// deletion go through <see cref="UndoService"/> so they can be undone from the toolbar.
/// </summary>
public sealed class RefOperationsService
{
    private readonly IGitRepositoryReader _reader;
    private readonly IGitRepositoryWriter _writer;
    private readonly UndoService _undoService;
    private readonly RemoteService _remoteService;

    public RefOperationsService(IGitRepositoryReader reader, IGitRepositoryWriter writer, UndoService undoService, RemoteService remoteService)
    {
        _reader = reader;
        _writer = writer;
        _undoService = undoService;
        _remoteService = remoteService;
    }

    public async Task<HeadInfo> GetHeadAsync(string path, CancellationToken cancellationToken = default)
    {
        var repository = await _reader.GetRepositoryAsync(path, cancellationToken);

        // LibGit2Sharp names a detached HEAD "(no branch)"; the menus need "no branch" as null.
        var branch = repository.CurrentBranch.StartsWith('(') ? null : repository.CurrentBranch;
        return new HeadInfo(branch, repository.HeadSha ?? string.Empty);
    }

    public Task<GitAncestry> GetAncestryAsync(string path, string first, string second, CancellationToken cancellationToken = default)
        => _reader.GetAncestryAsync(path, first, second, cancellationToken);

    /// <param name="input">The user's answer for actions with an <see cref="RefAction.InputPrompt"/>.</param>
    public async Task ExecuteAsync(string path, RefAction action, string? input = null, GitPullMode pullMode = GitPullMode.Merge, CancellationToken cancellationToken = default)
    {
        var head = await GetHeadAsync(path, cancellationToken);

        switch (action.Kind)
        {
            case RefActionKind.Pull:
                await _remoteService.PullAsync(path, pullMode, cancellationToken);
                break;

            case RefActionKind.FastForwardFromUpstream:
                await _writer.FastForwardBranchAsync(path, Required(action.Target), Required(action.Source), cancellationToken);
                break;

            case RefActionKind.Push:
                await _writer.PushBranchAsync(
                    path,
                    Required(action.Remote),
                    Required(action.Target),
                    Required(action.RemoteBranch),
                    setUpstream: await HasNoUpstreamAsync(path, Required(action.Target), cancellationToken),
                    cancellationToken);
                break;

            case RefActionKind.SetUpstream:
                await _writer.SetUpstreamAsync(path, Required(action.Target), RequiredInput(input), cancellationToken);
                break;

            case RefActionKind.FastForward:
                // HEAD moves with a fast-forward-only merge (updates the working tree); any other
                // branch is moved without checking it out.
                if (action.Target == head.BranchName)
                {
                    await _writer.MergeAsync(path, Required(action.Source), GitMergeMode.FastForwardOnly, cancellationToken);
                }
                else
                {
                    await _writer.FastForwardBranchAsync(path, Required(action.Target), Required(action.Source), cancellationToken);
                }

                break;

            case RefActionKind.Merge:
                // Merging needs the receiving branch checked out, as GitKraken does when dropping onto another branch.
                if (action.Target != head.BranchName)
                {
                    await _undoService.CheckoutAsync(path, Required(action.Target), cancellationToken);
                }

                await _writer.MergeAsync(path, Required(action.Source), GitMergeMode.NoFastForward, cancellationToken);
                break;

            case RefActionKind.Rebase:
                await _writer.RebaseAsync(path, Required(action.Target), action.Source == head.BranchName ? null : action.Source, cancellationToken);
                break;

            case RefActionKind.Checkout:
                await _undoService.CheckoutAsync(path, Required(action.Target), cancellationToken);
                break;

            case RefActionKind.CheckoutRemote:
                await _writer.CheckoutRemoteBranchAsync(path, Required(action.Target), cancellationToken);
                break;

            case RefActionKind.CheckoutCommit:
                await _writer.CheckoutCommitAsync(path, Required(action.Sha), cancellationToken);
                break;

            case RefActionKind.CreateBranchHere:
                await _undoService.CreateBranchAsync(path, RequiredInput(input), Required(action.Sha), cancellationToken);
                break;

            case RefActionKind.CherryPick:
                await _writer.CherryPickAsync(path, Required(action.Sha), cancellationToken);
                break;

            case RefActionKind.Reset:
                await _writer.ResetAsync(path, Required(action.Sha), action.ResetMode, cancellationToken);
                break;

            case RefActionKind.Revert:
                await _writer.RevertAsync(path, Required(action.Sha), cancellationToken);
                break;

            case RefActionKind.Rename:
                await _writer.RenameBranchAsync(path, Required(action.Target), RequiredInput(input), cancellationToken);
                break;

            case RefActionKind.Delete:
                await _undoService.DeleteBranchAsync(path, Required(action.Target), force: true, cancellationToken);
                break;

            case RefActionKind.DeleteLocalAndRemote:
                await _undoService.DeleteBranchAsync(path, Required(action.Target), force: true, cancellationToken);
                await _writer.DeleteRemoteBranchAsync(path, Required(action.Remote), Required(action.RemoteBranch), cancellationToken);
                break;

            case RefActionKind.DeleteRemote:
                await _writer.DeleteRemoteBranchAsync(path, Required(action.Remote), Required(action.RemoteBranch), cancellationToken);
                break;

            case RefActionKind.CreateTagHere:
                await _writer.CreateTagAsync(path, RequiredInput(input), Required(action.Sha), message: null, cancellationToken);
                break;

            case RefActionKind.DeleteTag:
                await _writer.DeleteTagAsync(path, Required(action.Target), cancellationToken);
                break;

            case RefActionKind.PushTag:
                await _writer.PushTagAsync(path, Required(action.Remote), Required(action.Target), cancellationToken);
                break;

            default:
                // Copy actions and submenus are handled by the view; nothing to run here.
                break;
        }
    }

    public Task<GitOperationState> GetOperationStateAsync(string path, CancellationToken cancellationToken = default)
        => _reader.GetOperationStateAsync(path, cancellationToken);

    public Task ContinueOperationAsync(string path, GitOperationState operation, CancellationToken cancellationToken = default)
        => _writer.ContinueOperationAsync(path, operation, cancellationToken);

    public Task AbortOperationAsync(string path, GitOperationState operation, CancellationToken cancellationToken = default)
        => _writer.AbortOperationAsync(path, operation, cancellationToken);

    private async Task<bool> HasNoUpstreamAsync(string path, string branchName, CancellationToken cancellationToken)
    {
        var branches = await _reader.GetBranchesAsync(path, cancellationToken);
        return branches.FirstOrDefault(b => b.IsLocal && b.Name == branchName)?.TrackingBranch is null;
    }

    private static string Required(string? value) =>
        value ?? throw new InvalidOperationException("The action is missing the branch or commit it applies to.");

    private static string RequiredInput(string? input) =>
        string.IsNullOrWhiteSpace(input) ? throw new InvalidOperationException("A name is required.") : input.Trim();
}
