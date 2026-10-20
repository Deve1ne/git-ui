using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Hosting;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// The "Create pull request" dialog: source branch, target branch (the service's default branch
/// preselected), title, description, draft. A local branch is pushed to the remote first when it is
/// not there yet or has unpushed commits, so the service sees what the user sees.
/// </summary>
public partial class CreatePullRequestViewModel : ObservableObject
{
    private readonly HostingService _hosting;
    private readonly IGitRepositoryReader _reader;
    private readonly IGitRepositoryWriter _writer;
    private readonly string _repositoryPath;
    private readonly RepositoryHosting _repositoryHosting;
    private readonly string? _localBranch;

    public CreatePullRequestViewModel(
        HostingService hosting,
        IGitRepositoryReader reader,
        IGitRepositoryWriter writer,
        string repositoryPath,
        RepositoryHosting repositoryHosting,
        string? localBranch,
        string? remoteBranch)
    {
        _hosting = hosting;
        _reader = reader;
        _writer = writer;
        _repositoryPath = repositoryPath;
        _repositoryHosting = repositoryHosting;
        _localBranch = localBranch;
        var prefix = repositoryHosting.RemoteName + "/";
        SourceBranch = localBranch ?? (remoteBranch!.StartsWith(prefix, StringComparison.Ordinal) ? remoteBranch[prefix.Length..] : remoteBranch);
    }

    public string WindowTitle => $"Create {_repositoryHosting.Repository.Provider.PullRequestName()}";

    public string Destination => $"{_repositoryHosting.Repository.Provider.DisplayName()} · {_repositoryHosting.Repository.FullPath}";

    /// <summary>The branch the pull request merges from, as named on the remote.</summary>
    public string SourceBranch { get; private set; }

    public IReadOnlyList<string> TargetBranches { get; private set; } = Array.Empty<string>();

    [ObservableProperty]
    private string? targetBranch;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private bool isDraft;

    [ObservableProperty]
    private bool openAfterCreating = true;

    /// <summary>What happens before the creation (the push), shown under the source branch.</summary>
    [ObservableProperty]
    private string? pushNote;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Raised with the created pull request; the dialog closes on it.</summary>
    public event Action<PullRequest>? Created;

    private (string Remote, string Branch, bool SetUpstream)? _pushTo;

    public async Task InitializeAsync()
    {
        var remote = _repositoryHosting.RemoteName;
        var branches = await _reader.GetBranchesAsync(_repositoryPath);
        TargetBranches = branches
            .Where(b => b.IsRemote && b.Name.StartsWith(remote + "/", StringComparison.Ordinal) && !b.Name.EndsWith("/HEAD", StringComparison.Ordinal))
            .Select(b => b.Name[(remote.Length + 1)..])
            .Where(b => b != SourceBranch)
            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();
        OnPropertyChanged(nameof(TargetBranches));

        if (_localBranch is { } local && branches.FirstOrDefault(b => b.IsLocal && b.Name == local) is { } branch)
        {
            Title = (await _reader.GetCommitDetailsAsync(_repositoryPath, branch.TipSha)).Summary;
            var upstream = branch.TrackingBranch;
            if (upstream is not null && upstream.StartsWith(remote + "/", StringComparison.Ordinal))
            {
                SourceBranch = upstream[(remote.Length + 1)..];
                OnPropertyChanged(nameof(SourceBranch));
                if (branch.AheadBy > 0)
                {
                    _pushTo = (remote, SourceBranch, false);
                    PushNote = $"{branch.AheadBy} commit(s) of {local} are pushed to {upstream} first.";
                }
            }
            else
            {
                _pushTo = (remote, local, true);
                PushNote = $"{local} is not on {remote} yet: it is pushed there first (and set as its upstream).";
            }
        }
        else if (branches.FirstOrDefault(b => b.IsRemote && b.Name == $"{remote}/{SourceBranch}") is { } remoteBranch)
        {
            Title = (await _reader.GetCommitDetailsAsync(_repositoryPath, remoteBranch.TipSha)).Summary;
        }

        string? defaultBranch = null;
        try
        {
            defaultBranch = await _hosting.GetDefaultBranchAsync(_repositoryHosting);
        }
        catch (HostingException)
        {
            // The list below still offers a sensible choice.
        }

        TargetBranch = new[] { defaultBranch, "main", "master", "develop" }.FirstOrDefault(b => b is not null && TargetBranches.Contains(b))
            ?? TargetBranches.FirstOrDefault();
    }

    private bool CanCreate() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (TargetBranch is not { } target)
        {
            ErrorMessage = "Choose the branch to merge into.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (_pushTo is { } push && _localBranch is { } local)
            {
                await _writer.PushBranchAsync(_repositoryPath, push.Remote, local, push.Branch, push.SetUpstream);
                _pushTo = null;
                PushNote = null;
            }

            var created = await _hosting.CreatePullRequestAsync(_repositoryHosting,
                new NewPullRequest(SourceBranch, target, Title.Trim(), Description.Trim(), IsDraft));
            Created?.Invoke(created);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
