using GitUi.Application.Abstractions;
using GitUi.Application.DTOs;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using GitUi.Domain.Services;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class CommitGraphServiceTests
{
    [Fact]
    public async Task GetGraphAsync_ReturnsLaidOutNodesForEachCommit()
    {
        var reader = Substitute.For<IGitRepositoryReader>();
        var commits = new[]
        {
            new GitCommit("c2", "second", "author", DateTimeOffset.UnixEpoch, new[] { "c1" }),
            new GitCommit("c1", "first", "author", DateTimeOffset.UnixEpoch, Array.Empty<string>()),
        };
        reader.GetCommitsAsync("/repo", Arg.Any<CancellationToken>()).Returns(commits);
        reader.GetBranchesAsync("/repo", Arg.Any<CancellationToken>()).Returns(Array.Empty<GitBranch>());
        reader.GetTagsAsync("/repo", Arg.Any<CancellationToken>()).Returns(Array.Empty<GitTag>());

        var service = new CommitGraphService(reader, new CommitGraphLayoutBuilder());

        var graph = await service.GetGraphAsync("/repo");

        Assert.Equal(2, graph.Count);
        Assert.Equal("c2", graph[0].Node.Commit.Sha);
        Assert.Empty(graph[0].Labels);
    }

    [Fact]
    public async Task GetGraphAsync_LocalBranchTipMatchesCommit_AttachesLocalBranchLabel()
    {
        var reader = Substitute.For<IGitRepositoryReader>();
        var commits = new[] { new GitCommit("c1", "first", "author", DateTimeOffset.UnixEpoch, Array.Empty<string>()) };
        reader.GetCommitsAsync("/repo", Arg.Any<CancellationToken>()).Returns(commits);
        reader.GetBranchesAsync("/repo", Arg.Any<CancellationToken>())
            .Returns(new[] { new GitBranch("main", true, false, null, "c1", true) });
        reader.GetTagsAsync("/repo", Arg.Any<CancellationToken>()).Returns(Array.Empty<GitTag>());

        var service = new CommitGraphService(reader, new CommitGraphLayoutBuilder());

        var graph = await service.GetGraphAsync("/repo");

        var label = Assert.Single(graph[0].Labels);
        Assert.Equal("main", label.Name);
        Assert.Equal(RefLabelKind.LocalBranch, label.Kind);
        Assert.True(label.IsHead);
    }

    [Fact]
    public async Task GetGraphAsync_RemoteBranchTipMatchesCommit_AttachesRemoteBranchLabel()
    {
        var reader = Substitute.For<IGitRepositoryReader>();
        var commits = new[] { new GitCommit("c1", "first", "author", DateTimeOffset.UnixEpoch, Array.Empty<string>()) };
        reader.GetCommitsAsync("/repo", Arg.Any<CancellationToken>()).Returns(commits);
        reader.GetBranchesAsync("/repo", Arg.Any<CancellationToken>())
            .Returns(new[] { new GitBranch("origin/main", false, true, null, "c1", false) });
        reader.GetTagsAsync("/repo", Arg.Any<CancellationToken>()).Returns(Array.Empty<GitTag>());

        var service = new CommitGraphService(reader, new CommitGraphLayoutBuilder());

        var graph = await service.GetGraphAsync("/repo");

        var label = Assert.Single(graph[0].Labels);
        Assert.Equal(RefLabelKind.RemoteBranch, label.Kind);
        Assert.False(label.IsHead);
    }

    [Fact]
    public async Task GetGraphAsync_TagTargetsCommit_AttachesTagLabel()
    {
        var reader = Substitute.For<IGitRepositoryReader>();
        var commits = new[] { new GitCommit("c1", "first", "author", DateTimeOffset.UnixEpoch, Array.Empty<string>()) };
        reader.GetCommitsAsync("/repo", Arg.Any<CancellationToken>()).Returns(commits);
        reader.GetBranchesAsync("/repo", Arg.Any<CancellationToken>()).Returns(Array.Empty<GitBranch>());
        reader.GetTagsAsync("/repo", Arg.Any<CancellationToken>())
            .Returns(new[] { new GitTag("v1.0.0", "c1") });

        var service = new CommitGraphService(reader, new CommitGraphLayoutBuilder());

        var graph = await service.GetGraphAsync("/repo");

        var label = Assert.Single(graph[0].Labels);
        Assert.Equal("v1.0.0", label.Name);
        Assert.Equal(RefLabelKind.Tag, label.Kind);
    }
}
