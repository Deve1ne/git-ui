namespace GitUi.Application.Abstractions;

/// <summary>How two revisions relate in history — decides between fast-forward, merge and nothing.</summary>
public enum GitAncestry
{
    /// <summary>Both revisions point at the same commit.</summary>
    Same,

    /// <summary>The first revision is an ancestor of the second (the first can fast-forward to the second).</summary>
    FirstIsAncestor,

    /// <summary>The second revision is an ancestor of the first.</summary>
    SecondIsAncestor,

    /// <summary>Both have commits the other lacks: a real merge is needed.</summary>
    Diverged,
}
