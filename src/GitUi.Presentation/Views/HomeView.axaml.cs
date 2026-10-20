using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

/// <summary>
/// New-tab page. Its DataContext is the <see cref="HomeViewModel"/>; <see cref="Control.Tag"/> carries
/// the <see cref="MainWindowViewModel"/> for the "Back to repository" button.
/// </summary>
public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnRecentTapped(object? sender, TappedEventArgs e)
    {
        // The remove button sits inside the row: let it act on its own.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (sender is Control { DataContext: RecentRepositoryItem item } && DataContext is HomeViewModel viewModel)
        {
            viewModel.OpenRecentCommand.Execute(item);
        }
    }
}
