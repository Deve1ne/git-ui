namespace GitUi.Application.Abstractions;

public enum GitMergeMode
{
    /// <summary>Always create a merge commit (<c>--no-ff</c>).</summary>
    NoFastForward,

    /// <summary>Only move the branch pointer (<c>--ff-only</c>).</summary>
    FastForwardOnly,
}
