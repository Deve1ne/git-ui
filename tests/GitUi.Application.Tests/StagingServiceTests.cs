using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class StagingServiceTests
{
    [Fact]
    public async Task CommitAsync_EmptyMessage_ThrowsAndDoesNotCallWriter()
    {
        var writer = Substitute.For<IGitRepositoryWriter>();
        var service = new StagingService(writer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync("/repo", "   "));
        await writer.DidNotReceive().CommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAsync_ValidMessage_CallsWriter()
    {
        var writer = Substitute.For<IGitRepositoryWriter>();
        var service = new StagingService(writer);

        await service.CommitAsync("/repo", "fix bug");

        await writer.Received(1).CommitAsync("/repo", "fix bug", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAsync_WithOptions_PassesThemToWriter()
    {
        var writer = Substitute.For<IGitRepositoryWriter>();
        var service = new StagingService(writer);
        var options = new GitCommitOptions(Amend: true, SkipHooks: true);

        await service.CommitAsync("/repo", "reword", options);

        await writer.Received(1).CommitAsync("/repo", "reword", options, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardAsync_SplitsUntrackedFromTrackedAndSkipsStaged()
    {
        var writer = Substitute.For<IGitRepositoryWriter>();
        var service = new StagingService(writer);
        var changes = new[]
        {
            new FileChange("new.txt", FileChangeType.Added, IsStaged: false),
            new FileChange("edit.txt", FileChangeType.Modified, IsStaged: false),
            new FileChange("gone.txt", FileChangeType.Deleted, IsStaged: false),
            new FileChange("staged.txt", FileChangeType.Modified, IsStaged: true),
        };

        await service.DiscardAsync("/repo", changes);

        await writer.Received(1).DiscardWorkingChangesAsync(
            "/repo",
            Arg.Is<IReadOnlyCollection<string>>(t => t.SequenceEqual(new[] { "edit.txt", "gone.txt" })),
            Arg.Is<IReadOnlyCollection<string>>(u => u.SequenceEqual(new[] { "new.txt" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardAsync_OnlyStagedChanges_DoesNothing()
    {
        var writer = Substitute.For<IGitRepositoryWriter>();
        var service = new StagingService(writer);

        await service.DiscardAsync("/repo", new[] { new FileChange("a.txt", FileChangeType.Modified, IsStaged: true) });

        await writer.DidNotReceiveWithAnyArgs().DiscardWorkingChangesAsync(default!, default!, default!, default);
    }
}
