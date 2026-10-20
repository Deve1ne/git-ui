using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using GitUi.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class RepositoryServiceTests
{
    [Fact]
    public async Task OpenAsync_ValidPath_ReturnsRepository()
    {
        var reader = Substitute.For<IGitRepositoryReader>();
        reader.IsValidRepository("/repo").Returns(true);
        reader.GetRepositoryAsync("/repo", Arg.Any<CancellationToken>())
            .Returns(new GitRepository("/repo", "main", false));

        var service = new RepositoryService(reader);

        var result = await service.OpenAsync("/repo");

        Assert.Equal("main", result.CurrentBranch);
    }

    [Fact]
    public async Task OpenAsync_InvalidPath_ThrowsGitRepositoryCorruptException()
    {
        var reader = Substitute.For<IGitRepositoryReader>();
        reader.IsValidRepository("/not-a-repo").Returns(false);

        var service = new RepositoryService(reader);

        await Assert.ThrowsAsync<GitRepositoryCorruptException>(() => service.OpenAsync("/not-a-repo"));
    }
}
