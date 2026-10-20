using GitUi.Application.Abstractions;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

/// <summary>Fetch options against a real bare remote: what prune and prune-tags remove locally.</summary>
public sealed class FetchOptionsIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gitui-fetch-" + Guid.NewGuid().ToString("N"));
    private readonly GitCommandRunner _git = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance), new ProcessRunner());
    private readonly string _remote;
    private readonly string _clone;

    public FetchOptionsIntegrationTests()
    {
        _remote = Path.Combine(_root, "remote.git");
        _clone = Path.Combine(_root, "clone");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var file in new DirectoryInfo(_root).GetFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_root, recursive: true);
    }

    private Task<string> Git(string path, params string[] arguments) => _git.RunAsync(path, arguments);

    /// <summary>A clone that knows the remote's branch "gone" and tag "v-gone", both then deleted on the remote.</summary>
    private async Task SetUpAsync()
    {
        await Git(_root, "init", "-q", "--bare", _remote);
        await Git(_root, "clone", "-q", _remote, _clone);
        await Git(_clone, "config", "user.name", "Test");
        await Git(_clone, "config", "user.email", "test@example.com");
        await File.WriteAllTextAsync(Path.Combine(_clone, "a.txt"), "a");
        await Git(_clone, "add", "a.txt");
        await Git(_clone, "-c", "commit.gpgsign=false", "commit", "-q", "-m", "first");
        await Git(_clone, "push", "-q", "origin", "HEAD");
        await Git(_clone, "push", "-q", "origin", "HEAD:refs/heads/gone");
        await Git(_clone, "tag", "v-gone");
        await Git(_clone, "push", "-q", "origin", "v-gone");
        await Git(_clone, "fetch", "-q");

        await Git(_remote, "branch", "-D", "gone");
        await Git(_remote, "tag", "-d", "v-gone");
    }

    private async Task<bool> HasRefAsync(string reference) =>
        (await _git.TryRunAsync(_clone, new[] { "show-ref", "--verify", "--quiet", reference })).Success;

    [Fact]
    public async Task PruneAndPruneTags_RemoveWhatTheRemoteDeleted()
    {
        await SetUpAsync();

        await _git.RunAsync(_clone, new GitFetchOptions(Prune: true, PruneTags: true).ToArguments());

        Assert.False(await HasRefAsync("refs/remotes/origin/gone"));
        Assert.False(await HasRefAsync("refs/tags/v-gone"));
    }

    [Fact]
    public async Task WithoutPrune_StaleRefsStay()
    {
        await SetUpAsync();

        await _git.RunAsync(_clone, new GitFetchOptions(Prune: false).ToArguments("origin"));

        Assert.True(await HasRefAsync("refs/remotes/origin/gone"));
        Assert.True(await HasRefAsync("refs/tags/v-gone"));
    }

    [Fact]
    public async Task TagMovedOnTheServer_IsReported_ThenUpdatedOnRequest()
    {
        await SetUpAsync();
        await Git(_clone, "tag", "v-moved");
        await Git(_clone, "push", "-q", "origin", "v-moved");
        // On the server, v-moved now points to a new commit.
        await File.WriteAllTextAsync(Path.Combine(_clone, "a.txt"), "b");
        await Git(_clone, "-c", "commit.gpgsign=false", "commit", "-q", "-am", "second");
        var second = await Git(_clone, "rev-parse", "HEAD");
        await Git(_clone, "push", "-q", "origin", "HEAD", "+refs/tags/v-moved^{}:refs/tags/ignored-placeholder");
        await Git(_remote, "tag", "-f", "v-moved", second);
        await Git(_clone, "reset", "-q", "--hard", "HEAD~1");

        var settings = NSubstitute.Substitute.For<ISettingsStore>();
        NSubstitute.SubstituteExtensions.Returns(settings.LoadAsync(NSubstitute.Arg.Any<CancellationToken>()),
            GitUi.Application.DTOs.AppSettings.CreateDefault() with { FetchOptions = new GitFetchOptions(Tags: GitFetchTags.All) });
        var service = new GitUi.Application.Services.RemoteService(NSubstitute.Substitute.For<IGitRepositoryWriter>(), _git, settings);

        Assert.Equal(new[] { "v-moved" }, await service.FetchWithOptionsAsync(_clone));
        Assert.NotEqual(second, await Git(_clone, "rev-parse", "v-moved^{commit}"));

        await service.UpdateMovedTagsAsync(_clone);
        Assert.Equal(second, await Git(_clone, "rev-parse", "v-moved^{commit}"));
    }

    [Fact]
    public async Task PruneOnly_KeepsTags()
    {
        await SetUpAsync();

        await _git.RunAsync(_clone, GitFetchOptions.Default.ToArguments());

        Assert.False(await HasRefAsync("refs/remotes/origin/gone"));
        Assert.True(await HasRefAsync("refs/tags/v-gone"));
    }
}
