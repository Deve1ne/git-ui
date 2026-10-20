using GitUi.Application.Services;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class PushIntegrationTests
{
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));
    private readonly LibGit2SharpRepositoryReader _reader = new();

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "gitui-push-" + Guid.NewGuid());

    private static void Commit(string repoPath, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(repoPath, file), content);
        using var repo = new Repository(repoPath);
        Commands.Stage(repo, file);
        var signature = new Signature("Test User", "test@example.com", DateTimeOffset.UtcNow);
        repo.Commit(message, signature, signature);
    }

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
    public async Task Push_RemoteMovedOn_IsRejectedThenForcePushWithLeaseWins()
    {
        using var seed = new TestRepositoryFixture();
        seed.CommitFile("a.txt", "seed", "seed");
        var bare = NewDirectory();
        var mine = NewDirectory();
        var theirs = NewDirectory();
        try
        {
            Repository.Clone(seed.Path, bare, new CloneOptions { IsBare = true });
            Repository.Clone(bare, mine);
            Repository.Clone(bare, theirs);

            Commit(theirs, "b.txt", "theirs", "their commit");
            await _writer.PushAsync(theirs, force: false);

            Commit(mine, "c.txt", "mine", "my commit");
            var service = new PushService(_reader, _writer);

            Assert.Equal(PushOutcome.Rejected, await service.PushAsync(mine));

            // With lease: our clone's origin/main is what the remote had at our last fetch... which
            // is stale, so fetch first, as the user would after reading the prompt.
            await _writer.FetchAsync(mine, prune: false);
            await service.ForcePushAsync(mine);

            using var remote = new Repository(bare);
            Assert.Equal("my commit", remote.Branches.First(b => b.FriendlyName is "main" or "master").Tip.MessageShort);
        }
        finally
        {
            ForceDelete(bare);
            ForceDelete(mine);
            ForceDelete(theirs);
        }
    }

    [Fact]
    public async Task Push_NewBranchWithoutUpstream_IsPushedAndTracked()
    {
        using var seed = new TestRepositoryFixture();
        seed.CommitFile("a.txt", "seed", "seed");
        var bare = NewDirectory();
        var mine = NewDirectory();
        try
        {
            Repository.Clone(seed.Path, bare, new CloneOptions { IsBare = true });
            Repository.Clone(bare, mine);
            using (var repo = new Repository(mine))
            {
                Commands.Checkout(repo, repo.CreateBranch("feature/new"));
            }

            var service = new PushService(_reader, _writer);
            Assert.Equal(PushOutcome.NeedsUpstream, await service.PushAsync(mine));

            await service.PushWithUpstreamAsync(mine, await service.SuggestUpstreamAsync(mine));

            using var local = new Repository(mine);
            Assert.Equal("origin/feature/new", local.Head.TrackedBranch?.FriendlyName);
        }
        finally
        {
            ForceDelete(bare);
            ForceDelete(mine);
        }
    }
}
