using GitUi.Domain.Entities;
using Xunit;

namespace GitUi.Domain.Tests;

public class EntitiesTests
{
    [Fact]
    public void GitCommit_SameValues_AreEqual()
    {
        var a = new GitCommit("sha1", "message", "author", DateTimeOffset.UnixEpoch, new[] { "parent1" });
        var b = new GitCommit("sha1", "message", "author", DateTimeOffset.UnixEpoch, new[] { "parent1" });

        Assert.Equal(a, b);
    }

    [Fact]
    public void FileChange_DefaultsToUnstaged()
    {
        var change = new FileChange("file.txt", FileChangeType.Modified, IsStaged: false);

        Assert.False(change.IsStaged);
        Assert.Equal(FileChangeType.Modified, change.ChangeType);
    }
}
