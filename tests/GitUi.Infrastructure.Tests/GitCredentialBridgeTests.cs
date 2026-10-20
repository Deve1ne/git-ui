using GitUi.Application.Hosting;
using GitUi.Infrastructure.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

/// <summary>
/// The bridge against real git with a throwaway credential helper: GIT_CONFIG_GLOBAL points at a
/// temporary config and GIT_CONFIG_NOSYSTEM keeps the machine's Credential Manager out of it.
/// </summary>
public sealed class GitCredentialBridgeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gitui-credential-" + Guid.NewGuid().ToString("N"));
    private readonly string _stored;
    private readonly GitCredentialBridge _bridge;

    public GitCredentialBridgeTests()
    {
        Directory.CreateDirectory(_directory);
        _stored = Path.Combine(_directory, "stored.txt");
        var config = Path.Combine(_directory, "gitconfig");
        // "get" answers a fixed credential; "store" (from approve) saves what git sends.
        File.WriteAllText(config,
            "[credential]\n\thelper = \"!f() { case $1 in get) echo username=amy; echo password=tok-123;; store) cat > '"
            + _stored.Replace('\\', '/') + "';; esac; }; f\"\n");
        _bridge = new GitCredentialBridge(NullLogger<GitCredentialBridge>.Instance, new Dictionary<string, string>
        {
            ["GIT_CONFIG_GLOBAL"] = config,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
        });
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Fill_ReturnsTheHelpersCredential()
    {
        var credential = await _bridge.FillAsync(new Uri("https://github.com/"), interactive: false);

        Assert.Equal(new HostingCredential("tok-123", "amy"), credential);
    }

    [Fact]
    public async Task Approve_HandsTheCredentialAndPathToTheHelper()
    {
        await _bridge.ApproveAsync(new Uri("https://dev.azure.com/contoso/"), new HostingCredential("tok-9", "amy"));

        var stored = await File.ReadAllTextAsync(_stored);
        Assert.Contains("host=dev.azure.com", stored);
        Assert.Contains("path=contoso", stored);
        Assert.Contains("username=amy", stored);
        Assert.Contains("password=tok-9", stored);
    }
}
