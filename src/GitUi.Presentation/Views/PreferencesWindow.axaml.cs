using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

/// <summary>Closes with true once the preferences were saved, false when cancelled.</summary>
public partial class PreferencesWindow : Window
{
    public PreferencesWindow() => InitializeComponent();

    public PreferencesWindow(PreferencesViewModel viewModel, Func<Func<Task>, Task> runGuarded) : this()
    {
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Detach();
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close(false);
        this.FindControl<Button>("AccountsButton")!.Click += async (_, _) => await AccountsWindow.ShowAsync(this);
        this.FindControl<Button>("SaveButton")!.Click += async (_, _) =>
        {
            var saved = false;
            await runGuarded(async () =>
            {
                await viewModel.SaveAsync();
                saved = true;
            });

            if (saved)
            {
                Close(true);
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
