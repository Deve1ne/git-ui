using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Services;
using GitUi.Application.Hosting;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Hosting;
using GitUi.Infrastructure.Processes;
using GitUi.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace GitUi.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitUiInfrastructure(this IServiceCollection services, string settingsFilePath)
    {
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<GitCliRunner>();
        services.AddSingleton<GitAvailabilityChecker>();
        services.AddSingleton<IGitRepositoryReader, LibGit2SharpRepositoryReader>();
        services.AddSingleton<IGitRepositoryWriter, CliGitRepositoryWriter>();
        services.AddSingleton<ICredentialResolver, GitCliCredentialResolver>();
        services.AddSingleton<IRepositoryWatcher, FileSystemRepositoryWatcher>();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(settingsFilePath));

        services.AddSingleton<CommitGraphLayoutBuilder>();
        services.AddSingleton<RepositoryService>();
        services.AddSingleton<CommitGraphService>();
        services.AddSingleton<BranchService>();
        services.AddSingleton<StagingService>();
        services.AddSingleton<StashService>();
        services.AddSingleton<RemoteService>();
        services.AddSingleton<RecentRepositoriesService>();
        services.AddSingleton<UndoService>();
        services.AddSingleton<RefOperationsService>();
        services.AddSingleton<ConflictResolutionService>();
        services.AddSingleton<ConflictDetectionService>();
        services.AddSingleton<RepositorySetupService>();
        services.AddSingleton<GraphFilterService>();
        services.AddSingleton<PushService>();
        services.AddSingleton<InteractiveRebaseService>();
        services.AddSingleton<IGitCommandRunner, GitCommandRunner>();
        services.AddSingleton<BisectService>();
        services.AddSingleton<ReflogService>();
        services.AddSingleton<LfsService>();
        services.AddSingleton<GitFlowService>();
        services.AddSingleton<SigningService>();
        services.AddSingleton<ProfileService>();

        // Hosting integrations: one HttpClient for the four REST APIs, tokens next to the settings.
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        services.AddSingleton<IHostingClient>(new GitHubClient(http));
        services.AddSingleton<IHostingClient>(new GitLabClient(http));
        services.AddSingleton<IHostingClient>(new AzureDevOpsClient(http));
        services.AddSingleton<IHostingClient>(new BitbucketClient(http));
        services.AddSingleton<IHostingTokenStore>(_ =>
            new FileHostingTokenStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsFilePath))!, "tokens")));
        services.AddSingleton<IGitCredentialBridge, GitCredentialBridge>();
        services.AddSingleton<HostingService>();

        return services;
    }
}
