using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

public enum RefKind { LocalBranch, RemoteBranch, Tag, Commit }

/// <summary>
/// What a context menu or a drag-and-drop acts on. <see cref="Name"/> is the branch name for a
/// local branch, <c>remote/branch</c> for a remote one, the tag name for a tag and the SHA for a bare commit.
/// </summary>
public sealed record RefTarget(RefKind Kind, string Name, string Sha, string? Upstream = null)
{
    public static RefTarget Commit(string sha) => new(RefKind.Commit, sha, sha);

    /// <summary>For a remote branch <c>origin/feature</c>: <c>origin</c>.</summary>
    public string? RemoteName => Kind == RefKind.RemoteBranch ? Name[..Name.IndexOf('/')] : null;

    /// <summary>For a remote branch <c>origin/feature</c>: <c>feature</c>.</summary>
    public string? RemoteBranchName => Kind == RefKind.RemoteBranch ? Name[(Name.IndexOf('/') + 1)..] : null;

    public string ShortSha => Sha.Length > 7 ? Sha[..7] : Sha;
}

/// <summary>The checked-out state: a branch name, or null when HEAD is detached.</summary>
public sealed record HeadInfo(string? BranchName, string Sha);

public enum RefActionKind
{
    Pull,
    FastForwardFromUpstream,
    Push,
    SetUpstream,
    FastForward,
    Merge,
    Rebase,
    Checkout,
    CheckoutRemote,
    CheckoutCommit,
    CreateBranchHere,
    CherryPick,
    Reset,
    Revert,
    Rename,
    Delete,
    DeleteLocalAndRemote,
    DeleteRemote,
    CreateTagHere,
    DeleteTag,
    PushTag,
    CopyName,
    CopySha,
    SetConflictTarget,
    Submenu,

    /// <summary>Saves the commit <see cref="RefAction.Sha"/> as a .patch file (the view asks where).</summary>
    CreatePatch,

    CopyMessage,

    /// <summary>Rewords <see cref="RefAction.Sha"/>: the interactive rebase editor opens with that commit set to Reword.</summary>
    EditMessage,

    /// <summary>Opens the interactive rebase editor for HEAD onto <see cref="RefAction.Sha"/>; nothing runs until the user starts it.</summary>
    InteractiveRebase,

    /// <summary>Opens the create pull request dialog for the branch <see cref="RefAction.Target"/> (local, or remote/branch).</summary>
    CreatePullRequest,

    /// <summary>Marks <see cref="RefAction.Sha"/> for <c>git bisect</c> (the first mark starts the session).</summary>
    BisectGood,
    BisectBad,
    BisectSkip,
}

/// <summary>
/// One menu entry. <see cref="Source"/> and <see cref="Target"/> carry the revisions involved:
/// for a merge, <see cref="Source"/> is merged into <see cref="Target"/>; for a rebase,
/// <see cref="Source"/> is replayed onto <see cref="Target"/>; for a fast-forward,
/// <see cref="Target"/> moves to <see cref="Source"/>.
/// </summary>
public sealed record RefAction(RefActionKind Kind, string Label)
{
    public string? Source { get; init; }
    public string? Target { get; init; }
    public string? Sha { get; init; }
    public string? Remote { get; init; }
    public string? RemoteBranch { get; init; }
    public GitResetMode ResetMode { get; init; }
    public bool IsDestructive { get; init; }
    public IReadOnlyList<RefAction> Children { get; init; } = Array.Empty<RefAction>();

    /// <summary>The prompt shown before running, for actions that need a name from the user.</summary>
    public string? InputPrompt => Kind switch
    {
        RefActionKind.CreateBranchHere => "New branch name",
        RefActionKind.Rename => $"New name for {Target}",
        RefActionKind.CreateTagHere => "Tag name",
        RefActionKind.SetUpstream => $"Upstream for {Target} (remote/branch)",
        _ => null,
    };
}
