using GitUi.Application.Abstractions;
using GitUi.Application.DTOs;
using GitUi.Application.Services;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class FetchOptionsTests
{
    [Fact]
    public void Defaults_FetchAllRemotesAndPrune() =>
        Assert.Equal(new[] { "fetch", "--all", "--prune" }, GitFetchOptions.Default.ToArguments());

    [Fact]
    public void EveryOption_OneRemote() =>
        Assert.Equal(
            new[] { "fetch", "--prune", "--prune-tags", "--tags", "--recurse-submodules=yes", "--", "origin" },
            new GitFetchOptions(Prune: true, PruneTags: true, Tags: GitFetchTags.All, RecurseSubmodules: true).ToArguments("origin"));

    [Fact]
    public void NoPrune_IgnoresPruneTags_AndCanSkipTags() =>
        Assert.Equal(
            new[] { "fetch", "--all", "--no-prune", "--no-tags" },
            new GitFetchOptions(Prune: false, PruneTags: true, Tags: GitFetchTags.None).ToArguments());

    private const string ClobberOutput =
        "From https://dev.azure.com/org/Project/_git/repo\n" +
        "   3079acc36d..3444893f7b  develop -> origin/develop\n" +
        " ! [rejected]              3.11.2.2                    -> 3.11.2.2  (would clobber existing tag)\n" +
        " ! [rejected]              3.11.4.0                    -> 3.11.4.0  (would clobber existing tag)\n" +
        " * [new tag]               v_prod_3.12.0.1 -> v_prod_3.12.0.1\n" +
        "error: could not fetch origin";

    [Fact]
    public async Task MovedTagsOnly_AreReported_NotThrown()
    {
        var git = Substitute.For<IGitCommandRunner>();
        git.TryRunAsync("/repo", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(1, string.Empty, ClobberOutput));
        var service = new RemoteService(Substitute.For<IGitRepositoryWriter>(), git);

        Assert.Equal(new[] { "3.11.2.2", "3.11.4.0" }, await service.FetchWithOptionsAsync("/repo"));

        await service.UpdateMovedTagsAsync("/repo");
        await git.Received(1).RunAsync("/repo", Arg.Is<IReadOnlyList<string>>(a => a.Contains("--force") && a.Contains("--all")),
            Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherFailures_StillThrow()
    {
        var git = Substitute.For<IGitCommandRunner>();
        var result = new GitCommandResult(128, string.Empty, ClobberOutput + "\nfatal: Authentication failed");
        git.TryRunAsync("/repo", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(result);
        git.ToException(Arg.Any<IReadOnlyList<string>>(), result).Returns(new InvalidOperationException("auth"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RemoteService(Substitute.For<IGitRepositoryWriter>(), git).FetchWithOptionsAsync("/repo"));
    }

    [Fact]
    public async Task FetchWithOptions_UsesTheSavedOptions()
    {
        var git = Substitute.For<IGitCommandRunner>();
        var settings = Substitute.For<ISettingsStore>();
        settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(AppSettings.CreateDefault() with
        {
            FetchOptions = new GitFetchOptions(Prune: false, Tags: GitFetchTags.All),
        });
        var service = new RemoteService(Substitute.For<IGitRepositoryWriter>(), git, settings);
        git.TryRunAsync("/repo", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, string.Empty, string.Empty));

        Assert.Empty(await service.FetchWithOptionsAsync("/repo", "upstream"));

        await git.Received(1).TryRunAsync("/repo",
            Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "fetch", "--no-prune", "--tags", "--", "upstream" })),
            Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }
}
