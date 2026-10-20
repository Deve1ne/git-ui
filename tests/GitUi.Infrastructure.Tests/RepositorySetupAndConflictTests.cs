using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using CloneOptions = GitUi.Application.Abstractions.CloneOptions;

namespace GitUi.Infrastructure.Tests;

/// <summary>Clone, init and conflict resolution against real repositories.</summary>
public sealed class RepositorySetupAndConflictTests : IDisposable
{
    private readonly TestRepositoryFixture _fixture = new();
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "gitui-setup-" + Guid.NewGuid());
    private readonly CliGitRepositoryWriter _writer = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance));
    private readonly LibGit2SharpRepositoryReader _reader = new();

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_workspace))
        {
            foreach (var file in new DirectoryInfo(_workspace).GetFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task CloneAsync_LocalRepository_CreatesFolderNamedAfterSource()
    {
        var sha = _fixture.CommitFile("a.txt", "a", "first");
        var service = new RepositorySetupService(_writer);

        var clonePath = await service.CloneAsync(_fixture.Path, _workspace, folderName: "copy", new CloneOptions());

        Assert.Equal(Path.Combine(_workspace, "copy"), clonePath);
        using var repo = new Repository(clonePath);
        Assert.Equal(sha, repo.Head.Tip.Sha);
    }

    [Fact]
    public async Task CloneAsync_DestinationNotEmpty_Refuses()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "taken"));
        File.WriteAllText(Path.Combine(_workspace, "taken", "file.txt"), "x");
        var service = new RepositorySetupService(_writer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloneAsync(_fixture.Path, _workspace, "taken", new CloneOptions()));
    }

    [Fact]
    public async Task InitAsync_WithTemplateAndInitialCommit_CreatesRepositoryOnDefaultBranch()
    {
        var service = new RepositorySetupService(_writer);

        var path = await service.InitAsync(_workspace, "fresh", "trunk", "VisualStudio", createInitialCommit: false);

        using (var repo = new Repository(path))
        {
            repo.Config.Set("user.name", "Test User", ConfigurationLevel.Local);
            repo.Config.Set("user.email", "test@example.com", ConfigurationLevel.Local);
        }

        Assert.Contains("obj/", File.ReadAllText(Path.Combine(path, ".gitignore")));
        await _writer.StageAsync(path, ".");
        await _writer.CommitAsync(path, "first");
        using var reopened = new Repository(path);
        Assert.Equal("trunk", reopened.Head.FriendlyName);
    }

    [Fact]
    public async Task ConflictResolution_PickLinesSaveAndContinue_CompletesMerge()
    {
        _fixture.CommitFile("c.txt", "base\n", "base");
        string main;
        using (var repo = new Repository(_fixture.Path))
        {
            main = repo.Head.FriendlyName;
            repo.CreateBranch("feature");
            Commands.Checkout(repo, "feature");
        }

        _fixture.CommitFile("c.txt", "feature line\n", "feature edit");
        using (var repo = new Repository(_fixture.Path))
        {
            Commands.Checkout(repo, main);
        }

        _fixture.CommitFile("c.txt", "main line\n", "main edit");
        await Assert.ThrowsAnyAsync<Exception>(() => _writer.MergeAsync(_fixture.Path, "feature", GitMergeMode.NoFastForward));

        var service = new ConflictResolutionService(_reader, _writer);
        var (versions, document) = await service.LoadAsync(_fixture.Path, "c.txt");
        Assert.False(versions.IsDeleteConflict);
        var conflict = Assert.Single(document!.Conflicts);

        var output = document.Compose(new[] { new ConflictResolution(new HashSet<int> { 0 }, new HashSet<int> { 0 }) });
        await service.SaveResolutionAsync(_fixture.Path, "c.txt", output);

        Assert.DoesNotContain(await _reader.GetStatusAsync(_fixture.Path), c => c.ChangeType == GitUi.Domain.Entities.FileChangeType.Conflicted);
        await _writer.ContinueOperationAsync(_fixture.Path, GitOperationState.Merge);

        Assert.Equal(GitOperationState.None, await _reader.GetOperationStateAsync(_fixture.Path));
        Assert.Equal("main line\nfeature line\n", File.ReadAllText(Path.Combine(_fixture.Path, "c.txt")).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task ConflictResolution_ModifyDeleteConflict_IsReportedAndResolvedByTakingTheDeletion()
    {
        _fixture.CommitFile("d.txt", "base\n", "base");
        string main;
        using (var repo = new Repository(_fixture.Path))
        {
            main = repo.Head.FriendlyName;
            repo.CreateBranch("feature");
            Commands.Checkout(repo, "feature");
            Commands.Remove(repo, "d.txt");
            var signature = new Signature("Test User", "test@example.com", DateTimeOffset.UtcNow);
            repo.Commit("delete d", signature, signature);
            Commands.Checkout(repo, main);
        }

        _fixture.CommitFile("d.txt", "changed\n", "modify d");
        await Assert.ThrowsAnyAsync<Exception>(() => _writer.MergeAsync(_fixture.Path, "feature", GitMergeMode.NoFastForward));

        var service = new ConflictResolutionService(_reader, _writer);
        var (versions, document) = await service.LoadAsync(_fixture.Path, "d.txt");
        Assert.True(versions.IsDeleteConflict);
        Assert.Null(document);

        await service.TakeSideAsync(_fixture.Path, "d.txt", ConflictSide.Theirs);

        Assert.False(File.Exists(Path.Combine(_fixture.Path, "d.txt")));
        Assert.DoesNotContain(await _reader.GetStatusAsync(_fixture.Path), c => c.ChangeType == GitUi.Domain.Entities.FileChangeType.Conflicted);
    }
}
