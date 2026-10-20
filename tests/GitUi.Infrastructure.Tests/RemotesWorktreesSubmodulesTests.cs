using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class RemotesWorktreesSubmodulesTests
{
    private readonly GitCliRunner _runner = new(new ProcessRunner(), NullLogger<GitCliRunner>.Instance);
    private readonly CliGitRepositoryWriter _writer;
    private readonly LibGit2SharpRepositoryReader _reader = new();

    public RemotesWorktreesSubmodulesTests() => _writer = new CliGitRepositoryWriter(_runner);

    private static void ForceDelete(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(path, recursive: true);
    }

    [Fact]
    public async Task Remotes_AddRenameSetUrlRemove()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "a", "a");

        await _writer.AddRemoteAsync(fixture.Path, "origin", "https://example.com/one.git");
        await _writer.RenameRemoteAsync(fixture.Path, "origin", "upstream");
        await _writer.SetRemoteUrlAsync(fixture.Path, "upstream", "https://example.com/two.git");

        var remote = Assert.Single(await _reader.GetRemotesAsync(fixture.Path));
        Assert.Equal(("upstream", "https://example.com/two.git"), (remote.Name, remote.FetchUrl));

        await _writer.RemoveRemoteAsync(fixture.Path, "upstream");
        Assert.Empty(await _reader.GetRemotesAsync(fixture.Path));
    }

    [Fact]
    public async Task Worktrees_AddListRemove()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "a", "a");
        var linkedPath = Path.Combine(Path.GetTempPath(), "gitui-wt-" + Guid.NewGuid());
        try
        {
            await _writer.AddWorktreeAsync(fixture.Path, linkedPath, "HEAD", "feature/wt");

            var worktrees = await _reader.GetWorktreesAsync(fixture.Path);
            Assert.Equal(2, worktrees.Count);
            Assert.True(worktrees[0].IsMain);
            Assert.Equal(("feature/wt", false), (worktrees[1].Branch, worktrees[1].IsMain));
            Assert.Equal(Path.GetFullPath(linkedPath).TrimEnd('\\'), Path.GetFullPath(worktrees[1].Path).TrimEnd('\\'), ignoreCase: true);

            // Listed the same from inside the linked worktree.
            Assert.Equal(2, (await _reader.GetWorktreesAsync(linkedPath)).Count);

            await _writer.RemoveWorktreeAsync(fixture.Path, linkedPath, force: false);
            Assert.Single(await _reader.GetWorktreesAsync(fixture.Path));
        }
        finally
        {
            ForceDelete(linkedPath);
        }
    }

    [Fact]
    public async Task Submodules_ListedAndUpdated()
    {
        using var library = new TestRepositoryFixture();
        library.CommitFile("lib.txt", "lib", "lib");
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "a", "a");

        // Local-path submodules need protocol.file.allow on recent git versions.
        await _runner.RunAsync(fixture.Path, new[] { "-c", "protocol.file.allow=always", "submodule", "add", library.Path.Replace('\\', '/'), "libs/lib" }, TimeSpan.FromMinutes(1));

        var submodule = Assert.Single(await _reader.GetSubmodulesAsync(fixture.Path));
        Assert.Equal(("libs/lib", true), (submodule.Path, submodule.IsInitialized));
    }
}
