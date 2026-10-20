using GitUi.Domain.Entities;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class StashDetailsTests
{
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));
    private readonly LibGit2SharpRepositoryReader _reader = new();

    [Fact]
    public async Task StashSaveAsync_WithUntrackedFile_StashesItAndDetailsListIt()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "one\n", "seed");
        File.WriteAllText(Path.Combine(fixture.Path, "a.txt"), "two\n");
        File.WriteAllText(Path.Combine(fixture.Path, "new.txt"), "fresh\n");

        await _writer.StashSaveAsync(fixture.Path, "my stash");

        Assert.False(File.Exists(Path.Combine(fixture.Path, "new.txt")));
        var stash = Assert.Single(await _reader.GetStashesAsync(fixture.Path));
        var details = await _reader.GetCommitDetailsAsync(fixture.Path, stash.Sha);
        Assert.Collection(
            details.Files,
            a => Assert.Equal(("a.txt", FileChangeType.Modified), (a.Path, a.ChangeType)),
            n => Assert.Equal(("new.txt", FileChangeType.Added), (n.Path, n.ChangeType)));

        var hunk = Assert.Single(await _reader.GetCommitFileDiffAsync(fixture.Path, stash.Sha, "new.txt"));
        Assert.Contains(hunk.Lines, l => l.Content == "fresh");
    }
}
