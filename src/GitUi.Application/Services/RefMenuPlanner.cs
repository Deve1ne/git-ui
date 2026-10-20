using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>
/// Decides which actions a branch/tag/commit context menu and a ref drag-and-drop offer, as groups
/// separated in the menu. Pure logic, modelled on GitKraken's menus: merge vs. fast-forward is
/// chosen from how the two commits relate in history, and nothing is offered that git would refuse
/// or that would be a no-op.
/// </summary>
public static class RefMenuPlanner
{
    /// <param name="target">What was right-clicked.</param>
    /// <param name="head">The checked-out branch and commit.</param>
    /// <param name="headVsTarget">Ancestry with HEAD as the first revision and the target as the second.</param>
    /// <param name="remotes">Remote names, e.g. "origin".</param>
    /// <param name="localBranchNames">Local branch names, to check out a remote branch's existing local twin.</param>
    /// <param name="inGraph">True from the commit graph, where a bare commit can also be checked out (detached).</param>
    public static IReadOnlyList<IReadOnlyList<RefAction>> BuildContextMenu(
        RefTarget target,
        HeadInfo head,
        GitAncestry? headVsTarget,
        IReadOnlyList<string> remotes,
        IReadOnlyCollection<string> localBranchNames,
        bool inGraph)
    {
        var isHead = target.Kind == RefKind.LocalBranch && target.Name == head.BranchName;
        var groups = new List<IReadOnlyList<RefAction>>
        {
            RemoteGroup(target, isHead, remotes),
            PullRequestGroup(target, remotes),
            MergeGroup(target, head, headVsTarget),
            CheckoutGroup(target, isHead, localBranchNames, inGraph),
            CommitGroup(target, head),
            ManageGroup(target, isHead, remotes),
            CopyGroup(target),
            new[] { new RefAction(RefActionKind.CreateTagHere, "Create tag here") { Sha = target.Sha } },
            ConflictDetectionGroup(target, isHead),
            inGraph ? BisectGroup(target) : Array.Empty<RefAction>(),
        };

        return groups.Where(g => g.Count > 0).ToList();
    }

    /// <summary>
    /// Actions for dropping <paramref name="dragged"/> onto <paramref name="target"/>.
    /// </summary>
    /// <param name="draggedVsTarget">Ancestry with the dragged ref first and the drop target second.</param>
    public static IReadOnlyList<IReadOnlyList<RefAction>> BuildDragDropMenu(
        RefTarget dragged,
        RefTarget target,
        HeadInfo head,
        GitAncestry draggedVsTarget,
        IReadOnlyList<string> remotes)
    {
        if (dragged.Sha == target.Sha && dragged.Name == target.Name)
        {
            return Array.Empty<IReadOnlyList<RefAction>>();
        }

        var integrate = new List<RefAction>();
        var targetIsLocal = target.Kind == RefKind.LocalBranch;
        var draggedIsLocal = dragged.Kind == RefKind.LocalBranch;

        if (targetIsLocal && dragged.Kind is RefKind.LocalBranch or RefKind.RemoteBranch or RefKind.Tag)
        {
            switch (draggedVsTarget)
            {
                case GitAncestry.FirstIsAncestor:
                    // The dragged ref is behind the target.
                    if (draggedIsLocal)
                    {
                        integrate.Add(FastForward(dragged.Name, target.Name));
                        integrate.Add(Merge(target.Name, dragged.Name));
                    }

                    break;
                case GitAncestry.SecondIsAncestor:
                    integrate.Add(FastForward(target.Name, dragged.Name));
                    integrate.Add(Merge(dragged.Name, target.Name));
                    break;
                case GitAncestry.Diverged:
                    integrate.Add(Merge(dragged.Name, target.Name));
                    break;
            }
        }

        if (draggedIsLocal && target.Kind is RefKind.LocalBranch or RefKind.RemoteBranch or RefKind.Tag
            && draggedVsTarget is GitAncestry.Diverged or GitAncestry.FirstIsAncestor)
        {
            integrate.Add(new RefAction(RefActionKind.Rebase, $"Rebase {dragged.Name} onto {target.Name}") { Source = dragged.Name, Target = target.Name });
        }

        var other = new List<RefAction>();
        if (draggedIsLocal && PushDestination(target) is { } destination)
        {
            other.Add(new RefAction(RefActionKind.Push, $"Push {dragged.Name} to {destination.Remote}/{destination.Branch}")
            {
                Target = dragged.Name,
                Remote = destination.Remote,
                RemoteBranch = destination.Branch,
            });
        }

        if (draggedIsLocal && dragged.Name == head.BranchName)
        {
            other.Add(ResetMenu(dragged.Name, target.Sha, "to this commit"));
        }

        return new IReadOnlyList<RefAction>[] { integrate, other }.Where(g => g.Count > 0).ToList();
    }

