using GitUi.Domain.Entities;
using GitUi.Domain.ValueObjects;
using GitUi.Infrastructure.Git;
using LibGit2Sharp;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public sealed class CommitDetailsTests : IDisposable
{
    private readonly TestRepositoryFixture _fixture = new();
    private readonly LibGit2SharpRepositoryReader _reader = new();

    public void Dispose() => _fixture.Dispose();

    private string Commit(string message, Action<string> change)
    {
        change(_fixture.Path);
        using var repo = new Repository(_fixture.Path);
        Commands.Stage(repo, "*");
        var signature = new Signature("Ada", "ada@example.com", DateTimeOffset.UtcNow);
        return repo.Commit(message, signature, signature).Sha;
    }

    [Fact]
    public async Task GetCommitDetailsAsync_ReportsMessageAuthorParentsAndFileStats()
    {
        var first = Commit("first", dir => File.WriteAllText(Path.Combine(dir, "a.txt"), "one\ntwo\n"));
        var second = Commit("Second summary\n\nLonger body\nline two", dir =>
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "one\nTWO\nthree\n");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "new\n");
        });

        var details = await _reader.GetCommitDetailsAsync(_fixture.Path, second);

        Assert.Equal("Second summary", details.Summary);
        Assert.Equal("Longer body\nline two", details.Body);
        Assert.Equal("Ada", details.AuthorName);
        Assert.Equal(new[] { first }, details.ParentShas);
        var a = Assert.Single(details.Files, f => f.Path == "a.txt");
        Assert.Equal((FileChangeType.Modified, 2, 1), (a.ChangeType, a.Additions, a.Deletions));
        Assert.Equal(FileChangeType.Added, Assert.Single(details.Files, f => f.Path == "b.txt").ChangeType);
        Assert.Equal(3, details.TotalAdditions);
    }

    [Fact]
    public async Task GetCommitDetailsAsync_RootCommit_ListsEveryFileAsAdded()
    {
        var root = Commit("root", dir => File.WriteAllText(Path.Combine(dir, "r.txt"), "x\n"));

        var details = await _reader.GetCommitDetailsAsync(_fixture.Path, root);

        Assert.Empty(details.ParentShas);
        Assert.Equal(FileChangeType.Added, Assert.Single(details.Files).ChangeType);
    }

    [Fact]
    public async Task GetCommitDetailsAsync_Rename_IsReportedWithOldPath()
    {
        Commit("add", dir => File.WriteAllText(Path.Combine(dir, "old.txt"), string.Join('\n', Enumerable.Range(0, 30)) + "\n"));
        var renamed = Commit("rename", dir => File.Move(Path.Combine(dir, "old.txt"), Path.Combine(dir, "new.txt")));

        var details = await _reader.GetCommitDetailsAsync(_fixture.Path, renamed);

        var file = Assert.Single(details.Files);
        Assert.Equal((FileChangeType.Renamed, "new.txt", "old.txt"), (file.ChangeType, file.Path, file.OldPath));
    }

    [Fact]
    public async Task GetCommitFileDiffAsync_ReturnsTheFileChangesOfThatCommit()
    {
        Commit("first", dir => File.WriteAllText(Path.Combine(dir, "a.txt"), "one\ntwo\n"));
        var second = Commit("second", dir => File.WriteAllText(Path.Combine(dir, "a.txt"), "one\nTWO\n"));

        var hunks = await _reader.GetCommitFileDiffAsync(_fixture.Path, second, "a.txt");

        var lines = hunks.SelectMany(h => h.Lines).ToList();
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Removed && l.Content == "two");
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Added && l.Content == "TWO");
    }
}
