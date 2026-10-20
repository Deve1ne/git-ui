using GitUi.Infrastructure.Git;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class FileHistoryTests
{
    private readonly LibGit2SharpRepositoryReader _reader = new();

    [Fact]
    public async Task GetFileHistoryAsync_ListsOnlyCommitsTouchingTheFile_NewestFirst()
    {
        using var fixture = new TestRepositoryFixture();
        var first = fixture.CommitFile("a.txt", "one\n", "a1");
        fixture.CommitFile("b.txt", "other\n", "b1");
        var second = fixture.CommitFile("a.txt", "one\ntwo\n", "a2");

        var history = await _reader.GetFileHistoryAsync(fixture.Path, "a.txt");

        Assert.Equal(new[] { second, first }, history.Select(h => h.Commit.Sha));
        Assert.All(history, h => Assert.Equal("a.txt", h.Path));
    }

    [Fact]
    public async Task GetBlameAsync_AttributesEachLineToTheCommitThatWroteIt()
    {
        using var fixture = new TestRepositoryFixture();
        var first = fixture.CommitFile("a.txt", "one\ntwo\n", "first");
        var second = fixture.CommitFile("a.txt", "one\nTWO\nthree\n", "second");

        var blame = await _reader.GetBlameAsync(fixture.Path, "a.txt");

        Assert.Equal(new[] { "one", "TWO", "three" }, blame.Select(l => l.Content));
        Assert.Equal(new[] { first, second, second }, blame.Select(l => l.Sha));
        Assert.Equal(new[] { true, true, false }, blame.Select(l => l.StartsHunk));
        Assert.Equal("second", blame[1].Summary);
    }

    [Fact]
    public async Task GetComparison_ListsEveryFileChangedBetweenTwoCommits()
    {
        using var fixture = new TestRepositoryFixture();
        var from = fixture.CommitFile("a.txt", "one\n", "first");
        fixture.CommitFile("b.txt", "b\n", "second");
        var to = fixture.CommitFile("a.txt", "one\ntwo\n", "third");

        var files = await _reader.GetComparisonAsync(fixture.Path, from, to);

        Assert.Equal(new[] { "a.txt", "b.txt" }, files.Select(f => f.Path));
        var hunk = Assert.Single(await _reader.GetComparisonFileDiffAsync(fixture.Path, from, to, "a.txt"));
        Assert.Contains(hunk.Lines, l => l.Content == "two" && l.Kind == GitUi.Domain.ValueObjects.DiffLineKind.Added);
    }

    [Fact]
    public async Task GetBlameAsync_UncommittedFile_ExplainsWhy()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "one\n", "first");
        File.WriteAllText(Path.Combine(fixture.Path, "new.txt"), "x");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _reader.GetBlameAsync(fixture.Path, "new.txt"));
        Assert.Contains("not committed", error.Message);
    }
}
