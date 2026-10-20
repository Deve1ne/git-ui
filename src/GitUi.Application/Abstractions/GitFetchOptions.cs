namespace GitUi.Application.Abstractions;

/// <summary>What <c>git fetch</c> does with tags.</summary>
public enum GitFetchTags
{
    /// <summary>git's default: tags pointing into the fetched history.</summary>
    Default,

    /// <summary>--tags: every tag of the remote.</summary>
    All,

    /// <summary>--no-tags: no tag at all.</summary>
    None,
}

/// <summary>
/// Fetch options, saved in the settings and used by the toolbar Fetch, the remote menus and auto-fetch.
/// </summary>
/// <param name="Prune">--prune: removes remote-tracking branches deleted on the remote.</param>
/// <param name="PruneTags">--prune-tags: also deletes local tags that no longer exist on the remote (needs Prune).</param>
/// <param name="RecurseSubmodules">--recurse-submodules=yes: fetches inside every submodule too.</param>
public sealed record GitFetchOptions(
    bool Prune = true,
    bool PruneTags = false,
    GitFetchTags Tags = GitFetchTags.Default,
    bool RecurseSubmodules = false)
{
    public static readonly GitFetchOptions Default = new();

    /// <summary>
    /// The <c>git fetch</c> arguments for every remote (<paramref name="remote"/> null, --all) or one remote.
    /// </summary>
    /// <param name="force">--force: also updates local tags that were moved on the server.</param>
    public IReadOnlyList<string> ToArguments(string? remote = null, bool force = false)
    {
        var arguments = new List<string> { "fetch" };
        if (remote is null)
        {
            arguments.Add("--all");
        }

        if (force)
        {
            arguments.Add("--force");
        }

        if (Prune)
        {
            arguments.Add("--prune");
            if (PruneTags)
            {
                arguments.Add("--prune-tags");
            }
        }
        else
        {
            arguments.Add("--no-prune");
        }

        switch (Tags)
        {
            case GitFetchTags.All:
                arguments.Add("--tags");
                break;
            case GitFetchTags.None:
                arguments.Add("--no-tags");
                break;
        }

        if (RecurseSubmodules)
        {
            arguments.Add("--recurse-submodules=yes");
        }

        if (remote is not null)
        {
            arguments.Add("--");
            arguments.Add(remote);
        }

        return arguments;
    }
}
