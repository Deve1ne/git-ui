using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class StashServiceTests
{
    [Fact]
    public async Task DropAsync_DelegatesToWriterWithIndex()
    {
        var writer = Substitute.For<IGitRepositoryWriter>();
        var service = new StashService(writer);

        await service.DropAsync("/repo", 2);

        await writer.Received(1).StashDropAsync("/repo", 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_DelegatesToWriterWithMessage()
    {
        var writer = Substitute.For<IGitRepositoryWriter>();
        var service = new StashService(writer);

        await service.SaveAsync("/repo", "wip");

        await writer.Received(1).StashSaveAsync("/repo", "wip", Arg.Any<CancellationToken>());
    }
}
