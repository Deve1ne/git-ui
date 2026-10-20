namespace GitUi.Presentation.Services;

/// <summary>
/// Raised by any view model after an operation that changes the repository (commit, checkout,
/// pull, stash, undo...). The main window reloads every panel for the active repository in response,
/// so panels never show state another panel just invalidated.
/// </summary>
public sealed class RepositoryChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
