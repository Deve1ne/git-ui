using CommunityToolkit.Mvvm.ComponentModel;

namespace GitUi.Presentation.ViewModels;

/// <summary>One open repository in the tab bar.</summary>
public partial class RepositoryTab : ObservableObject
{
    public RepositoryTab(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
    }

    public string Path { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool isActive;
}
