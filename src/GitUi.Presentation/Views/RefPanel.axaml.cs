using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using GitUi.Presentation.Controls;
using GitUi.Presentation.Services;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

// Left-hand ref panel: LOCAL / REMOTE / STASHES / TAGS sections. The panel's DataContext is the
// BranchViewModel; the STASHES section gets the StashViewModel through StashViewModel below.
// Branches and tags get GitKraken-style context menus and drag-and-drop through RefActions.
public partial class RefPanel : UserControl
{
    public RefPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BranchViewModel viewModel)
            {
                viewModel.RequestConfirmation = ConfirmAsync;
                viewModel.RequestInput = PromptAsync;

                // Tags start collapsed; a filter must still show the tags it matches.
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(BranchViewModel.IsFiltering) && viewModel.IsFiltering)
                    {
                        this.FindControl<Expander>("TagsSection")!.IsExpanded = true;
                    }
                };
            }
        };

        this.FindControl<Button>("ReflogButton")!.Click += (_, e) =>
        {
            e.Handled = true; // not a click on the section header (which would fold it)
            ReflogRequested?.Invoke();
        };

        foreach (var listName in new[] { "LocalList", "RemoteList", "TagList" })
        {
            var list = this.FindControl<ListBox>(listName)!;
            list.ContextRequested += OnRefContextRequested;
            RefDrag.AttachSource(list, e => TargetUnder(e.Source));
            RefDrag.AttachTarget(list, e => TargetUnder(e.Source), OnRefDroppedAsync);
        }
    }

    public StashViewModel? StashViewModel
    {
        get => this.FindControl<Expander>("StashSection")!.DataContext as StashViewModel;
        set
        {
            this.FindControl<Expander>("StashSection")!.DataContext = value;
            if (value is not null)
            {
                value.RequestConfirmation = ConfirmAsync;
            }
        }
    }

    /// <summary>The LOCAL header's reflog button.</summary>
    public event Action? ReflogRequested;

    public PullRequestsViewModel? PullRequests
    {
        get => this.FindControl<Expander>("PullRequestSection")!.DataContext as PullRequestsViewModel;
        set => this.FindControl<Expander>("PullRequestSection")!.DataContext = value;
    }

    public GitFlowViewModel? GitFlow
    {
        get => this.FindControl<Expander>("GitFlowSection")!.DataContext as GitFlowViewModel;
        set
        {
            this.FindControl<Expander>("GitFlowSection")!.DataContext = value;
            if (value is not null)
            {
                value.RequestConfirmation = ConfirmAsync;
                value.RequestInput = PromptAsync;
            }
        }
    }

    public LfsViewModel? Lfs
    {
        get => this.FindControl<Expander>("LfsSection")!.DataContext as LfsViewModel;
        set
        {
            this.FindControl<Expander>("LfsSection")!.DataContext = value;
            if (value is not null)
            {
                value.RequestConfirmation = ConfirmAsync;
                value.RequestInput = PromptAsync;
            }
        }
    }

    private void OnPullRequestDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: PullRequestRow row } && PullRequests is { } pullRequests)
        {
            pullRequests.OpenInBrowserCommand.Execute(row);
        }
    }

    /// <summary>Context menus and drag-and-drop actions for branches and tags.</summary>
    public RefActionsViewModel? RefActions { get; set; }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Puts the keyboard focus in the branch filter (Ctrl+Alt+F).</summary>
    public void FocusFilter()
    {
        var filter = this.FindControl<TextBox>("FilterBox")!;
        filter.Focus();
        filter.SelectAll();
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialogService = App.Services!.GetService(typeof(DialogService)) as DialogService;
        // Avalonia 12: the visual root is a TopLevelHost, not the window itself.
        var owner = (Window)TopLevel.GetTopLevel(this)!;
        return dialogService is not null && await dialogService.ConfirmDestructiveActionAsync(owner, title, message);
    }

    /// <summary>The branch or tag of the list row containing <paramref name="source"/>.</summary>
    private static RefTarget? TargetUnder(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext switch
        {
            RefRowViewModel { Branch: { } branch } => RefActionsViewModel.FromBranch(branch),
            GitBranch branch => RefActionsViewModel.FromBranch(branch),
            GitTag tag => RefActionsViewModel.FromTag(tag),
            _ => null,
        };

    private Task<string?> PromptAsync(string title, string prompt, string? initialValue)
    {
        var dialogService = App.Services!.GetService(typeof(DialogService)) as DialogService;
        return dialogService is null ? Task.FromResult<string?>(null) : dialogService.PromptAsync((Window)TopLevel.GetTopLevel(this)!, title, prompt, initialValue);
    }

    /// <summary>A remote's own row ("origin"): fetch, edit URL, rename, remove.</summary>
    private bool TryOpenRemoteMenu(Control control, object? source)
    {
        if ((source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not RefRowViewModel { IsRemoteRoot: true } row
            || DataContext is not BranchViewModel viewModel)
        {
            return false;
        }

        var remote = row.Label;
        var menu = new ContextMenu();
        void Add(string header, Func<string, Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => _ = action(remote);
            menu.Items.Add(item);
        }

        Add($"Fetch {remote}", viewModel.FetchRemoteAsync);
        Add("Edit URL…", viewModel.EditRemoteUrlAsync);
        Add("Rename…", viewModel.RenameRemoteAsync);
        menu.Items.Add(new Separator());
        Add($"Remove {remote}", viewModel.RemoveRemoteAsync);
        menu.Open(control);
        return true;
    }

    private async void OnRefContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control remoteList && TryOpenRemoteMenu(remoteList, e.Source))
        {
            e.Handled = true;
            return;
        }

        if (RefActions is null || sender is not Control control || TargetUnder(e.Source) is not { } target)
        {
            return;
        }

        e.Handled = true;
        var groups = await RefActions.BuildContextMenuAsync(target, inGraph: false);
        if (groups.Count > 0)
        {
            RefMenuFactory.Build(groups, RefActions.ExecuteAsync).Open(control);
        }
    }

    private async Task OnRefDroppedAsync(RefTarget dragged, RefTarget target)
    {
        if (RefActions is null)
        {
            return;
        }

        var groups = await RefActions.BuildDragDropMenuAsync(dragged, target);
        if (groups.Count > 0)
        {
            RefMenuFactory.Build(groups, RefActions.ExecuteAsync).Open(this);
        }
    }

    private void OnLocalBranchDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: RefRowViewModel { Branch: { } branch } } && DataContext is BranchViewModel viewModel)
        {
            viewModel.CheckoutCommand.Execute(branch);
        }
    }
}
