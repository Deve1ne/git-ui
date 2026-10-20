using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

public partial class ReflogWindow : Window
{
    public ReflogWindow() => InitializeComponent();

    public ReflogWindow(ReflogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
        this.FindControl<ListBox>("ReflogList")!.DoubleTapped += (_, _) => viewModel.ShowCommitCommand.Execute(null);
        void OnShow(string _) => Close();
        viewModel.ShowCommitRequested += OnShow;
        Closed += (_, _) => viewModel.ShowCommitRequested -= OnShow;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
