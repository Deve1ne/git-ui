using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

public partial class InteractiveRebaseView : UserControl
{
    public InteractiveRebaseView()
    {
        InitializeComponent();

        // P / R / S / F / D on the selected commit, as in GitKraken — but not while typing a message.
        this.FindControl<ListBox>("RebaseList")!.KeyDown += (_, e) =>
        {
            if (e.Source is TextBox || e.KeyModifiers != KeyModifiers.None || DataContext is not InteractiveRebaseViewModel viewModel)
            {
                return;
            }

            var letter = e.Key switch
            {
                Key.P => 'p',
                Key.R => 'r',
                Key.S => 's',
                Key.F => 'f',
                Key.D => 'd',
                _ => '\0',
            };
            if (letter != '\0' && viewModel.ApplyShortcut(letter))
            {
                e.Handled = true;
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
