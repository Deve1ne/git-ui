using GitUi.Infrastructure.Processes;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_GitVersion_ReturnsExitCodeZeroAndOutput()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync("git", new[] { "--version" }, Directory.GetCurrentDirectory(), TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("git version", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_UnknownExecutable_ReturnsNonZeroExitCodeInsteadOfThrowing()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync("definitely-not-a-real-command-xyz", Array.Empty<string>(), Directory.GetCurrentDirectory(), TimeSpan.FromSeconds(5));

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ProcessExceedsTimeout_ReturnsTimeoutExitCodeInsteadOfThrowing()
    {
        var runner = new ProcessRunner();
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("powershell", new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 5" })
            : ("sleep", new[] { "5" });

        var result = await runner.RunAsync(fileName, arguments, Directory.GetCurrentDirectory(), TimeSpan.FromMilliseconds(200));

        Assert.Equal(-2, result.ExitCode);
    }
}
