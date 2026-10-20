using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitUi.Presentation.Views;

public partial class CommitDiffView : UserControl
{
    public CommitDiffView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
