using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

public partial class DiffView : UserControl
{
    public DiffView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ListBox.SelectedItems cannot be bound to a read-only list: push the selection to the view model.
    private void OnDiffSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && DataContext is StagingViewModel viewModel)
        {
            viewModel.SelectedDiffRows = listBox.SelectedItems?.OfType<DiffRow>().ToList() ?? new List<DiffRow>();
        }
    }
}
