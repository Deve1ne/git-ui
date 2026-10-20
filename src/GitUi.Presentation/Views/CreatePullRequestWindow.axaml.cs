using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitUi.Application.Hosting;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

/// <summary>Closes with the created pull request, or null when cancelled.</summary>
public partial class CreatePullRequestWindow : Window
{
    public CreatePullRequestWindow() => InitializeComponent();

    public CreatePullRequestWindow(CreatePullRequestViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.Created += created => Close(created);
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close(null);
        Opened += (_, _) => this.FindControl<TextBox>("TitleBox")!.Focus();
    }

    public static Task<PullRequest?> ShowAsync(Window owner, CreatePullRequestViewModel viewModel) =>
        new CreatePullRequestWindow(viewModel).ShowDialog<PullRequest?>(owner);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
