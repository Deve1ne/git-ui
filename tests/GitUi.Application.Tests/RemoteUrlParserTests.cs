using GitUi.Application.Hosting;
using Xunit;

namespace GitUi.Application.Tests;

public class RemoteUrlParserTests
{
    [Theory]
    [InlineData("https://github.com/octo/hello.git")]
    [InlineData("https://github.com/octo/hello")]
    [InlineData("git@github.com:octo/hello.git")]
    [InlineData("ssh://git@github.com/octo/hello.git")]
    [InlineData("https://user@github.com/octo/hello.git")]
    public void GitHub(string url) =>
        Assert.Equal(new RemoteRepositoryRef(HostingProvider.GitHub, "octo", "hello"), RemoteUrlParser.Parse(url));

    [Theory]
    [InlineData("https://gitlab.com/group/sub/project.git")]
    [InlineData("git@gitlab.com:group/sub/project.git")]
    public void GitLab_KeepsTheWholeNamespace(string url)
    {
        var parsed = RemoteUrlParser.Parse(url);
        Assert.Equal(new RemoteRepositoryRef(HostingProvider.GitLab, "group/sub", "project"), parsed);
        Assert.Equal("group/sub/project", parsed!.FullPath);
    }

    [Theory]
    [InlineData("https://dev.azure.com/contoso/My%20Project/_git/web-app")]
    [InlineData("https://contoso@dev.azure.com/contoso/My%20Project/_git/web-app")]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/My%20Project/web-app")]
    [InlineData("https://contoso.visualstudio.com/My%20Project/_git/web-app")]
    [InlineData("https://contoso.visualstudio.com/DefaultCollection/My%20Project/_git/web-app")]
    public void AzureDevOps(string url) =>
        Assert.Equal(new RemoteRepositoryRef(HostingProvider.AzureDevOps, "contoso", "web-app", "My Project"), RemoteUrlParser.Parse(url));

    [Theory]
    [InlineData("https://bitbucket.org/team/tool.git")]
    [InlineData("https://someone@bitbucket.org/team/tool.git")]
    [InlineData("git@bitbucket.org:team/tool.git")]
    public void Bitbucket(string url) =>
        Assert.Equal(new RemoteRepositoryRef(HostingProvider.Bitbucket, "team", "tool"), RemoteUrlParser.Parse(url));

    [Theory]
    [InlineData("")]
    [InlineData("C:\\repos\\local")]
    [InlineData("/srv/git/project.git")]
    [InlineData("https://example.com/a/b.git")]
    [InlineData("https://github.com/only-owner")]
    [InlineData("https://dev.azure.com/org/project/repo")]
    public void OtherUrls_AreNotRecognized(string url) => Assert.Null(RemoteUrlParser.Parse(url));

    [Fact]
    public void WebUrls()
    {
        Assert.Equal("https://github.com/octo/hello", new RemoteRepositoryRef(HostingProvider.GitHub, "octo", "hello").WebUrl);
        Assert.Equal("https://dev.azure.com/contoso/My%20Project/_git/web-app",
            new RemoteRepositoryRef(HostingProvider.AzureDevOps, "contoso", "web-app", "My Project").WebUrl);
    }
}
