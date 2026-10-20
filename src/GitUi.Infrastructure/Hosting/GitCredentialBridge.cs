using System.Diagnostics;
using System.Text;
using GitUi.Application.Hosting;
using Microsoft.Extensions.Logging;

namespace GitUi.Infrastructure.Hosting;

/// <summary>
/// Talks to git's credential helper (Git Credential Manager) with <c>git credential fill / approve</c>:
/// the account sign-in reuses whatever the user already signed in with for git, or GCM's browser
/// sign-in, without the app having OAuth applications of its own.
/// </summary>
public sealed class GitCredentialBridge : IGitCredentialBridge
{
    private static readonly TimeSpan InteractiveTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SilentTimeout = TimeSpan.FromSeconds(30);
    private readonly ILogger<GitCredentialBridge> _logger;
    private readonly IReadOnlyDictionary<string, string> _environment;

    public GitCredentialBridge(ILogger<GitCredentialBridge> logger)
        : this(logger, new Dictionary<string, string>())
    {
    }

    /// <param name="environment">Extra variables for git (tests point GIT_CONFIG_GLOBAL at a throwaway helper).</param>
    public GitCredentialBridge(ILogger<GitCredentialBridge> logger, IReadOnlyDictionary<string, string> environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async Task<HostingCredential?> FillAsync(Uri url, bool interactive, CancellationToken cancellationToken = default)
    {
        var (exitCode, output, error) = await RunAsync("fill", Describe(url, null), interactive, _environment, cancellationToken);
        if (exitCode != 0)
        {
            _logger.LogWarning("git credential fill for {Host} failed ({ExitCode}): {Error}", url.Host, exitCode, error.Trim());
            return null;
        }

        var values = Parse(output);
        return values.TryGetValue("password", out var password) && password.Length > 0
            ? new HostingCredential(password, values.GetValueOrDefault("username"))
            : null;
    }

    public async Task ApproveAsync(Uri url, HostingCredential credential, CancellationToken cancellationToken = default)
    {
        var (exitCode, _, error) = await RunAsync("approve", Describe(url, credential), interactive: false, _environment, cancellationToken);
        if (exitCode != 0)
        {
            _logger.LogWarning("git credential approve for {Host} failed ({ExitCode}): {Error}", url.Host, exitCode, error.Trim());
        }
    }

    /// <summary>The key=value lines git credential reads, ended by a blank line.</summary>
    private static string Describe(Uri url, HostingCredential? credential)
    {
        var builder = new StringBuilder();
        builder.Append("protocol=").Append(url.Scheme).Append('\n');
        builder.Append("host=").Append(url.Authority).Append('\n');
        var path = url.AbsolutePath.Trim('/');
        if (path.Length > 0)
        {
            builder.Append("path=").Append(path).Append('\n');
        }

        if (credential is not null)
        {
            if (credential.Username is { } username)
            {
                builder.Append("username=").Append(username).Append('\n');
            }

            builder.Append("password=").Append(credential.Secret).Append('\n');
        }

        return builder.Append('\n').ToString();
    }

    private static Dictionary<string, string> Parse(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                values[line[..separator]] = line[(separator + 1)..].TrimEnd('\r');
            }
        }

        return values;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string action,
        string input,
        bool interactive,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };

        // Azure DevOps credentials are per organization: GCM needs the URL path for them.
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("credential.https://dev.azure.com.useHttpPath=true");
        startInfo.ArgumentList.Add("credential");
        startInfo.ArgumentList.Add(action);

        // Never wait on a terminal prompt nobody sees; GCM's own windows / browser are fine when interactive.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (!interactive)
        {
            startInfo.Environment["GCM_INTERACTIVE"] = "never";
        }

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }

        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = new CancellationTokenSource(interactive ? InteractiveTimeout : SilentTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return (-2, string.Empty, "git credential timed out.");
        }

        return (process.ExitCode, await output, await error);
    }
}
