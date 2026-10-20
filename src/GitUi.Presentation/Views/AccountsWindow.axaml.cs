using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitUi.Presentation.ViewModels;

namespace GitUi.Presentation.Views;

public partial class AccountsWindow : Window
{
    public AccountsWindow() => InitializeComponent();

    public AccountsWindow(AccountsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
        Opened += async (_, _) => await viewModel.LoadAsync();
    }

    /// <summary>Opens the Accounts window over <paramref name="owner"/> and waits until it is closed.</summary>
    public static Task ShowAsync(Window owner)
    {
        var viewModel = (AccountsViewModel)App.Services!.GetService(typeof(AccountsViewModel))!;
        return new AccountsWindow(viewModel).ShowDialog(owner);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
