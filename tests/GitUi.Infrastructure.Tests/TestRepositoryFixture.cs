using LibGit2Sharp;

namespace GitUi.Infrastructure.Tests;

public sealed class TestRepositoryFixture : IDisposable
{
    public string Path { get; }

    public TestRepositoryFixture()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gitui-tests-" + Guid.NewGuid());
        Repository.Init(Path);

        // Pin a local identity so git CLI operations that create objects (commit, stash)
        // work even on machines without a global user.name/user.email.
        using var repo = new Repository(Path);
        repo.Config.Set("user.name", "Test User", ConfigurationLevel.Local);
        repo.Config.Set("user.email", "test@example.com", ConfigurationLevel.Local);
    }

    public string CommitFile(string fileName, string content, string message)
    {
        var filePath = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(filePath, content);

        using var repo = new Repository(Path);
        Commands.Stage(repo, fileName);
        var signature = new Signature("Test User", "test@example.com", DateTimeOffset.UtcNow);
        var commit = repo.Commit(message, signature, signature);
        return commit.Sha;
    }

    public void Dispose()
    {
        var directory = new DirectoryInfo(Path);
        if (!directory.Exists)
        {
            return;
        }

        foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        directory.Delete(recursive: true);
    }
}
