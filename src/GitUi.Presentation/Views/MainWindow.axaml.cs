using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GitUi.Application.Abstractions;
using GitUi.Application.DTOs;
using GitUi.Application.Services;
using GitUi.Presentation.Controls;
using GitUi.Presentation.Services;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

public partial class MainWindow : Window
{
    private const int RefPanelColumn = 0;
    private const int DetailPanelColumn = 4;

    private bool _wired;
    private readonly LayoutTransformControl _zoomHost;
    private double _zoom = 1.0;

    // Widths to restore when Ctrl+J / Ctrl+K bring a hidden side panel back.
    private GridLength _refPanelWidth;
    private GridLength _detailPanelWidth;

    private void SetZoom(double zoom, bool save = true)
    {
        _zoom = Math.Clamp(Math.Round(zoom, 2), 0.7, 1.6);
        _zoomHost.LayoutTransform = Math.Abs(_zoom - 1.0) < 0.001 ? null : new Avalonia.Media.ScaleTransform(_zoom, _zoom);
        if (save)
        {
            var zoomValue = _zoom;
            _ = Resolve<ISettingsStore>().UpdateAsync(s => s with { Zoom = zoomValue });
        }
    }

    /// <summary>Ctrl+J (ref panel) / Ctrl+K (detail panel): hide or show a side panel and its splitter.</summary>
    private void TogglePanel(int panelColumn, int splitterColumn, ref GridLength savedWidth)
    {
        var grid = this.FindControl<Grid>("MainColumns")!;
        var column = grid.ColumnDefinitions[panelColumn];
        var children = grid.Children.Where(c => Grid.GetColumn(c) == panelColumn || Grid.GetColumn(c) == splitterColumn).ToList();
        var hide = children.Any(c => c.IsVisible);
        if (hide)
        {
            savedWidth = column.Width;
            column.MinWidth = 0;
            column.Width = new GridLength(0);
        }
        else
        {
            column.Width = savedWidth.Value > 0 ? savedWidth : new GridLength(280);
        }

        foreach (var child in children)
        {
            child.IsVisible = !hide;
        }
    }

    /// <summary>Restoring the previous session's tabs; the loading screen waits for it.</summary>
    public Task StartupTask { get; private set; } = Task.CompletedTask;
    private CommandViewModel? _commandViewModel;
    private Func<Task>? _openReflog;
    private System.Windows.Input.ICommand? _openPreferences;

    public MainWindow()
    {
        InitializeComponent();

        // The version in the title bar shows which build is running, e.g. after an upgrade.
        if (AppInfo.Version is { } version)
            Title = $"{AppInfo.Name} {version}";

        // Zoom (Ctrl+= / Ctrl+- / Ctrl+0) scales the whole window content, like GitKraken's zoom.
        var content = (Control)Content!;
        Content = null;
        _zoomHost = new LayoutTransformControl { Child = content };
        Content = _zoomHost;

        DataContextChanged += (_, _) => WireViewModel();

        Closing += (_, _) => SaveLayout();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private static T Resolve<T>() where T : class => (App.Services!.GetService(typeof(T)) as T)!;

    private void WireViewModel()
    {
        // WireViewModel adds event subscriptions, so a second call would double-subscribe
        // every panel load. DataContext is assigned once today; this keeps it harmless if it isn't.
        if (_wired || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var errorHandler = Resolve<CommandErrorHandler>();

        viewModel.RequestFolderPick = async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open Repository" });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        };

        viewModel.RequestErrorDialog = async message =>
        {
            var dialogService = Resolve<DialogService>();
            await dialogService.ShowErrorAsync(this, message, "see log directory in %AppData%/GitUi/logs");
        };

        Func<string, string, Task<bool>> confirm = (title, message) =>
            Resolve<DialogService>().ConfirmDestructiveActionAsync(this, title, message);

        var commitGraphViewModel = Resolve<CommitGraphViewModel>();
        var graphCanvas = this.FindControl<CommitGraphCanvas>("GraphCanvas")!;
        graphCanvas.DataContext = commitGraphViewModel;
        this.FindControl<Border>("GraphSearchBar")!.DataContext = commitGraphViewModel;
        commitGraphViewModel.Rows.CollectionChanged += (_, _) => graphCanvas.Rows = commitGraphViewModel.Rows.ToList();

        var stagingViewModel = Resolve<StagingViewModel>();
        var mergeTool = stagingViewModel.MergeTool;
        mergeTool.RequestConfirmation = confirm;
        stagingViewModel.RequestConfirmation = confirm;
        stagingViewModel.RequestInput = (title, prompt, initial) => Resolve<DialogService>().PromptAsync(this, title, prompt, initial);
        stagingViewModel.RequestClipboard = async text =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        this.FindControl<MergeToolView>("MergeToolControl")!.DataContext = mergeTool;

        var homeViewModel = Resolve<HomeViewModel>();
        var homeView = this.FindControl<HomeView>("HomeViewControl")!;
        homeView.DataContext = homeViewModel;
        homeView.Tag = viewModel;
        homeViewModel.RequestAccounts = () => AccountsWindow.ShowAsync(this);
        homeViewModel.RequestFolderPick = async title =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsHomeVisible) && viewModel.IsHomeVisible)
            {
                _ = errorHandler.RunGuardedAsync(homeViewModel.RefreshRecentAsync, "load recent repositories");
            }
        };
        this.FindControl<StagingPanel>("StagingPanelControl")!.DataContext = stagingViewModel;
        this.FindControl<DiffView>("DiffViewControl")!.DataContext = stagingViewModel;

