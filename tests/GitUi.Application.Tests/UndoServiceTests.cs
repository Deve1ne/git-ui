using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class UndoServiceTests
{
    private readonly IGitRepositoryReader _reader = Substitute.For<IGitRepositoryReader>();
    private readonly IGitRepositoryWriter _writer = Substitute.For<IGitRepositoryWriter>();
    private readonly UndoService _service;

    public UndoServiceTests()
    {
        _service = new UndoService(_reader, _writer, new StagingService(_writer), new BranchService(_reader, _writer));
    }

    private void HeadIs(params string[] shas)
    {
        var repositories = shas.Select(sha => new GitRepository("/repo", "main", false, sha)).ToArray();
        _reader.GetRepositoryAsync("/repo", Arg.Any<CancellationToken>()).Returns(repositories[0], repositories[1..]);
    }

    [Fact]
    public async Task UndoAsync_AfterCommit_SoftResetsToThePreviousHead()
    {
        HeadIs("parent", "child", "child");
        await _service.CommitAsync("/repo", "message");

        var undone = await _service.UndoAsync("/repo");

        Assert.Equal(UndoableOperationKind.Commit, undone!.Kind);
        await _writer.Received(1).ResetAsync("/repo", "parent", GitResetMode.Soft, Arg.Any<CancellationToken>());
        Assert.Null(_service.PeekUndo("/repo"));
        Assert.NotNull(_service.PeekRedo("/repo"));
    }

    [Fact]
    public async Task UndoAsync_AfterAmend_SoftResetsToTheReplacedCommit()
    {
        HeadIs("original", "amended", "amended");
        await _service.CommitAsync("/repo", "reworded", new GitCommitOptions(Amend: true));

        Assert.StartsWith("amend", _service.PeekUndo("/repo")!.Description);
        await _service.UndoAsync("/repo");

        await _writer.Received(1).ResetAsync("/repo", "original", GitResetMode.Soft, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardAll_UndoThenRedo_RestoresThenDiscardsWithTheNewStash()
    {
        _writer.DiscardAllAsync("/repo", Arg.Any<CancellationToken>()).Returns("stash-1", "stash-2");
        Assert.True(await _service.DiscardAllAsync("/repo"));

        await _service.UndoAsync("/repo");
        await _writer.Received(1).RestoreDiscardedAsync("/repo", "stash-1", Arg.Any<CancellationToken>());

        await _service.RedoAsync("/repo");
        await _service.UndoAsync("/repo");
        await _writer.Received(1).RestoreDiscardedAsync("/repo", "stash-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardAll_NothingToDiscard_RecordsNothing()
    {
        _writer.DiscardAllAsync("/repo", Arg.Any<CancellationToken>()).Returns((string?)null);

        Assert.False(await _service.DiscardAllAsync("/repo"));
        Assert.Null(_service.PeekUndo("/repo"));
    }

    [Fact]
    public async Task UndoAsync_HeadMovedSinceCommit_ThrowsAndKeepsJournal()
    {
        HeadIs("parent", "child", "someone-else");
        await _service.CommitAsync("/repo", "message");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UndoAsync("/repo"));
        await _writer.DidNotReceive().ResetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GitResetMode>(), Arg.Any<CancellationToken>());
        Assert.NotNull(_service.PeekUndo("/repo"));
    }

    [Fact]
    public async Task RedoAsync_AfterUndoneCommit_SoftResetsBackToTheCommit()
    {
        HeadIs("parent", "child", "child", "parent");
        await _service.CommitAsync("/repo", "message");
        await _service.UndoAsync("/repo");

        await _service.RedoAsync("/repo");

        await _writer.Received(1).ResetAsync("/repo", "child", GitResetMode.Soft, Arg.Any<CancellationToken>());
        Assert.NotNull(_service.PeekUndo("/repo"));
    }

    [Fact]
    public async Task UndoAsync_AfterCheckout_SwitchesBackToThePreviousBranch()
    {
        _reader.GetRepositoryAsync("/repo", Arg.Any<CancellationToken>()).Returns(new GitRepository("/repo", "main", false, "sha"));
        await _service.CheckoutAsync("/repo", "feature");

        await _service.UndoAsync("/repo");

        await _writer.Received(1).CheckoutAsync("/repo", "main", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UndoAsync_AfterDeleteBranch_RecreatesItAtItsOldTip()
    {
        _reader.GetRepositoryAsync("/repo", Arg.Any<CancellationToken>()).Returns(new GitRepository("/repo", "main", false, "sha"));
        _reader.GetBranchesAsync("/repo", Arg.Any<CancellationToken>())
            .Returns(new[] { new GitBranch("old", true, false, null, "tip-sha", false) });
        await _service.DeleteBranchAsync("/repo", "old", force: false);

        await _service.UndoAsync("/repo");

        await _writer.Received(1).CreateBranchAsync("/repo", "old", "tip-sha", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewOperation_AfterUndo_ClearsRedo()
    {
        _reader.GetRepositoryAsync("/repo", Arg.Any<CancellationToken>()).Returns(new GitRepository("/repo", "main", false, "sha"));
        await _service.CheckoutAsync("/repo", "feature");
        await _service.UndoAsync("/repo");

        await _service.CheckoutAsync("/repo", "other");

        Assert.Null(_service.PeekRedo("/repo"));
    }
}
