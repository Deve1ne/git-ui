using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitUi.Presentation.Views;

public partial class FileHistoryView : UserControl
{
    public FileHistoryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
