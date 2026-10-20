using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Services;
using GitUi.Domain.ValueObjects;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>One commit line of the interactive rebase editor.</summary>
public partial class RebaseItemViewModel : ObservableObject
{
    public RebaseItemViewModel(string sha, string message, string author)
    {
        Sha = sha;
        var newline = message.IndexOf('\n');
        Summary = newline < 0 ? message : message[..newline].TrimEnd('\r');
        NewMessage = message;
        Author = author;
    }

    public string Sha { get; }

    public string ShortSha => Sha[..Math.Min(7, Sha.Length)];

    public string Summary { get; }

    public string Author { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReword), nameof(IsDropped), nameof(Opacity))]
    private RebaseAction action = RebaseAction.Pick;

    /// <summary>Starts as the commit's full message; edited when the action is Reword.</summary>
    [ObservableProperty]
    private string newMessage;

    public bool IsReword => Action == RebaseAction.Reword;

    public bool IsDropped => Action == RebaseAction.Drop;

    public double Opacity => IsDropped ? 0.45 : 1.0;

    public RebaseStep ToStep() => new(Sha, Action, IsReword ? NewMessage : null);
}

/// <summary>
/// GitKraken's interactive rebase panel, over the graph: choose pick / reword / squash / fixup /
/// drop per commit, reorder them, then start the rebase.
/// </summary>
public partial class InteractiveRebaseViewModel : ObservableObject
{
    private readonly InteractiveRebaseService _service;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;
    private string? _ontoSha;

    public InteractiveRebaseViewModel(InteractiveRebaseService service, RepositoryChangeNotifier changeNotifier, CommandErrorHandler errorHandler)
    {
        _service = service;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
    }

    public static IReadOnlyList<RebaseAction> Actions { get; } = Enum.GetValues<RebaseAction>();

    public ObservableCollection<RebaseItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private RebaseItemViewModel? selectedItem;

    public Task OpenAsync(string repositoryPath, string ontoSha, string branchName) => _errorHandler.RunGuardedAsync(
        async () => Show(repositoryPath, ontoSha, branchName, await _service.GetCommitsAsync(repositoryPath, ontoSha)),
        "prepare interactive rebase");

    /// <summary>"Edit commit message": the editor opens on the commit's parent with that commit set to Reword.</summary>
    public Task OpenRewordAsync(string repositoryPath, string sha, string branchName) => _errorHandler.RunGuardedAsync(async () =>
    {
        var (onto, commits) = await _service.PrepareRewordAsync(repositoryPath, sha);
        Show(repositoryPath, onto, branchName, commits);
        if (Items.FirstOrDefault(i => i.Sha == sha) is { } item)
        {
            item.Action = RebaseAction.Reword;
            SelectedItem = item;
        }
    }, "prepare commit message edit");

    private void Show(string repositoryPath, string ontoSha, string branchName, IReadOnlyList<Domain.Entities.GitCommit> commits)
    {
        _repositoryPath = repositoryPath;
        _ontoSha = ontoSha;
        Items.Clear();
        foreach (var commit in commits)
        {
            Items.Add(new RebaseItemViewModel(commit.Sha, commit.Message, commit.Author));
        }

        Title = $"Interactive rebase: {branchName} onto {ontoSha[..Math.Min(7, ontoSha.Length)]} ({commits.Count} commit{(commits.Count == 1 ? string.Empty : "s")})";
        SelectedItem = Items.FirstOrDefault();
        IsOpen = true;
    }

    [RelayCommand]
    private void MoveUp(RebaseItemViewModel item) => Move(item, -1);

    [RelayCommand]
    private void MoveDown(RebaseItemViewModel item) => Move(item, +1);

    private void Move(RebaseItemViewModel item, int offset)
    {
        var index = Items.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Items.Count)
        {
            return;
        }

        Items.Move(index, target);
        SelectedItem = item;
    }

    /// <summary>GitKraken's keyboard shortcuts on the selected commit: P, R, S, F, D.</summary>
    public bool ApplyShortcut(char key)
    {
        if (SelectedItem is not { } item)
        {
            return false;
        }

        RebaseAction? action = char.ToLowerInvariant(key) switch
        {
            'p' => RebaseAction.Pick,
            'r' => RebaseAction.Reword,
            's' => RebaseAction.Squash,
            'f' => RebaseAction.Fixup,
            'd' => RebaseAction.Drop,
            _ => null,
        };
        if (action is null)
        {
            return false;
        }

        item.Action = action.Value;
        return true;
    }

    [RelayCommand]
    private Task StartAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path || _ontoSha is not { } onto)
        {
            return;
        }

        var steps = Items.Select(i => i.ToStep()).ToList();
        RebaseTodoBuilder.Validate(steps);
        Close();
        try
        {
            await _service.RunAsync(path, onto, steps);
        }
        finally
        {
            // Also after conflicts: the rebase banner offers Continue / Abort.
            _changeNotifier.NotifyChanged();
        }
    }, "interactive rebase");

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        Items.Clear();
        SelectedItem = null;
    }
}
