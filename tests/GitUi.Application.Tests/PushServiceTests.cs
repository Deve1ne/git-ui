using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using GitUi.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class PushServiceTests
{
    private readonly IGitRepositoryReader _reader = Substitute.For<IGitRepositoryReader>();
    private readonly IGitRepositoryWriter _writer = Substitute.For<IGitRepositoryWriter>();
    private readonly PushService _service;

    public PushServiceTests()
    {
        _service = new PushService(_reader, _writer);
        _reader.GetRemotesAsync("/repo", Arg.Any<CancellationToken>()).Returns(new[] { new GitRemote("origin", "u", "u"), new GitRemote("fork", "f", "f") });
    }

    private void HeadIs(string name, string? upstream) =>
        _reader.GetBranchesAsync("/repo", Arg.Any<CancellationToken>()).Returns(new[] { new GitBranch(name, true, false, upstream, "sha", true) });

    [Fact]
    public async Task PushAsync_NoUpstream_AsksInsteadOfPushing()
    {
        HeadIs("feature/x", upstream: null);

        Assert.Equal(PushOutcome.NeedsUpstream, await _service.PushAsync("/repo"));
        await _writer.DidNotReceiveWithAnyArgs().PushAsync(default!, default, default);
        Assert.Equal("origin/feature/x", await _service.SuggestUpstreamAsync("/repo"));
    }

    [Fact]
    public async Task PushAsync_RejectedByRemote_ReportsRejection()
    {
        HeadIs("main", "origin/main");
        _writer.PushAsync("/repo", false, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new GitOperationException("push failed", ErrorCode.Unknown, " ! [rejected]        main -> main (fetch first)"));

        Assert.Equal(PushOutcome.Rejected, await _service.PushAsync("/repo"));
    }

    [Fact]
    public async Task PushAsync_OtherFailure_Propagates()
    {
        HeadIs("main", "origin/main");
        _writer.PushAsync("/repo", false, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new GitNetworkException("offline"));

        await Assert.ThrowsAsync<GitNetworkException>(() => _service.PushAsync("/repo"));
    }

    [Fact]
    public async Task PushWithUpstreamAsync_SplitsRemoteFromBranchPath()
    {
        HeadIs("feature/x", upstream: null);

        await _service.PushWithUpstreamAsync("/repo", "fork/feature/x");

        await _writer.Received(1).PushBranchAsync("/repo", "fork", "feature/x", "feature/x", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushWithUpstreamAsync_UnknownRemote_Refuses()
    {
        HeadIs("main", upstream: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PushWithUpstreamAsync("/repo", "nope/main"));
    }
}