    private static (string Remote, string Branch)? PushDestination(RefTarget target) => target.Kind switch
    {
        RefKind.RemoteBranch => (target.RemoteName!, target.RemoteBranchName!),
        RefKind.LocalBranch when target.Upstream is { } upstream && upstream.Contains('/') =>
            (upstream[..upstream.IndexOf('/')], upstream[(upstream.IndexOf('/') + 1)..]),
        _ => null,
    };

    private static IReadOnlyList<RefAction> RemoteGroup(RefTarget target, bool isHead, IReadOnlyList<string> remotes)
    {
        if (target.Kind != RefKind.LocalBranch || remotes.Count == 0)
        {
            return Array.Empty<RefAction>();
        }

        var actions = new List<RefAction>();
        if (isHead)
        {
            actions.Add(new RefAction(RefActionKind.Pull, "Pull") { Target = target.Name });
        }
        else if (target.Upstream is not null)
        {
            actions.Add(new RefAction(RefActionKind.FastForwardFromUpstream, $"Pull (fast-forward {target.Name} to {target.Upstream})")
            {
                Target = target.Name,
                Source = target.Upstream,
            });
        }

        var (remote, remoteBranch) = target.Upstream is { } upstream && upstream.Contains('/')
            ? (upstream[..upstream.IndexOf('/')], upstream[(upstream.IndexOf('/') + 1)..])
            : (remotes.Contains("origin") ? "origin" : remotes[0], target.Name);

        actions.Add(new RefAction(RefActionKind.Push, target.Upstream is null ? $"Push {target.Name} to {remote}" : $"Push {target.Name} to {remote}/{remoteBranch}")
        {
            Target = target.Name,
            Remote = remote,
            RemoteBranch = remoteBranch,
        });
        actions.Add(new RefAction(RefActionKind.SetUpstream, "Set Upstream") { Target = target.Name });
        return actions;
    }

    /// <summary>"Create pull request from x…": the dialog checks the remote is on a supported service.</summary>
    private static IReadOnlyList<RefAction> PullRequestGroup(RefTarget target, IReadOnlyList<string> remotes) =>
        target.Kind is RefKind.LocalBranch or RefKind.RemoteBranch && remotes.Count > 0
            ? new[] { new RefAction(RefActionKind.CreatePullRequest, $"Create pull request from {target.Name}…") { Target = target.Name, Source = target.Kind == RefKind.RemoteBranch ? "remote" : "local" } }
            : Array.Empty<RefAction>();

