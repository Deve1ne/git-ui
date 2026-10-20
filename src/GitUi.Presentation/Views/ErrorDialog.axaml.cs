using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitUi.Presentation.Views;

public partial class ErrorDialog : Window
{
    public ErrorDialog() => InitializeComponent();

    public ErrorDialog(string message, string logFilePath) : this()
    {
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        this.FindControl<TextBlock>("LogPathText")!.Text = $"Log file: {logFilePath}";
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
