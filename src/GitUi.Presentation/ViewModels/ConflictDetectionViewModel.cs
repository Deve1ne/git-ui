using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// Toolbar indicator for proactive conflict detection: after every refresh the checked-out branch is
/// merged in memory with its target branch; a conflict shows a warning with the files involved and
/// one-click merge / rebase, like GitKraken's "Conflict detected with target branch" popover.
/// </summary>
public partial class ConflictDetectionViewModel : ObservableObject
{
    private readonly ConflictDetectionService _detection;
    private readonly RefActionsViewModel _refActions;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;
    private int _checkGeneration;

    public ConflictDetectionViewModel(ConflictDetectionService detection, RefActionsViewModel refActions, CommandErrorHandler errorHandler)
    {
        _detection = detection;
        _refActions = refActions;
        _errorHandler = errorHandler;
    }

    public Func<string, string, string?, Task<string?>>? RequestInput { get; set; }

    public ObservableCollection<string> ConflictedFiles { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private string? branchName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private string? target;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon), nameof(Label), nameof(Title), nameof(Description))]
    private bool hasConflicts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon), nameof(Label))]
    private bool isChecking;

    public bool IsVisible => BranchName is not null && Target is not null;

    public string Icon => IsChecking ? "…" : HasConflicts ? "⚠" : "✓";

    public string Label => IsChecking ? "Checking" : HasConflicts ? $"{ConflictedFiles.Count} conflict(s)" : "No conflicts";

    public string Title => HasConflicts ? "Conflict detected with target branch" : "Up to date with merge target";

    public string Description => HasConflicts
        ? $"Merging {Target} into {BranchName} would stop on conflicts. They get harder to resolve as both branches keep changing: merge or rebase early."
        : $"{BranchName} will not cause conflicts when merging with {Target}.";

    public void SetRepositoryPath(string? repositoryPath)
    {
        _repositoryPath = repositoryPath;
        if (repositoryPath is null)
        {
            Apply(ConflictCheckResult.NotApplicable);
        }
    }

    /// <summary>Runs the check in the background; a newer check supersedes a slower older one.</summary>
    [RelayCommand]
    public Task CheckAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path)
        {
            return;
        }

        var generation = ++_checkGeneration;
        IsChecking = true;
        try
        {
            var result = await Task.Run(() => _detection.CheckAsync(path));
            if (generation == _checkGeneration && path == _repositoryPath)
            {
                Apply(result);
            }
        }
        finally
        {
            if (generation == _checkGeneration)
            {
                IsChecking = false;
            }
        }
    }, "check for conflicts");

    private void Apply(ConflictCheckResult result)
    {
        ConflictedFiles.Clear();
        foreach (var file in result.Prediction.ConflictedFiles)
        {
            ConflictedFiles.Add(file);
        }

        BranchName = result.IsApplicable ? result.BranchName : null;
        Target = result.IsApplicable ? result.Target : null;
        HasConflicts = result.Prediction.HasConflicts;
        OnPropertyChanged(nameof(Label));
    }

    [RelayCommand]
    private Task MergeTargetAsync() => BranchName is { } branch && Target is { } target
        ? _refActions.ExecuteAsync(new RefAction(RefActionKind.Merge, $"Merge {target} into {branch}") { Source = target, Target = branch })
        : Task.CompletedTask;

    [RelayCommand]
    private Task RebaseOntoTargetAsync() => BranchName is { } branch && Target is { } target
        ? _refActions.ExecuteAsync(new RefAction(RefActionKind.Rebase, $"Rebase {branch} onto {target}") { Source = branch, Target = target })
        : Task.CompletedTask;

    [RelayCommand]
    private Task ChangeTargetAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null || RequestInput is null)
        {
            return;
        }

        var answer = await RequestInput.Invoke("Conflict detection target", "Branch to detect conflicts with (e.g. origin/main)", Target);
        if (!string.IsNullOrWhiteSpace(answer))
        {
            await SetTargetAsync(answer.Trim());
        }
    }, "change conflict detection target");

    public async Task SetTargetAsync(string newTarget)
    {
        if (_repositoryPath is null)
        {
            return;
        }

        await _detection.SetTargetAsync(_repositoryPath, newTarget);
        await CheckAsync();
    }
}
