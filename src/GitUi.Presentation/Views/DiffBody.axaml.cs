using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitUi.Presentation.Views;

public partial class DiffBody : UserControl
{
    public DiffBody() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
