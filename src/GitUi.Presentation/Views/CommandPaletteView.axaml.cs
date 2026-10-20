using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();

        this.FindControl<Border>("Backdrop")!.PointerPressed += (_, _) => (DataContext as CommandPaletteViewModel)?.CloseCommand.Execute(null);

        // A click runs the item, like Enter.
        this.FindControl<ListBox>("ResultList")!.Tapped += (_, e) =>
        {
            if (DataContext is CommandPaletteViewModel viewModel && viewModel.Selected is { } command)
            {
                e.Handled = true;
                viewModel.ExecuteCommand.Execute(command);
            }
        };

        // Focus the query box each time the palette appears.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("QueryBox")!.Focus());
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
