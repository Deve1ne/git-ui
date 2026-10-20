using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class BranchServiceTests
{
    [Fact]
    public async Task DeleteBranchAsync_CurrentBranch_ThrowsAndDoesNotCallWriter()
    {
        var reader = Substitute.For<IGitRepositoryReader>();
        var writer = Substitute.For<IGitRepositoryWriter>();
        reader.GetRepositoryAsync("/repo", Arg.Any<CancellationToken>())
            .Returns(new GitRepository("/repo", "main", false));

        var service = new BranchService(reader, writer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteBranchAsync("/repo", "main", force: false));
        await writer.DidNotReceive().DeleteBranchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteBranchAsync_OtherBranch_CallsWriter()
    {
        var reader = Substitute.For<IGitRepositoryReader>();
        var writer = Substitute.For<IGitRepositoryWriter>();
        reader.GetRepositoryAsync("/repo", Arg.Any<CancellationToken>())
            .Returns(new GitRepository("/repo", "main", false));

        var service = new BranchService(reader, writer);

        await service.DeleteBranchAsync("/repo", "feature", force: false);

        await writer.Received(1).DeleteBranchAsync("/repo", "feature", false, Arg.Any<CancellationToken>());
    }
}
