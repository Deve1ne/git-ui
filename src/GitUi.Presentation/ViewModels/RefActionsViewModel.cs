using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// Branch / tag / commit context menus and ref drag-and-drop: builds the menus with
/// <see cref="RefMenuPlanner"/> from fresh repository state, then runs the chosen action.
/// The view supplies the dialogs and the clipboard through the Request* callbacks.
/// </summary>
public sealed class RefActionsViewModel
{
    private readonly IGitRepositoryReader _reader;
    private readonly RefOperationsService _operations;
    private readonly ConflictDetectionService _conflictDetection;
    private readonly RemoteActionsViewModel _remoteActions;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;

    private readonly IGitRepositoryWriter _writer;
    private readonly NotificationService _notifications;

    public RefActionsViewModel(
        IGitRepositoryReader reader,
        IGitRepositoryWriter writer,
        RefOperationsService operations,
        ConflictDetectionService conflictDetection,
        RemoteActionsViewModel remoteActions,
        RepositoryChangeNotifier changeNotifier,
        CommandErrorHandler errorHandler,
        NotificationService notifications)
    {
        _notifications = notifications;
        _reader = reader;
        _writer = writer;
        _operations = operations;
        _conflictDetection = conflictDetection;
        _remoteActions = remoteActions;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
    }

    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }
    public Func<string, string, string?, Task<string?>>? RequestInput { get; set; }
    public Func<string, Task>? RequestClipboard { get; set; }

    /// <summary>Asks where to save a file (suggested name, content) and writes it.</summary>
    public Func<string, string, Task>? RequestSaveFile { get; set; }

    /// <summary>Raised with the base commit and the branch name to open the interactive rebase editor.</summary>
    public event Action<string, string>? InteractiveRebaseRequested;

    /// <summary>Raised with a commit and the branch name to reword that commit.</summary>
    public event Action<string, string>? RewordRequested;

    /// <summary>Raised to create a pull request: the local branch, or null and the remote branch ("origin/feature").</summary>
    public event Action<string?, string?>? PullRequestRequested;

    /// <summary>Raised to mark a commit for bisect.</summary>
    public event Action<string, BisectMark>? BisectRequested;

    public void SetRepositoryPath(string? repositoryPath) => _repositoryPath = repositoryPath;

    /// <summary>Builds the target for a branch shown in the ref panel, with its upstream.</summary>
    public static RefTarget FromBranch(GitBranch branch) =>
        new(branch.IsRemote ? RefKind.RemoteBranch : RefKind.LocalBranch, branch.Name, branch.TipSha, branch.TrackingBranch);

    public static RefTarget FromTag(GitTag tag) => new(RefKind.Tag, tag.Name, tag.TargetSha);

    // Menus are built from fresh git state; a failure there reports an error instead of crashing the
    // pointer handler that asked for the menu.
    public async Task<IReadOnlyList<IReadOnlyList<RefAction>>> BuildContextMenuAsync(RefTarget target, bool inGraph)
    {
        IReadOnlyList<IReadOnlyList<RefAction>> menu = Array.Empty<IReadOnlyList<RefAction>>();
        await _errorHandler.RunGuardedAsync(async () => menu = await BuildContextMenuCoreAsync(target, inGraph), "build branch menu");
        return menu;
    }

    public async Task<IReadOnlyList<IReadOnlyList<RefAction>>> BuildDragDropMenuAsync(RefTarget dragged, RefTarget target)
    {
        IReadOnlyList<IReadOnlyList<RefAction>> menu = Array.Empty<IReadOnlyList<RefAction>>();
        await _errorHandler.RunGuardedAsync(async () => menu = await BuildDragDropMenuCoreAsync(dragged, target), "build drag-and-drop menu");
        return menu;
    }

    private async Task<IReadOnlyList<IReadOnlyList<RefAction>>> BuildContextMenuCoreAsync(RefTarget target, bool inGraph)
    {
        if (_repositoryPath is null)
        {
            return Array.Empty<IReadOnlyList<RefAction>>();
        }

        var head = await _operations.GetHeadAsync(_repositoryPath);
        var branches = await _reader.GetBranchesAsync(_repositoryPath);
        GitAncestry? ancestry = head.BranchName is not null && target.Kind != RefKind.Commit
            ? await _operations.GetAncestryAsync(_repositoryPath, head.Sha, target.Sha)
            : null;

        var menu = RefMenuPlanner.BuildContextMenu(target, head, ancestry, RemoteNames(branches), LocalNames(branches), inGraph);
        return await AnnotateConflictsAsync(menu);
    }

    private async Task<IReadOnlyList<IReadOnlyList<RefAction>>> BuildDragDropMenuCoreAsync(RefTarget dragged, RefTarget target)
    {
        if (_repositoryPath is null)
        {
            return Array.Empty<IReadOnlyList<RefAction>>();
        }

        var head = await _operations.GetHeadAsync(_repositoryPath);
        var branches = await _reader.GetBranchesAsync(_repositoryPath);
        var ancestry = await _operations.GetAncestryAsync(_repositoryPath, dragged.Sha, target.Sha);
        var menu = RefMenuPlanner.BuildDragDropMenu(dragged, target, head, ancestry, RemoteNames(branches));
        return await AnnotateConflictsAsync(menu);
    }

    public Task ExecuteAsync(RefAction action) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        switch (action.Kind)
        {
            case RefActionKind.CopyName when action.Target is { } name:
                await (RequestClipboard?.Invoke(name) ?? Task.CompletedTask);
                return;
            case RefActionKind.CopySha when action.Sha is { } sha:
                await (RequestClipboard?.Invoke(sha) ?? Task.CompletedTask);
                return;
            case RefActionKind.Submenu:
                return;
            case RefActionKind.CreatePatch when action.Sha is { } patchSha:
                if (RequestSaveFile is not null)
                {
                    var patch = await _writer.CreatePatchAsync(_repositoryPath, patchSha);
                    await RequestSaveFile($"{patchSha[..Math.Min(7, patchSha.Length)]}.patch", patch);
                }

                return;
            case RefActionKind.CopyMessage when action.Sha is { } messageSha:
                var details = await _reader.GetCommitDetailsAsync(_repositoryPath, messageSha);
                var message = string.IsNullOrEmpty(details.Body) ? details.Summary : $"{details.Summary}\n\n{details.Body}";
                await (RequestClipboard?.Invoke(message) ?? Task.CompletedTask);
                return;
            case RefActionKind.EditMessage when action.Sha is { } rewordSha:
                RewordRequested?.Invoke(rewordSha, action.Source ?? "HEAD");
                return;
            case RefActionKind.InteractiveRebase when action.Sha is { } onto:
                InteractiveRebaseRequested?.Invoke(onto, action.Source ?? "HEAD");
                return;
            case RefActionKind.BisectGood or RefActionKind.BisectBad or RefActionKind.BisectSkip when action.Sha is { } bisectSha:
                BisectRequested?.Invoke(bisectSha, action.Kind switch
                {
                    RefActionKind.BisectGood => BisectMark.Good,
                    RefActionKind.BisectBad => BisectMark.Bad,
                    _ => BisectMark.Skip,
                });
                return;
            case RefActionKind.CreatePullRequest when action.Target is { } branch:
                if (action.Source == "remote")
                {
                    PullRequestRequested?.Invoke(null, branch);
                }
                else
                {
                    PullRequestRequested?.Invoke(branch, null);
                }

                return;
            case RefActionKind.SetConflictTarget when action.Target is { } conflictTarget:
                await _conflictDetection.SetTargetAsync(_repositoryPath, conflictTarget);
                _changeNotifier.NotifyChanged();
                return;
        }

        if (action.IsDestructive
            && RequestConfirmation is not null
            && !await RequestConfirmation.Invoke(action.Label, $"{action.Label}? This can lose work that is not committed or pushed elsewhere."))
        {
            return;
        }

        string? input = null;
        if (action.InputPrompt is { } prompt)
        {
            if (RequestInput is null)
            {
                return;
            }

            input = await RequestInput.Invoke(action.Label, prompt, DefaultInput(action));
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }
        }

        try
        {
            await _operations.ExecuteAsync(_repositoryPath, action, input, _remoteActions.PullMode);
            _notifications.Show($"{action.Label.Split("   ⚠")[0]}: done");
        }
        finally
        {
            // Also after a failure: a merge or rebase stopped on conflicts has changed the repository.
            _changeNotifier.NotifyChanged();
        }
    }, action.Label);

    /// <summary>
    /// Merges each offered merge / rebase in memory first and appends a warning to the entries that
    /// would stop on conflicts, so the user knows before choosing.
    /// </summary>
    private async Task<IReadOnlyList<IReadOnlyList<RefAction>>> AnnotateConflictsAsync(IReadOnlyList<IReadOnlyList<RefAction>> menu)
    {
        if (_repositoryPath is not { } path)
        {
            return menu;
        }

        var annotated = new List<IReadOnlyList<RefAction>>(menu.Count);
        foreach (var group in menu)
        {
            var actions = new List<RefAction>(group.Count);
            foreach (var action in group)
            {
                if (action.Kind is RefActionKind.Merge or RefActionKind.Rebase && action.Source is { } source && action.Target is { } target)
                {
                    var prediction = await _conflictDetection.PredictAsync(path, target, source);
                    actions.Add(prediction.HasConflicts
                        ? action with { Label = $"{action.Label}   ⚠ conflicts in {prediction.ConflictedFiles.Count} file(s)" }
                        : action);
                }
                else
                {
                    actions.Add(action);
                }
            }

            annotated.Add(actions);
        }

        return annotated;
    }

    private static string? DefaultInput(RefAction action) => action.Kind switch
    {
        RefActionKind.Rename => action.Target,
        RefActionKind.SetUpstream => $"origin/{action.Target}",
        _ => null,
    };

    private static IReadOnlyList<string> RemoteNames(IReadOnlyList<GitBranch> branches) =>
        branches.Where(b => b.IsRemote && b.Name.Contains('/'))
            .Select(b => b.Name[..b.Name.IndexOf('/')])
            .Distinct()
            .ToList();

    private static IReadOnlyCollection<string> LocalNames(IReadOnlyList<GitBranch> branches) =>
        branches.Where(b => b.IsLocal).Select(b => b.Name).ToHashSet();
}
