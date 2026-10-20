using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using Xunit;

namespace GitUi.Application.Tests;

public class RefMenuPlannerTests
{
    private static readonly HeadInfo HeadOnMain = new("main", "head-sha");
    private static readonly string[] Remotes = { "origin" };

    private static IReadOnlyList<RefAction> Flatten(IReadOnlyList<IReadOnlyList<RefAction>> groups) =>
        groups.SelectMany(g => g).SelectMany(a => a.Children.Count > 0 ? a.Children.Prepend(a) : new[] { a }).ToList();

    private static IReadOnlyList<string> Labels(IReadOnlyList<IReadOnlyList<RefAction>> groups) =>
        Flatten(groups).Select(a => a.Label).ToList();

    [Fact]
    public void ContextMenu_DivergedLocalBranch_OffersMergeIntoHeadAndRebase()
    {
        var feature = new RefTarget(RefKind.LocalBranch, "feature", "feature-sha");

        var menu = RefMenuPlanner.BuildContextMenu(feature, HeadOnMain, GitAncestry.Diverged, Remotes, new[] { "main", "feature" }, inGraph: false);

        var labels = Labels(menu);
        Assert.Contains("Merge feature into main", labels);
        Assert.Contains("Rebase main onto feature", labels);
        Assert.DoesNotContain(labels, l => l.StartsWith("Fast-forward", StringComparison.Ordinal));
        Assert.Contains("Checkout feature", labels);
        Assert.Contains("Delete feature", labels);
    }

    [Theory]
    [InlineData(RefKind.LocalBranch, "feature", "local")]
    [InlineData(RefKind.RemoteBranch, "origin/feature", "remote")]
    public void ContextMenu_Branches_OfferAPullRequest(RefKind kind, string name, string source)
    {
        var menu = RefMenuPlanner.BuildContextMenu(new RefTarget(kind, name, "sha"), HeadOnMain, GitAncestry.Diverged, Remotes, new[] { "main" }, inGraph: false);

        var action = Assert.Single(Flatten(menu), a => a.Kind == RefActionKind.CreatePullRequest);
        Assert.Equal(name, action.Target);
        Assert.Equal(source, action.Source);
    }

    [Fact]
    public void ContextMenu_InTheGraph_OffersBisectMarks_NotInTheRefPanel()
    {
        var commit = RefTarget.Commit("abc123");

        var graph = Flatten(RefMenuPlanner.BuildContextMenu(commit, HeadOnMain, null, Remotes, new[] { "main" }, inGraph: true));
        var panel = Flatten(RefMenuPlanner.BuildContextMenu(new RefTarget(RefKind.LocalBranch, "feature", "sha"), HeadOnMain, GitAncestry.Diverged, Remotes, new[] { "main" }, inGraph: false));

        Assert.Contains(graph, a => a.Kind == RefActionKind.BisectBad && a.Sha == "abc123");
        Assert.Contains(graph, a => a.Kind == RefActionKind.BisectGood && a.Sha == "abc123");
        Assert.Contains(graph, a => a.Kind == RefActionKind.BisectSkip);
        Assert.DoesNotContain(panel, a => a.Kind is RefActionKind.BisectBad or RefActionKind.BisectGood);
    }

    [Fact]
    public void ContextMenu_NoRemoteOrTag_NoPullRequest()
    {
        var local = RefMenuPlanner.BuildContextMenu(new RefTarget(RefKind.LocalBranch, "feature", "sha"), HeadOnMain, GitAncestry.Diverged, Array.Empty<string>(), new[] { "main" }, inGraph: false);
        var tag = RefMenuPlanner.BuildContextMenu(new RefTarget(RefKind.Tag, "v1", "sha"), HeadOnMain, GitAncestry.Diverged, Remotes, new[] { "main" }, inGraph: false);

        Assert.DoesNotContain(Flatten(local), a => a.Kind == RefActionKind.CreatePullRequest);
        Assert.DoesNotContain(Flatten(tag), a => a.Kind == RefActionKind.CreatePullRequest);
    }

    [Fact]
    public void ContextMenu_BranchBehindHead_OffersFastForwardThatBranchToHead()
    {
        var old = new RefTarget(RefKind.LocalBranch, "old", "old-sha");

        var menu = RefMenuPlanner.BuildContextMenu(old, HeadOnMain, GitAncestry.SecondIsAncestor, Remotes, new[] { "main", "old" }, inGraph: false);

        var fastForward = Assert.Single(Flatten(menu), a => a.Kind == RefActionKind.FastForward);
        Assert.Equal("Fast-forward old to main", fastForward.Label);
        Assert.Equal("old", fastForward.Target);
        Assert.Equal("main", fastForward.Source);
        Assert.DoesNotContain(Flatten(menu), a => a.Kind == RefActionKind.Rebase);
    }

