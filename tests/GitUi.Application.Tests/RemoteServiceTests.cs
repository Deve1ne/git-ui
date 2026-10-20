using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class RemoteServiceTests
{
    [Fact]
    public async Task FetchAsync_WithPrune_DelegatesToWriter()
    {
        var writer = Substitute.For<IGitRepositoryWriter>();
        var service = new RemoteService(writer);

        await service.FetchAsync("/repo", prune: true);

        await writer.Received(1).FetchAsync("/repo", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushAsync_WriterThrowsNetworkException_ExceptionPropagates()
    {
        var writer = Substitute.For<IGitRepositoryWriter>();
        writer.PushAsync("/repo", false, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new GitNetworkException("offline"));

        var service = new RemoteService(writer);

        await Assert.ThrowsAsync<GitNetworkException>(() => service.PushAsync("/repo", force: false));
    }
}
