using GitUi.Infrastructure.Processes;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class ShellCommandRunnerTests
{
    [Fact]
    public async Task RunAsync_GitVersionCommand_ReturnsOutputContainingGitVersion()
    {
        var runner = new ShellCommandRunner();

        var result = await runner.RunAsync("git --version", Directory.GetCurrentDirectory(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("git version", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }
}
