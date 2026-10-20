using GitUi.Application.Services;
using GitUi.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _settingsFilePath = Path.Combine(Path.GetTempPath(), $"gitui-di-settings-{Guid.NewGuid()}.json");

    [Fact]
    public void AddGitUiInfrastructure_ResolvesEveryApplicationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitUiInfrastructure(_settingsFilePath);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<RepositoryService>());
        Assert.NotNull(provider.GetRequiredService<CommitGraphService>());
        Assert.NotNull(provider.GetRequiredService<BranchService>());
        Assert.NotNull(provider.GetRequiredService<StagingService>());
        Assert.NotNull(provider.GetRequiredService<StashService>());
        Assert.NotNull(provider.GetRequiredService<RemoteService>());
        Assert.NotNull(provider.GetRequiredService<RecentRepositoriesService>());
    }

    public void Dispose()
    {
        if (File.Exists(_settingsFilePath))
        {
            File.Delete(_settingsFilePath);
        }
    }
}
