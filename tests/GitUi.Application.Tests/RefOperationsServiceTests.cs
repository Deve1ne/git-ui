using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class RefOperationsServiceTests
{
    private readonly IGitRepositoryReader _reader = Substitute.For<IGitRepositoryReader>();
    private readonly IGitRepositoryWriter _writer = Substitute.For<IGitRepositoryWriter>();
    private readonly RefOperationsService _service;

    public RefOperationsServiceTests()
    {
        _reader.GetRepositoryAsync("/repo", Arg.Any<CancellationToken>()).Returns(new GitRepository("/repo", "main", false, "head-sha"));
        _reader.GetBranchesAsync("/repo", Arg.Any<CancellationToken>()).Returns(Array.Empty<GitBranch>());
        var staging = new StagingService(_writer);
        var branches = new BranchService(_reader, _writer);
        _service = new RefOperationsService(_reader, _writer, new UndoService(_reader, _writer, staging, branches), new RemoteService(_writer));
    }

    [Fact]
    public async Task Merge_IntoOtherBranch_ChecksItOutFirst()
    {
        await _service.ExecuteAsync("/repo", new RefAction(RefActionKind.Merge, "") { Source = "main", Target = "release" });

        Received.InOrder(() =>
        {
            _writer.CheckoutAsync("/repo", "release", Arg.Any<CancellationToken>());
            _writer.MergeAsync("/repo", "main", GitMergeMode.NoFastForward, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Merge_IntoHead_DoesNotCheckout()
    {
        await _service.ExecuteAsync("/repo", new RefAction(RefActionKind.Merge, "") { Source = "feature", Target = "main" });

        await _writer.DidNotReceive().CheckoutAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _writer.Received(1).MergeAsync("/repo", "feature", GitMergeMode.NoFastForward, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FastForward_Head_UsesFastForwardOnlyMerge()
    {
        await _service.ExecuteAsync("/repo", new RefAction(RefActionKind.FastForward, "") { Target = "main", Source = "origin/main" });

        await _writer.Received(1).MergeAsync("/repo", "origin/main", GitMergeMode.FastForwardOnly, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FastForward_OtherBranch_MovesItWithoutCheckout()
    {
        await _service.ExecuteAsync("/repo", new RefAction(RefActionKind.FastForward, "") { Target = "old", Source = "main" });

        await _writer.Received(1).FastForwardBranchAsync("/repo", "old", "main", Arg.Any<CancellationToken>());
        await _writer.DidNotReceive().CheckoutAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rebase_OfHead_RebasesCurrentBranch()
    {
        await _service.ExecuteAsync("/repo", new RefAction(RefActionKind.Rebase, "") { Source = "main", Target = "feature" });

        await _writer.Received(1).RebaseAsync("/repo", "feature", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateBranchHere_WithoutName_Throws()
    {
        var action = new RefAction(RefActionKind.CreateBranchHere, "") { Sha = "abc" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExecuteAsync("/repo", action, input: " "));
    }

    [Fact]
    public async Task Push_BranchWithoutUpstream_SetsUpstream()
    {
        await _service.ExecuteAsync("/repo", new RefAction(RefActionKind.Push, "") { Target = "feature", Remote = "origin", RemoteBranch = "feature" });

        await _writer.Received(1).PushBranchAsync("/repo", "origin", "feature", "feature", true, Arg.Any<CancellationToken>());
    }
}
