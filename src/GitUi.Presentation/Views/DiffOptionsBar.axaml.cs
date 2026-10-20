using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitUi.Presentation.Views;

public partial class DiffOptionsBar : UserControl
{
    public DiffOptionsBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
