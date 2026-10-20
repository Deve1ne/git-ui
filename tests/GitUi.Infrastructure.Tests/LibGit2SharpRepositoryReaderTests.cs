using GitUi.Domain.ValueObjects;
using GitUi.Infrastructure.Git;
using LibGit2Sharp;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class LibGit2SharpRepositoryReaderTests : IClassFixture<TestRepositoryFixture>
{
    private readonly TestRepositoryFixture _fixture;

    public LibGit2SharpRepositoryReaderTests(TestRepositoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void IsValidRepository_InitializedRepo_ReturnsTrue()
    {
        var reader = new LibGit2SharpRepositoryReader();

        Assert.True(reader.IsValidRepository(_fixture.Path));
    }

    [Fact]
    public async Task GetCommitsAsync_TwoCommits_ReturnsBothNewestFirstWithParentChain()
    {
        var firstSha = _fixture.CommitFile("a.txt", "one", "first commit");
        var secondSha = _fixture.CommitFile("a.txt", "two", "second commit");

        var reader = new LibGit2SharpRepositoryReader();
        var commits = await reader.GetCommitsAsync(_fixture.Path);

        Assert.Equal(secondSha, commits[0].Sha);
        Assert.Equal(firstSha, commits[0].ParentShas[0]);
        Assert.Equal(firstSha, commits[1].Sha);
        Assert.Empty(commits[1].ParentShas);
    }

    [Fact]
    public async Task GetStatusAsync_UntrackedFile_ReturnsUnstagedAddedChange()
    {
        _fixture.CommitFile("tracked.txt", "content", "initial");
        File.WriteAllText(Path.Combine(_fixture.Path, "untracked.txt"), "new");

        var reader = new LibGit2SharpRepositoryReader();
        var changes = await reader.GetStatusAsync(_fixture.Path);

        var untracked = Assert.Single(changes, c => c.Path == "untracked.txt");
        Assert.Equal(GitUi.Domain.Entities.FileChangeType.Added, untracked.ChangeType);
        Assert.False(untracked.IsStaged);
    }

    [Fact]
    public async Task GetStatusAsync_IgnoredFile_IsNotReported()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile(".gitignore", "build/\n", "ignore build output");
        Directory.CreateDirectory(Path.Combine(fixture.Path, "build"));
        File.WriteAllText(Path.Combine(fixture.Path, "build", "out.dll"), "binary");

        var reader = new LibGit2SharpRepositoryReader();
        var changes = await reader.GetStatusAsync(fixture.Path);

        Assert.DoesNotContain(changes, c => c.Path.StartsWith("build", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetStatusAsync_FileModifiedInIndexAndWorkdir_ReturnsStagedAndUnstagedEntries()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("both.txt", "one", "initial");
        File.WriteAllText(Path.Combine(fixture.Path, "both.txt"), "two");
        using (var repo = new Repository(fixture.Path))
        {
            Commands.Stage(repo, "both.txt");
        }

        File.WriteAllText(Path.Combine(fixture.Path, "both.txt"), "three");

        var reader = new LibGit2SharpRepositoryReader();
        var changes = await reader.GetStatusAsync(fixture.Path);

        var entries = changes.Where(c => c.Path == "both.txt").ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, c => c.IsStaged && c.ChangeType == GitUi.Domain.Entities.FileChangeType.Modified);
        Assert.Contains(entries, c => !c.IsStaged && c.ChangeType == GitUi.Domain.Entities.FileChangeType.Modified);
    }

    [Fact]
    public async Task GetDiffAsync_UntrackedFile_ReturnsWholeFileAsAddedLines()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("tracked.txt", "content", "initial");
        File.WriteAllText(Path.Combine(fixture.Path, "new.txt"), "first\nsecond\n");

        var reader = new LibGit2SharpRepositoryReader();
        var hunks = await reader.GetDiffAsync(fixture.Path, "new.txt", staged: false);

        var lines = hunks.SelectMany(h => h.Lines).ToList();
        Assert.Equal(new[] { "first", "second" }, lines.Select(l => l.Content));
        Assert.All(lines, l => Assert.Equal(DiffLineKind.Added, l.Kind));
    }

    [Fact]
    public async Task GetDiffAsync_FileWithoutTrailingNewline_DoesNotIncludeNoNewlineMarkerAsContentLine()
    {
        _fixture.CommitFile("no-eol.txt", "line one", "add no-eol file");
        File.WriteAllText(Path.Combine(_fixture.Path, "no-eol.txt"), "line one changed");

        var reader = new LibGit2SharpRepositoryReader();
        var hunks = await reader.GetDiffAsync(_fixture.Path, "no-eol.txt", staged: false);

        var allLines = hunks.SelectMany(h => h.Lines).ToList();
        Assert.DoesNotContain(allLines, l => l.Content.Contains("No newline at end of file"));
        Assert.Contains(allLines, l => l.Kind == DiffLineKind.Removed && l.Content == "line one");
        Assert.Contains(allLines, l => l.Kind == DiffLineKind.Added && l.Content == "line one changed");
    }

    [Fact]
    public async Task GetCommitsAsync_BranchDivergedFromHead_StillIncludesItsCommits()
    {
        using var fixture = new TestRepositoryFixture();
        fixture.CommitFile("a.txt", "one", "on original branch");

        string originalBranchName;
        using (var repo = new Repository(fixture.Path))
        {
            originalBranchName = repo.Head.FriendlyName;
            var divergedBranch = repo.CreateBranch("diverged");
            Commands.Checkout(repo, divergedBranch);
        }

        var divergedSha = fixture.CommitFile("b.txt", "two", "on diverged branch");

        using (var repo = new Repository(fixture.Path))
        {
            Commands.Checkout(repo, originalBranchName);
        }

        var reader = new LibGit2SharpRepositoryReader();
        var commits = await reader.GetCommitsAsync(fixture.Path);

        Assert.Contains(commits, c => c.Sha == divergedSha);
    }

    [Fact]
    public async Task GetTagsAsync_AnnotatedTag_ReturnsCommitShaNotAnnotationSha()
    {
        using var fixture = new TestRepositoryFixture();
        var commitSha = fixture.CommitFile("tagged.txt", "content", "commit to tag");

        using (var repo = new Repository(fixture.Path))
        {
            var tagger = new Signature("Test User", "test@example.com", DateTimeOffset.UtcNow);
            repo.Tags.Add("v1.0.0", repo.Head.Tip, tagger, "annotated tag message");
        }

        var reader = new LibGit2SharpRepositoryReader();
        var tags = await reader.GetTagsAsync(fixture.Path);

        var tag = Assert.Single(tags, t => t.Name == "v1.0.0");
        Assert.Equal(commitSha, tag.TargetSha);
    }

    [Fact]
    public async Task GetBranchesAsync_LocalCommitNotPushed_ReportsAheadOfUpstream()
    {
        using var upstream = new TestRepositoryFixture();
        upstream.CommitFile("a.txt", "one", "first");
        var clonePath = Path.Combine(Path.GetTempPath(), "gitui-clone-" + Guid.NewGuid());
        Repository.Clone(upstream.Path, clonePath);

        try
        {
            using (var clone = new Repository(clonePath))
            {
                File.WriteAllText(Path.Combine(clonePath, "b.txt"), "local");
                Commands.Stage(clone, "b.txt");
                var signature = new Signature("Test User", "test@example.com", DateTimeOffset.UtcNow);
                clone.Commit("local only", signature, signature);
            }

            var branches = await new LibGit2SharpRepositoryReader().GetBranchesAsync(clonePath);

            var head = Assert.Single(branches, b => b.IsHead);
            Assert.Equal(1, head.AheadBy);
            Assert.Equal(0, head.BehindBy);
        }
        finally
        {
            foreach (var file in new DirectoryInfo(clonePath).GetFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(clonePath, recursive: true);
        }
    }
}
