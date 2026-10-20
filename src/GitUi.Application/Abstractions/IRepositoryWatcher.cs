namespace GitUi.Application.Abstractions;

/// <summary>What changed in a watched repository, from the cheapest refresh to the full one.</summary>
public enum RepositoryChangeKind
{
    /// <summary>Files of the work tree or the index: only the staging area needs a refresh.</summary>
    WorkingTree,

    /// <summary>HEAD, branches, tags, stash or an operation in progress: everything needs a refresh.</summary>
    References,
}

/// <summary>
/// Watches the open repository on disk so changes made outside the app (a terminal, another git
/// client, an editor) show up without a manual refresh. Events are debounced and raised off the UI thread.
/// </summary>
public interface IRepositoryWatcher : IDisposable
{
    event Action<RepositoryChangeKind>? Changed;

    /// <summary>Starts watching <paramref name="repositoryPath"/> (null stops watching).</summary>
    void Watch(string? repositoryPath);

    /// <summary>
    /// Takes the current refs as known: the app calls it after reloading for its own operations,
    /// so the watcher does not report them a second time.
    /// </summary>
    void Rebaseline();
}