    private static IReadOnlyList<RefAction> MergeGroup(RefTarget target, HeadInfo head, GitAncestry? headVsTarget)
    {
        if (head.BranchName is not { } headName || headVsTarget is null || target.Kind == RefKind.Commit
            || (target.Kind == RefKind.LocalBranch && target.Name == headName))
        {
            return Array.Empty<RefAction>();
        }

        var actions = new List<RefAction>();
        switch (headVsTarget)
        {
            case GitAncestry.SecondIsAncestor:
                // The target is behind HEAD: only a local branch can be moved forward or merged into.
                if (target.Kind == RefKind.LocalBranch)
                {
                    actions.Add(FastForward(target.Name, headName));
                    actions.Add(Merge(headName, target.Name));
                }

                break;
            case GitAncestry.FirstIsAncestor:
                actions.Add(FastForward(headName, target.Name));
                actions.Add(Merge(target.Name, headName));
                break;
            case GitAncestry.Diverged:
                actions.Add(Merge(target.Name, headName));
                break;
        }

        if (headVsTarget is GitAncestry.Diverged or GitAncestry.FirstIsAncestor)
        {
            actions.Add(new RefAction(RefActionKind.Rebase, $"Rebase {headName} onto {target.Name}") { Source = headName, Target = target.Name });
        }

        return actions;
    }

    private static IReadOnlyList<RefAction> CheckoutGroup(RefTarget target, bool isHead, IReadOnlyCollection<string> localBranchNames, bool inGraph)
    {
        var actions = new List<RefAction>();
        switch (target.Kind)
        {
            case RefKind.LocalBranch when !isHead:
                actions.Add(new RefAction(RefActionKind.Checkout, $"Checkout {target.Name}") { Target = target.Name });
                break;
            case RefKind.RemoteBranch:
                // Like GitKraken: checking out a remote branch switches to its local twin when one exists.
                actions.Add(localBranchNames.Contains(target.RemoteBranchName!)
                    ? new RefAction(RefActionKind.Checkout, $"Checkout {target.RemoteBranchName}") { Target = target.RemoteBranchName }
                    : new RefAction(RefActionKind.CheckoutRemote, $"Checkout {target.Name}") { Target = target.Name });
                break;
            case RefKind.Tag:
                actions.Add(new RefAction(RefActionKind.CheckoutCommit, $"Checkout the commit at {target.Name}") { Sha = target.Sha });
                break;
        }

        if (inGraph && target.Kind is RefKind.LocalBranch or RefKind.RemoteBranch or RefKind.Commit)
        {
            actions.Add(new RefAction(RefActionKind.CheckoutCommit, "Checkout this commit") { Sha = target.Sha });
        }

        return actions;
    }

    private static IReadOnlyList<RefAction> CommitGroup(RefTarget target, HeadInfo head)
    {
        var actions = new List<RefAction>
        {
            new(RefActionKind.CreateBranchHere, "Create branch here") { Sha = target.Sha },
        };

        if (target.Sha != head.Sha)
        {
            actions.Add(new RefAction(RefActionKind.CherryPick, "Cherry pick commit") { Sha = target.Sha });
            if (head.BranchName is { } headName)
            {
                actions.Add(ResetMenu(headName, target.Sha, "to this commit"));
                if (target.Kind == RefKind.Commit)
                {
                    actions.Add(new RefAction(RefActionKind.Rebase, $"Rebase {headName} onto this commit") { Source = headName, Target = target.Sha });
                    actions.Add(new RefAction(RefActionKind.InteractiveRebase, $"Interactive rebase {headName} onto this commit") { Source = headName, Sha = target.Sha });
                }
            }
        }

        actions.Add(new RefAction(RefActionKind.Revert, "Revert commit") { Sha = target.Sha });
        if (head.BranchName is { } branchName)
        {
            actions.Add(new RefAction(RefActionKind.EditMessage, "Edit commit message…") { Sha = target.Sha, Source = branchName });
        }

        actions.Add(new RefAction(RefActionKind.CreatePatch, "Create patch from commit…") { Sha = target.Sha });
        return actions;
    }

