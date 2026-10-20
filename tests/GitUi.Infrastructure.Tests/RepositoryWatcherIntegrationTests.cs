using System.Collections.Concurrent;
using GitUi.Application.Abstractions;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitUi.Infrastructure.Tests;

/// <summary>The watcher against a real repository changed from the command line, like a terminal would.</summary>
public sealed class RepositoryWatcherIntegrationTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(2);

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "gitui-watch-" + Guid.NewGuid().ToString("N"));
    private readonly GitCommandRunner _git = new(new GitCliRunner(new ProcessRunner(), NullLogger<GitCliRunner>.Instance), new ProcessRunner());
    private readonly FileSystemRepositoryWatcher _watcher = new(NullLogger<FileSystemRepositoryWatcher>.Instance);
    private readonly BlockingCollection<RepositoryChangeKind> _changes = new();

    public RepositoryWatcherIntegrationTests()
    {
        Directory.CreateDirectory(_repo);
        _watcher.Changed += _changes.Add;
    }

    public void Dispose()
    {
        _watcher.Dispose();
        foreach (var file in new DirectoryInfo(_repo).GetFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_repo, recursive: true);
    }

    private Task<string> Git(params string[] arguments) => _git.RunAsync(_repo, arguments);

    private async Task SetUpAsync()
    {
        await Git("init", "-q", "-b", "main");
        await Git("config", "user.name", "Test");
        await Git("config", "user.email", "test@example.com");
        await File.WriteAllTextAsync(Path.Combine(_repo, ".gitignore"), "bin/\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "a");
        await Git("add", ".");
        await Git("-c", "commit.gpgsign=false", "commit", "-q", "-m", "first");
        Directory.CreateDirectory(Path.Combine(_repo, "bin"));
        _watcher.Watch(_repo);
    }

    // Waits for the burst caused by one action to settle and returns what was reported.
    private List<RepositoryChangeKind> Drain()
    {
        var seen = new List<RepositoryChangeKind>();
        if (_changes.TryTake(out var first, Timeout))
        {
            seen.Add(first);
            while (_changes.TryTake(out var next, Silence))
            {
                seen.Add(next);
            }
        }

        return seen;
    }

    [Fact]
    public async Task CommitFromTheCommandLine_IsAReferenceChange()
    {
        await SetUpAsync();

        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "b");
        await Git("-c", "commit.gpgsign=false", "commit", "-q", "-am", "second");

        Assert.Contains(RepositoryChangeKind.References, Drain());
    }

    [Fact]
    public async Task NewBranchFromTheCommandLine_IsAReferenceChange()
    {
        await SetUpAsync();

        await Git("branch", "feature");

        Assert.Contains(RepositoryChangeKind.References, Drain());
    }

    [Fact]
    public async Task EditingATrackedFile_IsAWorkingTreeChangeOnly()
    {
        await SetUpAsync();

        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "edited");

        var changes = Drain();
        Assert.Contains(RepositoryChangeKind.WorkingTree, changes);
        Assert.DoesNotContain(RepositoryChangeKind.References, changes);
    }

    [Fact]
    public async Task WritingIgnoredFiles_IsNotReported()
    {
        await SetUpAsync();

        await File.WriteAllTextAsync(Path.Combine(_repo, "bin", "out.dll"), "x");

        Assert.False(_changes.TryTake(out _, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Rebaseline_TheAppsOwnRefChangeIsNotReportedAgain()
    {
        await SetUpAsync();

        await Git("tag", "v1");
        _watcher.Rebaseline();

        Assert.DoesNotContain(RepositoryChangeKind.References, Drain());
    }
}
