using System.Runtime.InteropServices;

namespace GitUi.Infrastructure.Processes;

public sealed class ShellCommandRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private readonly IProcessRunner _processRunner = new ProcessRunner();

    public Task<ProcessResult> RunAsync(string commandLine, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var (shell, shellArgPrefix) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd.exe", "/c")
            : ("/bin/sh", "-c");

        return _processRunner.RunAsync(shell, new[] { shellArgPrefix, commandLine }, workingDirectory, Timeout, cancellationToken);
    }
}
