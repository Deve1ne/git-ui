using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public sealed record GitFlowBranchRow(GitFlowBranch Branch, bool IsHead)
{
    public string Kind => Branch.Kind.ToString().ToLowerInvariant();

    public string FinishLabel => $"Finish {Kind} {Branch.Name}";
}

/// <summary>
/// GIT FLOW section of the ref panel: initialize, start a feature / release / hotfix, and finish
/// the ones in progress (listed under their prefixes), like GitKraken's Gitflow panel.
/// </summary>
public partial class GitFlowViewModel : ObservableObject
{
    private readonly GitFlowService _flow;
    private readonly IGitRepositoryReader _reader;
    private readonly UndoService _undoService;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private readonly NotificationService _notifications;
    private string? _repositoryPath;
    private GitFlowConfig? _config;

    public GitFlowViewModel(
        GitFlowService flow,
        IGitRepositoryReader reader,
        UndoService undoService,
        RepositoryChangeNotifier changeNotifier,
        CommandErrorHandler errorHandler,
        NotificationService notifications)
    {
        _flow = flow;
        _reader = reader;
        _undoService = undoService;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
        _notifications = notifications;
    }

    public ObservableCollection<GitFlowBranchRow> Branches { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool isInitialized;

    public bool ShowEmpty => IsInitialized && Branches.Count == 0;

    [ObservableProperty]
    private string header = "GIT FLOW";

    [ObservableProperty]
    private string? configSummary;

    public Func<string, string, string?, Task<string?>>? RequestInput { get; set; }
    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }

    public void Clear()
    {
        _repositoryPath = null;
        _config = null;
        IsInitialized = false;
        Branches.Clear();
    }

    public async Task LoadAsync(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
        _config = await _flow.GetConfigAsync(repositoryPath);
        IsInitialized = _config is not null;
        Branches.Clear();
        if (_config is not { } config)
        {
            Header = "GIT FLOW";
            return;
        }

        var branches = await _reader.GetBranchesAsync(repositoryPath);
        var head = branches.FirstOrDefault(b => b.IsHead && b.IsLocal)?.Name;
        foreach (var flow in GitFlowService.Classify(config, branches.Where(b => b.IsLocal).Select(b => b.Name)))
        {
            Branches.Add(new GitFlowBranchRow(flow, flow.Branch == head));
        }

        Header = $"GIT FLOW ({Branches.Count})";
        OnPropertyChanged(nameof(ShowEmpty));
        ConfigSummary = $"{config.ProductionBranch} ← {config.DevelopBranch} · {config.FeaturePrefix} {config.ReleasePrefix} {config.HotfixPrefix}";
    }

    private async Task<string?> AskAsync(string title, string prompt, string? initial = null)
    {
        var answer = RequestInput is null ? null : await RequestInput(title, prompt, initial);
        return string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
    }

    [RelayCommand]
    private Task InitializeAsync() => RunAsync(async path =>
    {
        var suggested = await _flow.SuggestConfigAsync(path);
        if (await AskAsync("Initialize Git Flow", "Production branch (releases are merged and tagged there):", suggested.ProductionBranch) is not { } production
            || await AskAsync("Initialize Git Flow", "Development branch (created from production when missing):", suggested.DevelopBranch) is not { } develop)
        {
            return;
        }

        await _flow.InitializeAsync(path, suggested with { ProductionBranch = production, DevelopBranch = develop });
        _notifications.Show($"Git Flow initialized ({production} / {develop})");
    }, "initialize Git Flow");

    [RelayCommand]
    private Task StartFeatureAsync() => StartAsync(GitFlowKind.Feature, "Feature name (e.g. login-page):");

    [RelayCommand]
    private Task StartReleaseAsync() => StartAsync(GitFlowKind.Release, "Release version (e.g. 1.2.0):");

    [RelayCommand]
    private Task StartHotfixAsync() => StartAsync(GitFlowKind.Hotfix, "Hotfix version (e.g. 1.2.1):");

    private Task StartAsync(GitFlowKind kind, string prompt) => RunAsync(async path =>
    {
        if (_config is not { } config)
        {
            throw new InvalidOperationException("Initialize Git Flow first.");
        }

        if (await AskAsync($"Start {kind.ToString().ToLowerInvariant()}", $"{prompt}\nIt branches from {config.BaseBranch(kind)}.") is not { } name)
        {
            return;
        }

        var branch = await _flow.StartAsync(path, config, kind, name);
        _notifications.Show($"Started {branch}");
    }, $"start {kind.ToString().ToLowerInvariant()}");

    [RelayCommand]
    private Task FinishAsync(GitFlowBranchRow row) => RunAsync(async path =>
    {
        if (_config is not { } config)
        {
            return;
        }

        var flow = row.Branch;
        string? tagMessage = null;
        if (flow.Kind == GitFlowKind.Feature)
        {
            if (RequestConfirmation is not null
                && !await RequestConfirmation(row.FinishLabel, $"Merge {flow.Branch} into {config.DevelopBranch} (--no-ff) and delete it?"))
            {
                return;
            }
        }
        else
        {
            var tag = config.VersionTagPrefix + flow.Name;
            tagMessage = await AskAsync(row.FinishLabel,
                $"Merges {flow.Branch} into {config.ProductionBranch}, tags it {tag}, merges the tag into {config.DevelopBranch} and deletes the branch.\nTag message:",
                $"{(flow.Kind == GitFlowKind.Release ? "Release" : "Hotfix")} {flow.Name}");
            if (tagMessage is null)
            {
                return;
            }
        }

        await _flow.FinishAsync(path, config, flow, tagMessage);
        _notifications.Show($"Finished {flow.Branch}");
    }, "finish");

    [RelayCommand]
    private Task CheckoutAsync(GitFlowBranchRow row) => RunAsync(path => _undoService.CheckoutAsync(path, row.Branch.Branch), "checkout");

    private Task RunAsync(Func<string, Task> action, string operation) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path)
        {
            return;
        }

        try
        {
            await action(path);
        }
        finally
        {
            _changeNotifier.NotifyChanged();
        }
    }, operation);
}
