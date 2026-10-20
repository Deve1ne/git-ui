using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitUi.Presentation.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog() => InitializeComponent();

    public ConfirmationDialog(string title, string message) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        this.FindControl<Button>("ConfirmButton")!.Click += (_, _) => Close(true);
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close(false);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