    private static IReadOnlyList<RefAction> ManageGroup(RefTarget target, bool isHead, IReadOnlyList<string> remotes)
    {
        var actions = new List<RefAction>();
        switch (target.Kind)
        {
            case RefKind.LocalBranch:
                actions.Add(new RefAction(RefActionKind.Rename, $"Rename {target.Name}") { Target = target.Name });
                if (!isHead)
                {
                    actions.Add(new RefAction(RefActionKind.Delete, $"Delete {target.Name}") { Target = target.Name, IsDestructive = true });
                    if (target.Upstream is { } upstream && upstream.Contains('/'))
                    {
                        actions.Add(new RefAction(RefActionKind.DeleteLocalAndRemote, $"Delete {target.Name} and {upstream}")
                        {
                            Target = target.Name,
                            Remote = upstream[..upstream.IndexOf('/')],
                            RemoteBranch = upstream[(upstream.IndexOf('/') + 1)..],
                            IsDestructive = true,
                        });
                    }
                }

                break;
            case RefKind.RemoteBranch:
                actions.Add(new RefAction(RefActionKind.DeleteRemote, $"Delete {target.Name}")
                {
                    Remote = target.RemoteName,
                    RemoteBranch = target.RemoteBranchName,
                    IsDestructive = true,
                });
                break;
            case RefKind.Tag:
                actions.Add(new RefAction(RefActionKind.DeleteTag, $"Delete {target.Name} locally") { Target = target.Name, IsDestructive = true });
                actions.AddRange(remotes.Select(remote =>
                    new RefAction(RefActionKind.PushTag, $"Push {target.Name} to {remote}") { Target = target.Name, Remote = remote }));
                break;
        }

        return actions;
    }

    /// <summary>Bisect from the graph: the first mark starts the session.</summary>
    private static IReadOnlyList<RefAction> BisectGroup(RefTarget target) => new[]
    {
        new RefAction(RefActionKind.Submenu, "Bisect")
        {
            Children = new[]
            {
                new RefAction(RefActionKind.BisectBad, "Mark as bad (has the bug)") { Sha = target.Sha },
                new RefAction(RefActionKind.BisectGood, "Mark as good (before the bug)") { Sha = target.Sha },
                new RefAction(RefActionKind.BisectSkip, "Skip (cannot be tested)") { Sha = target.Sha },
            },
        },
    };

    private static IReadOnlyList<RefAction> ConflictDetectionGroup(RefTarget target, bool isHead) =>
        target.Kind is RefKind.LocalBranch or RefKind.RemoteBranch && !isHead
            ? new[] { new RefAction(RefActionKind.SetConflictTarget, "Use as conflict detection target") { Target = target.Name } }
            : Array.Empty<RefAction>();

    private static IReadOnlyList<RefAction> CopyGroup(RefTarget target)
    {
        var actions = new List<RefAction>();
        if (target.Kind != RefKind.Commit)
        {
            actions.Add(new RefAction(RefActionKind.CopyName, target.Kind == RefKind.Tag ? "Copy tag name" : "Copy branch name") { Target = target.Name });
        }

        actions.Add(new RefAction(RefActionKind.CopySha, "Copy commit sha") { Sha = target.Sha });
        actions.Add(new RefAction(RefActionKind.CopyMessage, "Copy commit message") { Sha = target.Sha });
        return actions;
    }

    private static RefAction FastForward(string branch, string to) =>
        new(RefActionKind.FastForward, $"Fast-forward {branch} to {to}") { Target = branch, Source = to };

    private static RefAction Merge(string from, string into) =>
        new(RefActionKind.Merge, $"Merge {from} into {into}") { Source = from, Target = into };

    private static RefAction ResetMenu(string branch, string sha, string suffix) =>
        new(RefActionKind.Submenu, $"Reset {branch} {suffix}")
        {
            Children = new[]
            {
                new RefAction(RefActionKind.Reset, "Soft - keep all changes") { Sha = sha, ResetMode = GitResetMode.Soft },
                new RefAction(RefActionKind.Reset, "Mixed - keep working copy but reset index") { Sha = sha, ResetMode = GitResetMode.Mixed },
                new RefAction(RefActionKind.Reset, "Hard - discard all changes") { Sha = sha, ResetMode = GitResetMode.Hard, IsDestructive = true },
            },
        };
}