    [Fact]
    public void ContextMenu_RemoteBranchAheadOfHead_OffersFastForwardHead()
    {
        var remote = new RefTarget(RefKind.RemoteBranch, "origin/main", "remote-sha");

        var menu = RefMenuPlanner.BuildContextMenu(remote, HeadOnMain, GitAncestry.FirstIsAncestor, Remotes, new[] { "main" }, inGraph: false);

        var labels = Labels(menu);
        Assert.Contains("Fast-forward main to origin/main", labels);
        Assert.Contains("Merge origin/main into main", labels);
        // A local twin exists: checkout switches to it instead of creating a tracking branch.
        Assert.Contains(Flatten(menu), a => a.Kind == RefActionKind.Checkout && a.Target == "main");
        Assert.Contains(Flatten(menu), a => a.Kind == RefActionKind.DeleteRemote && a.Remote == "origin" && a.RemoteBranch == "main");
    }

    [Fact]
    public void ContextMenu_HeadBranch_HasPullButNoCheckoutDeleteOrMerge()
    {
        var main = new RefTarget(RefKind.LocalBranch, "main", "head-sha", "origin/main");

        var menu = RefMenuPlanner.BuildContextMenu(main, HeadOnMain, GitAncestry.Same, Remotes, new[] { "main" }, inGraph: false);

        var kinds = Flatten(menu).Select(a => a.Kind).ToList();
        Assert.Contains(RefActionKind.Pull, kinds);
        Assert.Contains(RefActionKind.Push, kinds);
        Assert.DoesNotContain(RefActionKind.Checkout, kinds);
        Assert.DoesNotContain(RefActionKind.Delete, kinds);
        Assert.DoesNotContain(RefActionKind.Merge, kinds);
        Assert.DoesNotContain(RefActionKind.Reset, kinds);
    }

    [Fact]
    public void ContextMenu_OtherBranch_CanBecomeConflictDetectionTarget_ButNotHead()
    {
        var feature = new RefTarget(RefKind.LocalBranch, "feature", "feature-sha");
        var main = new RefTarget(RefKind.LocalBranch, "main", "head-sha");

        var featureMenu = RefMenuPlanner.BuildContextMenu(feature, HeadOnMain, GitAncestry.Diverged, Remotes, new[] { "main" }, inGraph: false);
        var mainMenu = RefMenuPlanner.BuildContextMenu(main, HeadOnMain, GitAncestry.Same, Remotes, new[] { "main" }, inGraph: false);

        Assert.Contains(Flatten(featureMenu), a => a.Kind == RefActionKind.SetConflictTarget && a.Target == "feature");
        Assert.DoesNotContain(Flatten(mainMenu), a => a.Kind == RefActionKind.SetConflictTarget);
    }

    [Fact]
    public void ContextMenu_HardReset_IsMarkedDestructive()
    {
        var commit = RefTarget.Commit("other-sha");

        var menu = RefMenuPlanner.BuildContextMenu(commit, HeadOnMain, null, Remotes, new[] { "main" }, inGraph: true);

        var hard = Assert.Single(Flatten(menu), a => a.Kind == RefActionKind.Reset && a.ResetMode == GitResetMode.Hard);
        Assert.True(hard.IsDestructive);
        Assert.Contains("Rebase main onto this commit", Labels(menu));
        Assert.Contains("Checkout this commit", Labels(menu));
    }

    [Fact]
    public void DragDrop_DivergedBranchOntoLocalBranch_OffersMergeAndRebase()
    {
        var feature = new RefTarget(RefKind.LocalBranch, "feature", "feature-sha");
        var main = new RefTarget(RefKind.LocalBranch, "main", "head-sha", "origin/main");

        var menu = RefMenuPlanner.BuildDragDropMenu(feature, main, HeadOnMain, GitAncestry.Diverged, Remotes);

        var labels = Labels(menu);
        Assert.Equal("Merge feature into main", labels[0]);
        Assert.Contains("Rebase feature onto main", labels);
        Assert.Contains("Push feature to origin/main", labels);
    }

    [Fact]
    public void DragDrop_HeadOntoAncestor_OffersFastForwardOfTheTargetAndReset()
    {
        var main = new RefTarget(RefKind.LocalBranch, "main", "head-sha");
        var old = new RefTarget(RefKind.LocalBranch, "old", "old-sha");

        var menu = RefMenuPlanner.BuildDragDropMenu(main, old, HeadOnMain, GitAncestry.SecondIsAncestor, Remotes);

        var labels = Labels(menu);
        Assert.Contains("Fast-forward old to main", labels);
        Assert.Contains("Merge main into old", labels);
        Assert.Contains("Reset main to this commit", labels);
        Assert.DoesNotContain(labels, l => l.StartsWith("Rebase", StringComparison.Ordinal));
    }

    [Fact]
    public void DragDrop_LocalBranchOntoRemoteBranch_OffersRebaseAndPushButNoMerge()
    {
        var feature = new RefTarget(RefKind.LocalBranch, "feature", "feature-sha");
        var remote = new RefTarget(RefKind.RemoteBranch, "origin/feature", "remote-sha");

        var menu = RefMenuPlanner.BuildDragDropMenu(feature, remote, HeadOnMain, GitAncestry.Diverged, Remotes);

        var kinds = Flatten(menu).Select(a => a.Kind).ToList();
        Assert.DoesNotContain(RefActionKind.Merge, kinds);
        Assert.Contains(RefActionKind.Rebase, kinds);
        var push = Assert.Single(Flatten(menu), a => a.Kind == RefActionKind.Push);
        Assert.Equal(("origin", "feature"), (push.Remote, push.RemoteBranch));
    }
}
