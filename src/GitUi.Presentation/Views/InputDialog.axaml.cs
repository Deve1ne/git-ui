using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitUi.Presentation.Views;

/// <summary>Single-line text prompt; closes with the entered text, or null when cancelled.</summary>
public partial class InputDialog : Window
{
    public InputDialog() => InitializeComponent();

    public InputDialog(string title, string prompt, string? initialValue) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("PromptText")!.Text = prompt;
        var valueBox = this.FindControl<TextBox>("ValueBox")!;
        valueBox.Text = initialValue ?? string.Empty;
        this.FindControl<Button>("OkButton")!.Click += (_, _) => Close(valueBox.Text);
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            valueBox.Focus();
            valueBox.SelectAll();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
