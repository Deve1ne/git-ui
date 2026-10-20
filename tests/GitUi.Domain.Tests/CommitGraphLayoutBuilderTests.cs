using GitUi.Domain.Entities;
using GitUi.Domain.Services;
using Xunit;

namespace GitUi.Domain.Tests;

public class CommitGraphLayoutBuilderTests
{
    private static GitCommit Commit(string sha, params string[] parents) =>
        new(sha, $"message {sha}", "author", DateTimeOffset.UnixEpoch, parents);

    [Fact]
    public void Build_LinearHistory_AllCommitsShareLaneZero()
    {
        var commits = new[]
        {
            Commit("c3", "c2"),
            Commit("c2", "c1"),
            Commit("c1"),
        };

        var nodes = new CommitGraphLayoutBuilder().Build(commits);

        Assert.All(nodes, node => Assert.Equal(0, node.Lane));
    }

    [Fact]
    public void Build_StashOnHead_SitsBesideTheBranchWhichKeepsLaneZero()
    {
        var commits = new[]
        {
            new GitCommit("stash", "On main: wip", "author", DateTimeOffset.UnixEpoch, new[] { "c2" }, IsStash: true),
            Commit("c2", "c1"),
            Commit("c1"),
        };

        var nodes = new CommitGraphLayoutBuilder().Build(commits);

        Assert.Equal(1, nodes[0].Lane);
        Assert.Equal(0, nodes[1].Lane);
        Assert.Equal(0, nodes[2].Lane);
    }

    [Fact]
    public void Build_LaneFreedByStashParent_IsReusedLater()
    {
        var commits = new[]
        {
            new GitCommit("stash", "On main: wip", "author", DateTimeOffset.UnixEpoch, new[] { "c2" }, IsStash: true),
            Commit("c2", "c1"),
            Commit("side", "c1"),
            Commit("c1"),
        };

        var nodes = new CommitGraphLayoutBuilder().Build(commits);

        // The stash's lane 1 was released by c2, so the "side" branch tip takes it.
        Assert.Equal(1, nodes[2].Lane);
    }

    [Fact]
    public void Build_MergeCommit_OpensSecondLaneForSecondParent()
    {
        var commits = new[]
        {
            Commit("merge", "b1", "m1"),
            Commit("b1", "root"),
            Commit("m1", "root"),
            Commit("root"),
        };

        var nodes = new CommitGraphLayoutBuilder().Build(commits);

        var mergeNode = nodes[0];
        Assert.Equal(0, mergeNode.Lane);
        Assert.Equal(new[] { 0, 1 }, mergeNode.ParentLanes);

        var m1Node = nodes[2];
        Assert.Equal(1, m1Node.Lane);
    }

    [Fact]
    public void Build_RootCommit_HasNoAdditionalParentLanes()
    {
        var commits = new[] { Commit("root") };

        var nodes = new CommitGraphLayoutBuilder().Build(commits);

        Assert.Single(nodes);
        Assert.Equal(new[] { 0 }, nodes[0].ParentLanes);
    }

    [Fact]
    public void Build_ManySequentialMergedFeatureBranches_ReusesLanesInsteadOfLeaking()
    {
        // Newest-first, mirroring 3 short-lived feature branches each merged back into main
        // one after another — a common real-world history shape. Each feature branch's lane
        // should be freed and reused once its merge lands, not leaked forever.
        var commits = new[]
        {
            Commit("merge3", "main3", "feature3"),
            Commit("feature3", "main3"),
            Commit("main3", "merge2"),
            Commit("merge2", "main2", "feature2"),
            Commit("feature2", "main2"),
            Commit("main2", "merge1"),
            Commit("merge1", "main1", "feature1"),
            Commit("feature1", "main1"),
            Commit("main1", "root"),
            Commit("root"),
        };

        var nodes = new CommitGraphLayoutBuilder().Build(commits);

        var maxLane = nodes.Max(n => n.Lane);
        Assert.True(maxLane <= 1, $"Expected at most 2 concurrent lanes (main + one feature at a time), but max lane was {maxLane} — feature branch lanes are leaking instead of being reused.");
    }
}
