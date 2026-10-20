using GitUi.Application.Services;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

/// <summary>Bisect, reflog, Git Flow, LFS and signature status against real git in throwaway repositories.</summary>
public sealed class GitWorkflowsIntegrationTests : IDisposable
{
    private readonly TestRepositoryFixture _fixture = new();
    private readonly GitCommandRunner _git = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance), new ProcessRunner());

    public void Dispose() => _fixture.Dispose();

    private string Path => _fixture.Path;

    private Task<string> Git(params string[] arguments) => _git.RunAsync(Path, arguments);

    private async Task<string> CommitAsync(string file, string content, string message)
    {
        await File.WriteAllTextAsync(System.IO.Path.Combine(Path, file), content);
        await Git("add", "--", file);
        // No signing from the machine's global config in these commits.
        await Git("-c", "commit.gpgsign=false", "commit", "-q", "-m", message);
        return await Git("rev-parse", "HEAD");
    }

    [Fact]
    public async Task Bisect_FindsTheCommitThatIntroducedTheBug()
    {
        var shas = new List<string>();
        for (var i = 1; i <= 8; i++)
        {
            // Commit 5 introduces the bug, which stays afterwards.
            shas.Add(await CommitAsync("app.txt", i >= 5 ? $"v{i} bug" : $"v{i}", $"commit {i}"));
        }

        var bisect = new BisectService(_git);
        await bisect.MarkAsync(Path, shas[^1], BisectMark.Bad);
        await bisect.MarkAsync(Path, shas[0], BisectMark.Good);

        var state = await bisect.GetStateAsync(Path);
        for (var step = 0; step < 10 && !state.IsFinished; step++)
        {
            var broken = (await File.ReadAllTextAsync(System.IO.Path.Combine(Path, "app.txt"))).Contains("bug", StringComparison.Ordinal);
            await bisect.MarkAsync(Path, state.Current!, broken ? BisectMark.Bad : BisectMark.Good);
            state = await bisect.GetStateAsync(Path);
        }

        Assert.True(state.IsActive);
        Assert.Equal(shas[4], state.FirstBad);

        await bisect.ResetAsync(Path);
        Assert.False((await bisect.GetStateAsync(Path)).IsActive);
        Assert.Equal(shas[^1], await Git("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task Bisect_StateCountsRevisionsLeft()
    {
        var first = await CommitAsync("a.txt", "1", "one");
        for (var i = 2; i <= 6; i++)
        {
            await CommitAsync("a.txt", i.ToString(), $"c{i}");
        }

        var bisect = new BisectService(_git);
        var summary = await bisect.MarkAsync(Path, "HEAD", BisectMark.Bad);
        Assert.False((await bisect.GetStateAsync(Path)).IsFinished);

        summary = await bisect.MarkAsync(Path, first, BisectMark.Good);
        var state = await bisect.GetStateAsync(Path);

        Assert.StartsWith("Bisecting:", summary);
        Assert.True(state.RevisionsLeft > 0);
        Assert.Single(state.Good);
        await bisect.ResetAsync(Path);
    }

    [Fact]
    public async Task Reflog_KeepsACommitLostByAHardReset()
    {
        var first = await CommitAsync("a.txt", "1", "one");
        var lost = await CommitAsync("a.txt", "2", "two");
        await Git("reset", "--hard", first);

        var entries = await new ReflogService(_git).GetAsync(Path);

        Assert.Equal("HEAD@{0}", entries[0].Selector);
        Assert.Equal("reset", entries[0].Action);
        Assert.Equal(first, entries[0].Sha);
        Assert.Contains(entries, e => e.Sha == lost && e.Message.Contains("two", StringComparison.Ordinal));
        Assert.All(entries, e => Assert.NotNull(e.Date));
    }

    [Fact]
    public async Task GitFlow_InitStartAndFinishFeatureAndRelease()
    {
        await CommitAsync("a.txt", "base", "base");
        var production = await Git("branch", "--show-current");
        var flow = new GitFlowService(_git);

        Assert.Null(await flow.GetConfigAsync(Path));
        var suggested = await flow.SuggestConfigAsync(Path);
        Assert.Equal(production, suggested.ProductionBranch);
        await flow.InitializeAsync(Path, suggested);
        var config = (await flow.GetConfigAsync(Path))!;
        Assert.Equal("develop", config.DevelopBranch);
        Assert.Equal("feature/", config.FeaturePrefix);

        // Feature: merged into develop with a merge commit, then deleted.
        Assert.Equal("feature/login", await flow.StartAsync(Path, config, GitFlowKind.Feature, "login"));
        await CommitAsync("login.txt", "login", "login page");
        var feature = Assert.Single(GitFlowService.Classify(config, new[] { production, "develop", "feature/login" }));
        await flow.FinishAsync(Path, config, feature);

        Assert.Equal("develop", await Git("branch", "--show-current"));
        Assert.Equal("", await Git("branch", "--list", "feature/login"));
        Assert.Contains("Merge branch 'feature/login'", await Git("log", "-1", "--format=%s"));

        // Release: into production with a tag, and back into develop.
        await flow.StartAsync(Path, config, GitFlowKind.Release, "1.0");
        await CommitAsync("version.txt", "1.0", "bump version");
        await flow.FinishAsync(Path, config, new GitFlowBranch(GitFlowKind.Release, "1.0", "release/1.0"), "Version 1.0");

        Assert.Equal(await Git("rev-parse", $"{production}^{{commit}}"), await Git("rev-parse", "1.0^{commit}"));
        Assert.Equal("Version 1.0", await Git("tag", "-l", "--format=%(contents:subject)", "1.0"));
        Assert.True(File.Exists(System.IO.Path.Combine(Path, "version.txt")), "develop has the release commit");
        Assert.True((await _git.TryRunAsync(Path, new[] { "merge-base", "--is-ancestor", production, "develop" })).Success);
    }

    [Fact]
    public async Task Lfs_TrackAndUntrackAPattern()
    {
        await CommitAsync("a.txt", "x", "base");
        var lfs = new LfsService(_git);
        if (!await lfs.IsInstalledAsync(Path))
        {
            return; // git-lfs is not installed on this machine.
        }

        await lfs.TrackAsync(Path, "*.bin");
        var status = await lfs.GetStatusAsync(Path);
        Assert.Contains("*.bin", status.TrackedPatterns);
        Assert.Contains("filter=lfs", await File.ReadAllTextAsync(System.IO.Path.Combine(Path, ".gitattributes")));
        Assert.Contains("A  .gitattributes", await Git("status", "--porcelain"));

        await lfs.UntrackAsync(Path, "*.bin");
        Assert.DoesNotContain("*.bin", (await lfs.GetStatusAsync(Path)).TrackedPatterns);
    }

    [Fact]
    public void Lfs_ParsesTheTrackListing()
    {
        var output = "Listing tracked patterns\n    *.psd (.gitattributes)\n    assets/*.bin (assets/.gitattributes)\nListing excluded patterns\n    *.txt (.gitattributes)\n";
        Assert.Equal(new[] { "*.psd", "assets/*.bin" }, LfsService.ParseTrackedPatterns(output));
    }

    [Fact]
    public async Task Signature_OfAnSshSignedCommitIsGood_AndOfAnUnsignedOneIsNone()
    {
        var keyDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gitui-ssh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyDirectory);
        try
        {
            var unsigned = await CommitAsync("a.txt", "1", "unsigned");
            var key = System.IO.Path.Combine(keyDirectory, "key");
            var keygen = await new ProcessRunner().RunAsync("ssh-keygen", new[] { "-q", "-t", "ed25519", "-N", "", "-C", "test", "-f", key }, keyDirectory, TimeSpan.FromSeconds(30));
            if (keygen.ExitCode != 0)
            {
                return; // No ssh-keygen here.
            }

            var publicKey = (await File.ReadAllTextAsync(key + ".pub")).Trim();
            var allowed = System.IO.Path.Combine(keyDirectory, "allowed_signers");
            await File.WriteAllTextAsync(allowed, $"test@example.com namespaces=\"git\" {publicKey}\n");
            await Git("config", "gpg.format", "ssh");
            await Git("config", "user.signingkey", key + ".pub");
            await Git("config", "gpg.ssh.allowedSignersFile", allowed);

            await File.WriteAllTextAsync(System.IO.Path.Combine(Path, "a.txt"), "2");
            await Git("commit", "-q", "-S", "-am", "signed");
            var signing = new SigningService(null!, _git);

            var signature = await signing.GetSignatureAsync(Path, "HEAD");
            Assert.Equal('G', signature.Code);
            Assert.True(signature.IsVerified);
            Assert.Equal("test@example.com", signature.Signer);
            Assert.False((await signing.GetSignatureAsync(Path, unsigned)).IsSigned);
        }
        finally
        {
            Directory.Delete(keyDirectory, recursive: true);
        }
    }
}