        // The graph's "// WIP" row mirrors the working-directory change count.
        stagingViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StagingViewModel.TotalChangeCount))
            {
                graphCanvas.WipChangeCount = stagingViewModel.TotalChangeCount;
            }
        };
        // Commit detail: selecting a commit shows it on the right; the WIP row brings back the
        // working directory, like GitKraken.
        var commitDetails = Resolve<CommitDetailsViewModel>();
        this.FindControl<CommitDetailsPanel>("CommitDetailsControl")!.DataContext = commitDetails;
        this.FindControl<CommitDiffView>("CommitDiffControl")!.DataContext = commitDetails;
        commitDetails.RequestClipboard = async text =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        commitDetails.RequestConfirmation = confirm;
        commitDetails.WorkingDirectoryChanged += () => Resolve<RepositoryChangeNotifier>().NotifyChanged();
        commitDetails.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommitDetailsViewModel.IsVisible))
            {
                this.FindControl<DockPanel>("WorkingDirectoryPanel")!.IsVisible = !commitDetails.IsVisible;
            }
        };
        // Selects a commit in the graph, or just shows its detail when the graph does not draw it.
        void SelectCommit(string sha)
        {
            var row = commitGraphViewModel.Rows.FirstOrDefault(r => r.Node.Commit.Sha == sha);
            if (row is not null)
            {
                commitGraphViewModel.SelectedRow = row;
            }
            else
            {
                _ = commitDetails.LoadAsync(sha);
            }
        }

        commitDetails.ParentRequested += SelectCommit;
        commitGraphViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommitGraphViewModel.SelectedRow) && commitGraphViewModel.SelectedRow is { } row)
            {
                stagingViewModel.CloseDiffCommand.Execute(null);
                _ = row.Node.Commit.IsStash && Resolve<StashViewModel>().Stashes.FirstOrDefault(s => s.Sha == row.Node.Commit.Sha) is { } stash
                    ? commitDetails.LoadStashAsync(stash)
                    : commitDetails.LoadAsync(row.Node.Commit.Sha);
            }
        };
        graphCanvas.CompareRequested += (_, pair) =>
        {
            // The row further down the graph is the older commit: compare from it to the other one.
            var rows = commitGraphViewModel.Rows;
            var (older, newer) = rows.IndexOf(pair.Selected) > rows.IndexOf(pair.Other) ? (pair.Selected, pair.Other) : (pair.Other, pair.Selected);
            stagingViewModel.CloseDiffCommand.Execute(null);
            _ = commitDetails.LoadComparisonAsync(older.Node.Commit.Sha, newer.Node.Commit.Sha);
        };
        graphCanvas.WipRowClicked += (_, _) =>
        {
            stagingViewModel.CloseDiffCommand.Execute(null);
            commitDetails.Close();
        };

        // File history / blame covers the graph; opening any diff, or picking one of its commits, closes it.
        var fileHistory = Resolve<FileHistoryViewModel>();
        this.FindControl<FileHistoryView>("FileHistoryControl")!.DataContext = fileHistory;
        void OpenFileView(string file, bool blame)
        {
            stagingViewModel.CloseDiffCommand.Execute(null);
            commitDetails.CloseDiffCommand.Execute(null);
            _ = blame ? fileHistory.OpenBlameAsync(file) : fileHistory.OpenHistoryAsync(file);
        }

        stagingViewModel.FileViewRequested += OpenFileView;
        commitDetails.FileViewRequested += OpenFileView;
        stagingViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StagingViewModel.IsDiffVisible) && stagingViewModel.IsDiffVisible)
            {
                fileHistory.Close();
            }
        };
        commitDetails.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommitDetailsViewModel.IsDiffVisible) && commitDetails.IsDiffVisible)
            {
                fileHistory.Close();
            }
        };
        fileHistory.CommitRequested += sha =>
        {
            fileHistory.Close();
            SelectCommit(sha);
        };

        var branchViewModel = Resolve<BranchViewModel>();
        var refPanel = this.FindControl<RefPanel>("RefPanelControl")!;
        refPanel.DataContext = branchViewModel;
        branchViewModel.RequestOpenRepository = path => viewModel.OpenRecentCommand.Execute(path);
        this.FindControl<Button>("BranchButton")!.DataContext = branchViewModel;
        this.FindControl<StackPanel>("StatusBranch")!.DataContext = branchViewModel;

        var stashViewModel = Resolve<StashViewModel>();
        refPanel.StashViewModel = stashViewModel;
        this.FindControl<StackPanel>("StashButtons")!.DataContext = stashViewModel;
        stashViewModel.RequestPrompt = (title, prompt, initial) => Resolve<DialogService>().PromptAsync(this, title, prompt, initial);
        stashViewModel.PendingMessage = () => stagingViewModel.CommitSummary;
        stashViewModel.StashMessageUsed += () => stagingViewModel.CommitSummary = string.Empty;
        stashViewModel.StashSelected += stash =>
        {
            stagingViewModel.CloseDiffCommand.Execute(null);
            _ = commitDetails.LoadStashAsync(stash);
        };

        var remoteActionsViewModel = Resolve<RemoteActionsViewModel>();
        remoteActionsViewModel.RequestConfirmation = confirm;
        remoteActionsViewModel.RequestInput = (title, prompt, initial) => Resolve<DialogService>().PromptAsync(this, title, prompt, initial);
        stagingViewModel.PushAfterCommitRequested += remoteActionsViewModel.PushNowAsync;
        this.FindControl<StackPanel>("RemoteButtons")!.DataContext = remoteActionsViewModel;

        var historyViewModel = Resolve<HistoryViewModel>();
        this.FindControl<StackPanel>("HistoryButtons")!.DataContext = historyViewModel;

        var refActions = Resolve<RefActionsViewModel>();
        refActions.RequestConfirmation = confirm;
        refActions.RequestInput = (title, prompt, initial) => Resolve<DialogService>().PromptAsync(this, title, prompt, initial);
        refActions.RequestClipboard = async text =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        refPanel.RefActions = refActions;
        refActions.RequestSaveFile = async (suggestedName, content) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save patch",
                SuggestedFileName = suggestedName,
                DefaultExtension = "patch",
                FileTypeChoices = new[] { new FilePickerFileType("Patch") { Patterns = new[] { "*.patch", "*.diff" } } },
            });
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(content);
            viewModel.StatusMessage = $"Patch saved to {file.Path.LocalPath}";
        };

        var interactiveRebase = Resolve<InteractiveRebaseViewModel>();
        this.FindControl<InteractiveRebaseView>("InteractiveRebaseControl")!.DataContext = interactiveRebase;
        refActions.InteractiveRebaseRequested += (onto, branch) =>
        {
            if (viewModel.CurrentRepositoryPath is { } repositoryPath)
            {
                stagingViewModel.CloseDiffCommand.Execute(null);
                fileHistory.Close();
                _ = interactiveRebase.OpenAsync(repositoryPath, onto, branch);
            }
        };
        refActions.RewordRequested += (sha, branch) =>
        {
            if (viewModel.CurrentRepositoryPath is { } repositoryPath)
            {
                stagingViewModel.CloseDiffCommand.Execute(null);
                fileHistory.Close();
                _ = interactiveRebase.OpenRewordAsync(repositoryPath, sha, branch);
            }
        };

        var pullRequests = Resolve<PullRequestsViewModel>();
        refPanel.PullRequests = pullRequests;
        pullRequests.RequestAccounts = () => AccountsWindow.ShowAsync(this);
        pullRequests.RequestCreateDialog = dialog => CreatePullRequestWindow.ShowAsync(this, dialog);
        pullRequests.RequestClipboard = refActions.RequestClipboard;
        refActions.PullRequestRequested += (local, remote) => _ = pullRequests.StartCreateAsync(local, remote);

        // Bisect: marks from the graph's menu, banner over the graph.
        var bisect = Resolve<BisectViewModel>();
        this.FindControl<Border>("BisectBanner")!.DataContext = bisect;
        refActions.BisectRequested += (sha, mark) => _ = bisect.MarkAsync(sha, mark);
        bisect.SelectCommitRequested += SelectCommit;

        // Git Flow and LFS sections of the ref panel.
        var gitFlow = Resolve<GitFlowViewModel>();
        refPanel.GitFlow = gitFlow;
        var lfs = Resolve<LfsViewModel>();
        refPanel.Lfs = lfs;

        // Reflog window: from the LOCAL section's header and the palette.
        _openReflog = async () =>
        {
            if (viewModel.CurrentRepositoryPath is not { } repositoryPath)
            {
                return;
            }

            var reflog = Resolve<ReflogViewModel>();
            reflog.RequestInput = (title, prompt, initial) => Resolve<DialogService>().PromptAsync(this, title, prompt, initial);
            reflog.RequestConfirmation = confirm;
            reflog.RequestClipboard = refActions.RequestClipboard;
            reflog.ShowCommitRequested += SelectCommit;
            await errorHandler.RunGuardedAsync(() => reflog.LoadAsync(repositoryPath), "load reflog");
            await new ReflogWindow(reflog).ShowDialog(this);
        };
        refPanel.ReflogRequested += () => _ = _openReflog();

        // Identity profile switcher in the status bar.
        var profiles = Resolve<ProfilesViewModel>();
        var profileButton = this.FindControl<Button>("ProfileButton")!;
        profileButton.DataContext = profiles;
        _ = errorHandler.RunGuardedAsync(profiles.LoadAsync, "load profiles");
        profileButton.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (var row in profiles.Profiles)
            {
                var item = new MenuItem { Header = (row.IsActive ? "✓ " : "    ") + row.Profile.Name + "  —  " + row.Detail };
                item.Click += (_, _) => profiles.ActivateCommand.Execute(row);
                menu.Items.Add(item);
            }

            if (profiles.Profiles.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            var manage = new MenuItem { Header = profiles.HasProfiles ? "Manage profiles…" : "Create a profile (Preferences)…" };
            manage.Click += (_, _) => _openPreferences?.Execute(null);
            menu.Items.Add(manage);
            menu.Open(profileButton);
        };

        var conflictDetection = Resolve<ConflictDetectionViewModel>();
        conflictDetection.RequestInput = refActions.RequestInput;
        this.FindControl<Button>("ConflictIndicator")!.DataContext = conflictDetection;
        WireGraphRefActions(graphCanvas, refActions, branchViewModel);

        _commandViewModel = Resolve<CommandViewModel>();
        this.FindControl<CommandPanel>("CommandPanelControl")!.DataContext = _commandViewModel;

        var watcher = Resolve<IRepositoryWatcher>();

        void LoadAll(string path)
        {
            watcher.Watch(path);
            remoteActionsViewModel.SetRepositoryPath(path);
            historyViewModel.SetRepositoryPath(path);
            refActions.SetRepositoryPath(path);
            commitDetails.SetRepositoryPath(path);
            fileHistory.SetRepositoryPath(path);
            conflictDetection.SetRepositoryPath(path);
            _ = conflictDetection.CheckAsync();
            _commandViewModel.SetRepositoryPath(path);
            _ = errorHandler.RunGuardedAsync(() => commitGraphViewModel.LoadAsync(path), "load commit graph");
            _ = errorHandler.RunGuardedAsync(() => stagingViewModel.LoadAsync(path), "load staging panel");
            _ = errorHandler.RunGuardedAsync(() => branchViewModel.LoadAsync(path), "load branch panel");
            _ = errorHandler.RunGuardedAsync(() => stashViewModel.LoadAsync(path), "load stash panel");
            _ = errorHandler.RunGuardedAsync(() => pullRequests.LoadAsync(path), "load pull requests");
            _ = errorHandler.RunGuardedAsync(() => bisect.LoadAsync(path), "load bisect state");
            _ = errorHandler.RunGuardedAsync(() => gitFlow.LoadAsync(path), "load Git Flow");
            _ = errorHandler.RunGuardedAsync(() => lfs.LoadAsync(path), "load LFS");
        }

        viewModel.RepositoryOpened += LoadAll;

        // A merge / pull / rebase that stops on conflicts is the start of a resolution, not an error:
        // the refresh that follows shows the Continue / Abort banner and the Conflicted Files list.
        errorHandler.MergeConflictsOccurred += _ =>
        {
            viewModel.StatusMessage = "Stopped on conflicts: resolve the Conflicted Files (click one to open the merge tool), then Continue — or Abort.";
            stagingViewModel.CloseDiffCommand.Execute(null);
        };

        // Any panel that changes the repository asks for a full reload of the active one.
        Resolve<RepositoryChangeNotifier>().Changed += () =>
        {
            if (viewModel.CurrentRepositoryPath is { } path)
            {
                LoadAll(path);
                _ = Task.Run(watcher.Rebaseline);
            }
        };

        // Changes made outside the app (terminal, another client, an editor): new refs reload
        // everything, file edits only the staging area (and the graph's WIP row with it).
        watcher.Changed += kind => Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.CurrentRepositoryPath is not { } path)
            {
                return;
            }

            if (kind == RepositoryChangeKind.References)
            {
                LoadAll(path);
            }
            else
            {
                _ = errorHandler.RunGuardedAsync(() => stagingViewModel.LoadAsync(path), "refresh staging panel");
            }
        });

        viewModel.RepositoryClosed += () =>
        {
            watcher.Watch(null);
            remoteActionsViewModel.SetRepositoryPath(null);
            historyViewModel.SetRepositoryPath(null);
            refActions.SetRepositoryPath(null);
            commitDetails.SetRepositoryPath(null);
            fileHistory.SetRepositoryPath(null);
            conflictDetection.SetRepositoryPath(null);
            if (mergeTool.IsOpen)
            {
                mergeTool.CloseCommand.Execute(null);
            }

            commitGraphViewModel.Clear();
            stagingViewModel.Clear();
            branchViewModel.Clear();
            stashViewModel.Clear();
            pullRequests.Clear();
            bisect.Clear();
            gitFlow.Clear();
            lfs.Clear();
            graphCanvas.WipChangeCount = 0;
        };

        WireMenusAndShortcuts(viewModel, homeViewModel, historyViewModel, remoteActionsViewModel, stagingViewModel);

        _wired = true;

        _ = errorHandler.RunGuardedAsync(RestoreLayoutAsync, "restore layout");
        _ = errorHandler.RunGuardedAsync(remoteActionsViewModel.InitializeAsync, "load pull mode");
        _ = errorHandler.RunGuardedAsync(Resolve<DiffSettings>().LoadAsync, "load diff options");
        this.FindControl<ItemsControl>("ToastHost")!.DataContext = Resolve<NotificationService>();
        _ = errorHandler.RunGuardedAsync(homeViewModel.InitializeAsync, "load new-tab page");
        StartupTask = errorHandler.RunGuardedAsync(viewModel.RestoreSessionAsync, "restore open repositories");
    }

    /// <summary>
    /// Graph rows behave like GitKraken's: right-click a row for its branch (or commit) menu, drag a
    /// ref badge from the BRANCH / TAG column onto another row to merge, rebase, push or reset.
    /// </summary>
    private void WireGraphRefActions(CommitGraphCanvas graphCanvas, RefActionsViewModel refActions, BranchViewModel branches)
    {
        void OpenStashMenu(string sha)
        {
            var stashes = Resolve<StashViewModel>();
            if (stashes.Stashes.FirstOrDefault(s => s.Sha == sha) is not { } stash)
            {
                return;
            }

            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "Apply stash", Command = stashes.ApplyCommand, CommandParameter = stash });
            menu.Items.Add(new MenuItem { Header = "Pop stash", Command = stashes.PopCommand, CommandParameter = stash });
            menu.Items.Add(new MenuItem { Header = "Delete stash", Command = stashes.DropCommand, CommandParameter = stash });
            menu.Open(graphCanvas);
        }

        // A row stands for its checked-out branch first, then its first local branch, then any
        // label; a row without labels stands for the bare commit.
        RefTarget TargetForRow(CommitGraphRow row)
        {
            var label = row.Labels.FirstOrDefault(l => l.IsHead)
                ?? row.Labels.FirstOrDefault(l => l.Kind == RefLabelKind.LocalBranch)
                ?? row.Labels.FirstOrDefault();
            return label is null ? RefTarget.Commit(row.Node.Commit.Sha) : TargetForLabel(row, label);
        }

        RefTarget TargetForLabel(CommitGraphRow row, RefLabel label)
        {
            var kind = label.Kind switch
            {
                RefLabelKind.LocalBranch => RefKind.LocalBranch,
                RefLabelKind.RemoteBranch => RefKind.RemoteBranch,
                _ => RefKind.Tag,
            };
            var upstream = kind == RefKind.LocalBranch
                ? branches.Branches.FirstOrDefault(b => b.IsLocal && b.Name == label.Name)?.TrackingBranch
                : null;
            return new RefTarget(kind, label.Name, row.Node.Commit.Sha, upstream);
        }

        graphCanvas.ContextRequested += async (_, e) =>
        {
            if (!e.TryGetPosition(graphCanvas, out var point) || graphCanvas.RowAt(point) is not { } row)
            {
                return;
            }

            e.Handled = true;
            graphCanvas.SelectedRow = row;
            if (row.Node.Commit.IsStash)
            {
                OpenStashMenu(row.Node.Commit.Sha);
                return;
            }

            var groups = await refActions.BuildContextMenuAsync(TargetForRow(row), inGraph: true);
            if (groups.Count > 0)
            {
                RefMenuFactory.Build(groups, refActions.ExecuteAsync).Open(graphCanvas);
            }
        };

        // "+N" in the BRANCH / TAG column: every ref of the commit, each with its own actions.
        graphCanvas.RefsOverflowClicked += async (_, row) =>
        {
            var menu = new ContextMenu();
            foreach (var label in CommitGraphDrawOperation.OrderedLabels(row.Labels))
            {
                var kind = label.Kind switch
                {
                    RefLabelKind.LocalBranch => "branch",
                    RefLabelKind.RemoteBranch => "remote",
                    _ => "tag",
                };
                var item = new MenuItem { Header = label.IsHead ? $"✓ {label.Name}   ({kind}, checked out)" : $"{label.Name}   ({kind})" };
                var groups = await refActions.BuildContextMenuAsync(TargetForLabel(row, label), inGraph: true);
                foreach (var child in RefMenuFactory.BuildItems(groups, refActions.ExecuteAsync))
                {
                    item.Items.Add(child);
                }

                menu.Items.Add(item);
            }

            menu.Open(graphCanvas);
        };

        // Double-click checks out the row's branch, like GitKraken: a local branch directly, a remote
        // one through a local tracking branch (or the existing local one of the same name).
        graphCanvas.DoubleTapped += (_, e) =>
        {
            if (graphCanvas.RowAt(e.GetPosition(graphCanvas)) is not { } row || row.Labels.Any(l => l.IsHead))
            {
                return;
            }

            RefAction? checkout = null;
            if (row.Labels.FirstOrDefault(l => l.Kind == RefLabelKind.LocalBranch) is { } local)
            {
                checkout = new RefAction(RefActionKind.Checkout, $"Checkout {local.Name}") { Target = local.Name };
            }
            else if (row.Labels.FirstOrDefault(l => l.Kind == RefLabelKind.RemoteBranch) is { } remote)
            {
                var localName = remote.Name[(remote.Name.IndexOf('/') + 1)..];
                checkout = branches.Branches.Any(b => b.IsLocal && b.Name == localName)
                    ? new RefAction(RefActionKind.Checkout, $"Checkout {localName}") { Target = localName }
                    : new RefAction(RefActionKind.CheckoutRemote, $"Checkout {remote.Name}") { Target = remote.Name };
            }

            if (checkout is not null)
            {
                e.Handled = true;
                _ = refActions.ExecuteAsync(checkout);
            }
        };

        RefDrag.AttachSource(graphCanvas, e =>
        {
            var point = e.GetPosition(graphCanvas);
            return graphCanvas.IsInRefsColumn(point) && !graphCanvas.IsOverExpandedRefs(point) && graphCanvas.RowAt(point) is { Labels.Count: > 0 } row
                ? TargetForRow(row)
                : null;
        });

        RefDrag.AttachTarget(
            graphCanvas,
            e => graphCanvas.RowAt(e.GetPosition(graphCanvas)) is { } row ? TargetForRow(row) : null,
            async (dragged, target) =>
            {
                var groups = await refActions.BuildDragDropMenuAsync(dragged, target);
                if (groups.Count > 0)
                {
                    RefMenuFactory.Build(groups, refActions.ExecuteAsync).Open(graphCanvas);
                }
            });
    }

    /// <summary>
    /// Application menu (logo, top-left) and the Open dropdown, rebuilt on every opening so the
    /// recent list is current; plus the global keyboard shortcuts of a git client.
    /// </summary>
    private void WireMenusAndShortcuts(
        MainWindowViewModel viewModel,
        HomeViewModel home,
        HistoryViewModel history,
        RemoteActionsViewModel remote,
        StagingViewModel staging)
    {
        viewModel.HomeSectionRequested += section => home.ShowSectionCommand.Execute(section);

        static MenuItem Item(string header, System.Windows.Input.ICommand command, object? parameter = null, string? gesture = null) => new()
        {
            Header = header,
            Command = command,
            CommandParameter = parameter,
            InputGesture = gesture is null ? null : KeyGesture.Parse(gesture),
        };

        MenuItem RecentMenu()
        {
            var recent = new MenuItem { Header = "Recent repositories" };
            foreach (var path in viewModel.RecentRepositories.Where(Directory.Exists).Take(10))
            {
                recent.Items.Add(Item(path, viewModel.OpenRecentCommand, path));
            }

            if (recent.Items.Count == 0)
            {
                recent.Items.Add(new MenuItem { Header = "(none yet)", IsEnabled = false });
            }

            return recent;
        }

        IEnumerable<Control> RepositoryItems() => new Control[]
        {
            Item("Open repository…", viewModel.OpenRepositoryCommand, null, "Ctrl+O"),
            Item("Clone repository…", viewModel.ShowHomeSectionCommand, HomeSection.Clone, "Ctrl+N"),
            Item("New repository (init)…", viewModel.ShowHomeSectionCommand, HomeSection.Init, "Ctrl+I"),
            RecentMenu(),
        };

        // Apply a .patch / .diff file to the working directory (three-way), like GitKraken's "Apply patch".
        var applyPatch = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Resolve<CommandErrorHandler>().RunGuardedAsync(async () =>
        {
            if (viewModel.CurrentRepositoryPath is not { } repositoryPath)
            {
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Apply patch",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Patch") { Patterns = new[] { "*.patch", "*.diff" } }, FilePickerFileTypes.All },
            });
            if (files.Count == 0)
            {
                return;
            }

            try
            {
                await Resolve<IGitRepositoryWriter>().ApplyPatchFileAsync(repositoryPath, files[0].Path.LocalPath);
            }
            finally
            {
                Resolve<RepositoryChangeNotifier>().NotifyChanged();
            }
        }, "apply patch"));

        // Background fetch of the open repository; failures (offline, credentials) stay silent here,
        // the toolbar Fetch shows them.
        var autoFetchTimer = new Avalonia.Threading.DispatcherTimer();
        var fetching = false;
        autoFetchTimer.Tick += async (_, _) =>
        {
            if (fetching || viewModel.CurrentRepositoryPath is not { } path)
            {
                return;
            }

            fetching = true;
            try
            {
                await Resolve<RemoteService>().FetchWithOptionsAsync(path);
                Resolve<RepositoryChangeNotifier>().NotifyChanged();
            }
            catch (Exception)
            {
                // Logged by the git runner; nothing to show for a background attempt.
            }
            finally
            {
                fetching = false;
            }
        };

        async Task ApplyAutoFetchAsync()
        {
            var minutes = (await Resolve<ISettingsStore>().LoadAsync()).AutoFetchMinutes ?? 0;
            autoFetchTimer.Stop();
            if (minutes > 0)
            {
                autoFetchTimer.Interval = TimeSpan.FromMinutes(minutes);
                autoFetchTimer.Start();
            }
        }

        _ = ApplyAutoFetchAsync();

        // Preferences (Ctrl+,): git identity, auto-fetch, graph columns.
        var openPreferences = _openPreferences = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () =>
        {
            var preferences = Resolve<PreferencesViewModel>();
            var errorHandler = Resolve<CommandErrorHandler>();
            var loaded = false;
            await errorHandler.RunGuardedAsync(async () =>
            {
                await preferences.LoadAsync(viewModel.CurrentRepositoryPath, (CommitGraphColumns)Resources["GraphColumns"]!);
                loaded = true;
            }, "load preferences");

            if (loaded && await new PreferencesWindow(preferences, action => errorHandler.RunGuardedAsync(action, "save preferences")).ShowDialog<bool>(this))
            {
                await ApplyAutoFetchAsync();
            }
        });

        var openAbout = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => new AboutWindow().ShowDialog(this));

        // Command palette (Ctrl+P / Ctrl+Shift+P): every action, plus branches and recent repositories.
        var palette = Resolve<CommandPaletteViewModel>();
        this.FindControl<CommandPaletteView>("CommandPaletteControl")!.DataContext = palette;
        palette.CommandsProvider = () =>
        {
            static Action Do(System.Windows.Input.ICommand command, object? parameter = null) => () =>
            {
                if (command.CanExecute(parameter))
                {
                    command.Execute(parameter);
                }
            };

            var graph = Resolve<CommitGraphViewModel>();
            var stash = Resolve<StashViewModel>();
            var branchList = Resolve<BranchViewModel>();
            var commands = new List<PaletteCommand>
            {
                new("Remote", "Fetch all remotes", "Ctrl+L", Do(remote.FetchCommand)),
                new("Remote", remote.PullModeLabel, null, Do(remote.PullCommand)),
                new("Remote", "Push", null, Do(remote.PushCommand)),
                new("Remote", "Create pull request from the current branch…", null, Do(Resolve<PullRequestsViewModel>().CreateCommand)),
                new("Remote", "Reload pull requests", null, Do(Resolve<PullRequestsViewModel>().RefreshCommand)),
                new("Repository", "Reflog (where HEAD has been)…", null, () => _ = _openReflog?.Invoke()),
                new("Git Flow", "Initialize Git Flow…", null, Do(Resolve<GitFlowViewModel>().InitializeCommand)),
                new("Git Flow", "Start feature…", null, Do(Resolve<GitFlowViewModel>().StartFeatureCommand)),
                new("Git Flow", "Start release…", null, Do(Resolve<GitFlowViewModel>().StartReleaseCommand)),
                new("Git Flow", "Start hotfix…", null, Do(Resolve<GitFlowViewModel>().StartHotfixCommand)),
                new("LFS", "Track a file pattern with LFS…", null, Do(Resolve<LfsViewModel>().TrackCommand)),
                new("LFS", "Pull LFS files", null, Do(Resolve<LfsViewModel>().PullCommand)),
                new("LFS", "Fetch LFS files", null, Do(Resolve<LfsViewModel>().FetchCommand)),
                new("LFS", "Prune old local LFS files", null, Do(Resolve<LfsViewModel>().PruneCommand)),
                new("Bisect", "Bisect: mark the checked-out commit as bad", null, Do(Resolve<BisectViewModel>().BadCommand)),
                new("Bisect", "Bisect: mark the checked-out commit as good", null, Do(Resolve<BisectViewModel>().GoodCommand)),
                new("Bisect", "Bisect: end (reset)", null, Do(Resolve<BisectViewModel>().ResetCommand)),
                new("Changes", "Stage all changes", "Ctrl+Shift+S", Do(staging.StageAllCommand)),
                new("Changes", "Unstage all changes", "Ctrl+Shift+U", Do(staging.UnstageAllCommand)),
                new("Changes", "Discard all changes", null, Do(staging.DiscardAllCommand)),
                new("Changes", "Commit", "Ctrl+Enter", Do(staging.CommitCommand)),
                new("Changes", "Stash changes", null, Do(stash.SaveCommand)),
                new("Changes", "Stash changes with message…", null, Do(stash.SaveWithMessageCommand)),
                new("Changes", "Pop the latest stash", null, Do(stash.PopLatestCommand)),
                new("History", "Undo", "Ctrl+Z", Do(history.UndoCommand)),
                new("History", "Redo", "Ctrl+Y", Do(history.RedoCommand)),
                new("History", "Search commits", "Ctrl+F", () =>
                {
                    graph.OpenSearchCommand.Execute(null);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("GraphSearchBox")!.Focus());
                }),
                new("Branch", "Create branch…", "Ctrl+B", () =>
                {
                    var branchButton = this.FindControl<Button>("BranchButton")!;
                    branchButton.Flyout?.ShowAt(branchButton);
                }),
                new("Branch", "Filter branches", "Ctrl+Alt+F", () => this.FindControl<RefPanel>("RefPanelControl")!.FocusFilter()),
                new("Branch", "Show all branches in the graph", null, Do(branchList.ShowAllBranchesCommand)),
                new("Repository", "Open repository…", "Ctrl+O", Do(viewModel.OpenRepositoryCommand)),
                new("Repository", "Clone repository…", "Ctrl+N", Do(viewModel.ShowHomeSectionCommand, HomeSection.Clone)),
                new("Repository", "New repository (init)…", "Ctrl+I", Do(viewModel.ShowHomeSectionCommand, HomeSection.Init)),
                new("Repository", "Open in File Explorer", null, Do(viewModel.OpenInFileExplorerCommand)),
                new("Repository", "Open external terminal", null, Do(viewModel.OpenExternalTerminalCommand)),
                new("Repository", "Apply patch…", null, Do(applyPatch)),
                new("Repository", "Add remote…", null, Do(branchList.AddRemoteCommand)),
                new("Repository", "Add worktree…", null, Do(branchList.AddWorktreeCommand)),
                new("Repository", "Update submodules", null, Do(branchList.UpdateSubmodulesCommand)),
                new("Tabs", "New tab", "Ctrl+T", Do(viewModel.ShowHomeCommand)),
                new("Tabs", "Close tab", "Ctrl+W", Do(viewModel.CloseCurrentTabCommand)),
                new("Tabs", "Reopen closed tab", "Ctrl+Shift+T", Do(viewModel.ReopenClosedTabCommand)),
                new("App", "Preferences…", "Ctrl+,", Do(openPreferences)),
                new("App", "About Git UI", null, Do(openAbout)),
                new("App", "Accounts (GitHub, GitLab, Azure DevOps, Bitbucket)…", null, () => _ = AccountsWindow.ShowAsync(this)),
                new("App", viewModel.IsDarkTheme ? "Switch to the light theme" : "Switch to the dark theme", null, Do(viewModel.ToggleThemeCommand)),
                new("App", "Toggle the terminal panel", null, () => _commandViewModel?.TogglePanel()),
                new("View", "Toggle the branch panel", "Ctrl+J", () => TogglePanel(RefPanelColumn, RefPanelColumn + 1, ref _refPanelWidth)),
                new("View", "Toggle the detail panel", "Ctrl+K", () => TogglePanel(DetailPanelColumn, DetailPanelColumn - 1, ref _detailPanelWidth)),
                new("View", "Zoom in", "Ctrl+=", () => SetZoom(_zoom + 0.1)),
                new("View", "Zoom out", "Ctrl+-", () => SetZoom(_zoom - 0.1)),
                new("View", "Reset zoom", "Ctrl+0", () => SetZoom(1.0)),
            };

            foreach (var branch in branchList.Branches.Where(b => b.IsLocal && !b.IsHead))
            {
                commands.Add(new PaletteCommand("Checkout", branch.Name, null, Do(branchList.CheckoutCommand, branch)));
            }

            foreach (var path in viewModel.RecentRepositories.Where(Directory.Exists).Take(15))
            {
                commands.Add(new PaletteCommand("Open", path, null, Do(viewModel.OpenRecentCommand, path)));
            }

            return commands;
        };

        // An empty MenuFlyout never opens, so both menus are filled now and refreshed on each opening.
        var openMenu = (MenuFlyout)this.FindControl<Button>("OpenMenuButton")!.Flyout!;
        void FillOpenMenu()
        {
            openMenu.Items.Clear();
            foreach (var item in RepositoryItems())
            {
                openMenu.Items.Add(item);
            }
        }

        FillOpenMenu();
        openMenu.Opening += (_, _) => FillOpenMenu();

        var appMenu = (MenuFlyout)this.FindControl<Button>("AppMenuButton")!.Flyout!;
        void FillAppMenu()
        {
            appMenu.Items.Clear();
            appMenu.Items.Add(Item("New tab", viewModel.ShowHomeCommand, null, "Ctrl+T"));
            foreach (var item in RepositoryItems())
            {
                appMenu.Items.Add(item);
            }

            appMenu.Items.Add(new Separator());
            appMenu.Items.Add(Item("Close tab", viewModel.CloseCurrentTabCommand, null, "Ctrl+W"));
            appMenu.Items.Add(Item("Reopen closed tab", viewModel.ReopenClosedTabCommand, null, "Ctrl+Shift+T"));
            appMenu.Items.Add(Item("Next tab", viewModel.CycleTabCommand, 1, "Ctrl+Tab"));
            appMenu.Items.Add(Item("Previous tab", viewModel.CycleTabCommand, -1, "Ctrl+Shift+Tab"));
            appMenu.Items.Add(new Separator());
            appMenu.Items.Add(Item("Open in File Explorer", viewModel.OpenInFileExplorerCommand));
            appMenu.Items.Add(Item("Open external terminal", viewModel.OpenExternalTerminalCommand));
            appMenu.Items.Add(Item("Apply patch…", applyPatch));
            appMenu.Items.Add(new Separator());
            appMenu.Items.Add(Item(viewModel.IsDarkTheme ? "Light theme" : "Dark theme", viewModel.ToggleThemeCommand));
            appMenu.Items.Add(Item("Preferences…", openPreferences, null, "Ctrl+OemComma"));
            appMenu.Items.Add(Item("About Git UI", openAbout));
            appMenu.Items.Add(new MenuItem { Header = "Exit", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(Close), InputGesture = KeyGesture.Parse("Alt+F4") });
        }

        FillAppMenu();
        appMenu.Opening += (_, _) => FillAppMenu();

        // Tunnel: seen before the focused control, so Ctrl+Tab is not eaten by focus navigation.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            if (!ctrl)
            {
                return;
            }

            // Inside a text field, Ctrl+Z / Ctrl+Y and friends belong to the text, not to git.
            var typing = FocusManager?.GetFocusedElement() is TextBox;

            void Run(System.Windows.Input.ICommand command, object? parameter = null)
            {
                if (command.CanExecute(parameter))
                {
                    command.Execute(parameter);
                }

                e.Handled = true;
            }

            switch (e.Key)
            {
                case Key.OemComma:
                    Run(openPreferences);
                    break;
                case Key.P:
                    palette.Open();
                    e.Handled = true;
                    break;
                case Key.J when !typing:
                    TogglePanel(RefPanelColumn, RefPanelColumn + 1, ref _refPanelWidth);
                    e.Handled = true;
                    break;
                case Key.K when !typing:
                    TogglePanel(DetailPanelColumn, DetailPanelColumn - 1, ref _detailPanelWidth);
                    e.Handled = true;
                    break;
                case Key.OemPlus or Key.Add:
                    SetZoom(_zoom + 0.1);
                    e.Handled = true;
                    break;
                case Key.OemMinus or Key.Subtract:
                    SetZoom(_zoom - 0.1);
                    e.Handled = true;
                    break;
                case Key.D0 or Key.NumPad0:
                    SetZoom(1.0);
                    e.Handled = true;
                    break;
                case Key.F when alt:
                    this.FindControl<RefPanel>("RefPanelControl")!.FocusFilter();
                    e.Handled = true;
                    break;
                case Key.F when !shift:
                    var graphViewModel = Resolve<CommitGraphViewModel>();
                    graphViewModel.OpenSearchCommand.Execute(null);
                    var searchBox = this.FindControl<TextBox>("GraphSearchBox")!;
                    // The bar was just made visible: focus once it is laid out.
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        searchBox.Focus();
                        searchBox.SelectAll();
                    });
                    e.Handled = true;
                    break;
                case Key.T when shift:
                    Run(viewModel.ReopenClosedTabCommand);
                    break;
                case Key.T:
                    Run(viewModel.ShowHomeCommand);
                    break;
                case Key.O when !shift:
                    Run(viewModel.OpenRepositoryCommand);
                    break;
                case Key.N:
                    Run(viewModel.ShowHomeSectionCommand, HomeSection.Clone);
                    break;
                case Key.I when !typing:
                    Run(viewModel.ShowHomeSectionCommand, HomeSection.Init);
                    break;
                case Key.W:
                    Run(viewModel.CloseCurrentTabCommand);
                    break;
                case Key.Tab:
                    Run(viewModel.CycleTabCommand, shift ? -1 : 1);
                    break;
                case >= Key.D1 and <= Key.D9 when !shift:
                    Run(viewModel.SelectTabCommand, e.Key - Key.D0);
                    break;
                case >= Key.NumPad1 and <= Key.NumPad9 when !shift:
                    Run(viewModel.SelectTabCommand, e.Key - Key.NumPad0);
                    break;
                case Key.Z when !typing && shift:
                case Key.Y when !typing:
                    Run(history.RedoCommand);
                    break;
                case Key.Z when !typing:
                    Run(history.UndoCommand);
                    break;
                case Key.L:
                    Run(remote.FetchCommand);
                    break;
                case Key.B when !typing:
                    var branchButton = this.FindControl<Button>("BranchButton")!;
                    branchButton.Flyout?.ShowAt(branchButton);
                    e.Handled = true;
                    break;
                case Key.S when shift && !typing:
                    Run(staging.StageAllCommand);
                    break;
                case Key.U when shift && !typing:
                    Run(staging.UnstageAllCommand);
                    break;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnTabTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: RepositoryTab tab } && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ActivateTabCommand.Execute(tab);
        }
    }

    /// <summary>Right-click on a repository tab: close it or the others, open its folder, copy its path.</summary>
    private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: RepositoryTab tab } control || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        var menu = new ContextMenu();
        void Add(string header, Action action, string? gesture = null)
        {
            var item = new MenuItem { Header = header, InputGesture = gesture is null ? null : KeyGesture.Parse(gesture) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Close tab", () => viewModel.CloseTabCommand.Execute(tab), "Ctrl+W");
        Add("Close other tabs", () => viewModel.CloseOtherTabsCommand.Execute(tab));
        menu.Items.Add(new Separator());
        Add("Open in File Explorer", () => ShellLauncher.RevealInFolder(tab.Path.TrimEnd('\\', '/')));
        Add("Copy repository path", async () =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(tab.Path);
            }
        });
        menu.Open(control);
    }

    private async Task RestoreLayoutAsync()
    {
        var settings = await Resolve<ISettingsStore>().LoadAsync();
        if (settings.Zoom is { } zoom)
        {
            SetZoom(zoom, save: false);
        }

        if (settings.Layout is not { } layout)
        {
            return;
        }

        var columns = this.FindControl<Grid>("MainColumns")!.ColumnDefinitions;
        if (layout.RefPanelWidth > 0)
        {
            columns[RefPanelColumn].Width = new GridLength(layout.RefPanelWidth);
        }

        if (layout.DetailPanelWidth > 0)
        {
            columns[DetailPanelColumn].Width = new GridLength(layout.DetailPanelWidth);
        }

        var graphColumns = (CommitGraphColumns)Resources["GraphColumns"]!;
        graphColumns.Import(layout.GraphColumnWidths);
        graphColumns.ImportHidden(layout.HiddenGraphColumns);
    }

    private void SaveLayout()
    {
        var columns = this.FindControl<Grid>("MainColumns")!.ColumnDefinitions;
        var graphColumns = (CommitGraphColumns)Resources["GraphColumns"]!;
        var layout = new LayoutSettings(
            columns[RefPanelColumn].ActualWidth,
            columns[DetailPanelColumn].ActualWidth,
            graphColumns.Export(),
            graphColumns.ExportHidden());

        // Closing is synchronous; Task.Run keeps the settings IO off the UI synchronization
        // context so blocking on it here cannot deadlock.
        var store = Resolve<ISettingsStore>();
        Task.Run(() => store.UpdateAsync(settings => settings with { Layout = layout })).GetAwaiter().GetResult();
    }

    private void OnToggleCommandPanelClick(object? sender, RoutedEventArgs e) =>
        _commandViewModel?.TogglePanel();
}
