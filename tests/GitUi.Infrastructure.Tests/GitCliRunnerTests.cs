using GitUi.Domain.Exceptions;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class GitCliRunnerTests
{
    [Fact]
    public async Task RunAsync_SuccessfulCommand_ReturnsStandardOutput()
    {
        var processRunner = Substitute.For<IProcessRunner>();
        processRunner.RunAsync("git", Arg.Any<IReadOnlyList<string>>(), "/repo", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "output\n", string.Empty));

        var runner = new GitCliRunner(processRunner, NullLogger<GitCliRunner>.Instance);

        var output = await runner.RunAsync("/repo", new[] { "status" });

        Assert.Equal("output", output.Trim());
    }

    [Fact]
    public async Task RunAsync_FailedCommand_ThrowsClassifiedException()
    {
        var processRunner = Substitute.For<IProcessRunner>();
        processRunner.RunAsync("git", Arg.Any<IReadOnlyList<string>>(), "/repo", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(128, string.Empty, "fatal: not a git repository"));

        var runner = new GitCliRunner(processRunner, NullLogger<GitCliRunner>.Instance);

        await Assert.ThrowsAsync<GitRepositoryCorruptException>(() => runner.RunAsync("/repo", new[] { "status" }));
    }
}
