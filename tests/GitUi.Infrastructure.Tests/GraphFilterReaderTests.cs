using GitUi.Application.Abstractions;
using GitUi.Infrastructure.Git;
using LibGit2Sharp;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class GraphFilterReaderTests
{
    private readonly LibGit2SharpRepositoryReader _reader = new();

    // main: base -> m1 ; side (from base): s1
    private static (TestRepositoryFixture Fixture, string Base, string M1, string S1) Build()
    {
        var fixture = new TestRepositoryFixture();
        var baseSha = fixture.CommitFile("a.txt", "base", "base");
        using (var repo = new Repository(fixture.Path))
        {
            repo.CreateBranch("side");
        }

        var m1 = fixture.CommitFile("a.txt", "main", "m1");
        using (var repo = new Repository(fixture.Path))
        {
            Commands.Checkout(repo, "side");
        }

        var s1 = fixture.CommitFile("b.txt", "side", "s1");
        using (var repo = new Repository(fixture.Path))
        {
            Commands.Checkout(repo, repo.Branches.First(b => !b.IsRemote && b.FriendlyName != "side"));
        }

        return (fixture, baseSha, m1, s1);
    }

    [Fact]
    public async Task HiddenBranch_DropsCommitsOnlyItReaches()
    {
        var (fixture, baseSha, m1, s1) = Build();
        using var _ = fixture;

        var commits = await _reader.GetCommitsAsync(fixture.Path, new GraphRefFilter(new[] { "refs/heads/side" }, Array.Empty<string>()));

        var shas = commits.Select(c => c.Sha).ToList();
        Assert.Contains(m1, shas);
        Assert.Contains(baseSha, shas);
        Assert.DoesNotContain(s1, shas);
    }

    [Fact]
    public async Task SoloBranch_ShowsOnlyItsHistory()
    {
        var (fixture, baseSha, m1, s1) = Build();
        using var _ = fixture;

        var commits = await _reader.GetCommitsAsync(fixture.Path, new GraphRefFilter(Array.Empty<string>(), new[] { "refs/heads/side" }));

        Assert.Equal(new[] { s1, baseSha }, commits.Select(c => c.Sha));
        Assert.DoesNotContain(m1, commits.Select(c => c.Sha));
    }
}
