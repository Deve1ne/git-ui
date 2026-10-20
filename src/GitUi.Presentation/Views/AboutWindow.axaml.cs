using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.Views;

/// <summary>Version, author, license and links, from the app menu.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = AppInfo.Version is null ? "Development build"
            : AppInfo.Commit is null ? "Version " + AppInfo.Version
            : $"Version {AppInfo.Version} ({AppInfo.Commit})";
        this.FindControl<TextBlock>("VersionText")!.Text = version;
        this.FindControl<TextBlock>("CopyrightText")!.Text = AppInfo.Copyright;
        NameScope.GetNameScope(this)!.Find<Run>("LicenseRun")!.Text = AppInfo.LicenseName;
        this.FindControl<TextBlock>("RuntimeText")!.Text = AppInfo.Runtime;

        void Link(string name, string url) => this.FindControl<Button>(name)!.Click += (_, _) => ShellLauncher.OpenUrl(url);
        Link("RepositoryButton", AppInfo.RepositoryUrl);
        Link("ReleasesButton", AppInfo.ReleasesUrl);
        Link("LicenseButton", AppInfo.LicenseUrl);

        this.FindControl<Button>("CopyButton")!.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync($"{AppInfo.Name} {version}{Environment.NewLine}{AppInfo.Runtime}");
            }
        };
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
