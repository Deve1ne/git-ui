namespace GitUi.Application.Abstractions;

/// <summary>How `git pull` integrates the fetched upstream into the current branch.</summary>
public enum GitPullMode
{
    /// <summary>Fast-forward when possible, otherwise create a merge commit.</summary>
    Merge,

    /// <summary>Refuse to pull unless the branch can be fast-forwarded.</summary>
    FastForwardOnly,

    /// <summary>Replay local commits on top of the upstream.</summary>
    Rebase,
}
