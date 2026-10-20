using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitUi.Presentation.Views;

public partial class CommitDetailsPanel : UserControl
{
    public CommitDetailsPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
