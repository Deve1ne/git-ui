using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

public enum GitFlowKind { Feature, Release, Hotfix }

/// <summary>
/// Git Flow settings, stored in the repository's git config under the same keys as the git-flow
/// extension (gitflow.branch.master, gitflow.prefix.feature…), so both tools agree.
/// </summary>
public sealed record GitFlowConfig(
    string ProductionBranch,
    string DevelopBranch,
    string FeaturePrefix = "feature/",
    string ReleasePrefix = "release/",
    string HotfixPrefix = "hotfix/",
    string VersionTagPrefix = "")
{
    public string Prefix(GitFlowKind kind) => kind switch
    {
        GitFlowKind.Feature => FeaturePrefix,
        GitFlowKind.Release => ReleasePrefix,
        _ => HotfixPrefix,
    };

    /// <summary>Features start from develop; releases and hotfixes too, except hotfixes start from production.</summary>
    public string BaseBranch(GitFlowKind kind) => kind == GitFlowKind.Hotfix ? ProductionBranch : DevelopBranch;
}

/// <summary>A branch Git Flow manages: <see cref="Name"/> without its prefix ("login" for "feature/login").</summary>
public sealed record GitFlowBranch(GitFlowKind Kind, string Name, string Branch);

/// <summary>
/// Git Flow without the git-flow extension: initialize (branch names and prefixes, develop created
/// from production when missing), start a feature / release / hotfix, and finish it — merged with
/// --no-ff into develop (and, for releases and hotfixes, into production with a version tag), then
/// deleted. Finishing again after resolving a conflict skips the merges already done.
/// </summary>
public sealed class GitFlowService
{
    private readonly IGitCommandRunner _git;

    public GitFlowService(IGitCommandRunner git) => _git = git;

    /// <summary>The repository's Git Flow settings, or null when Git Flow was never initialized.</summary>
    public async Task<GitFlowConfig?> GetConfigAsync(string path, CancellationToken cancellationToken = default)
    {
        var result = await _git.TryRunAsync(path, new[] { "config", "--local", "--get-regexp", @"^gitflow\." }, cancellationToken: cancellationToken);
        if (!result.Success)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var space = line.IndexOf(' ');
            values[space < 0 ? line.Trim() : line[..space]] = space < 0 ? string.Empty : line[(space + 1)..].Trim();
        }

        if (!values.TryGetValue("gitflow.branch.master", out var production) || !values.TryGetValue("gitflow.branch.develop", out var develop))
        {
            return null;
        }

        return new GitFlowConfig(
            production,
            develop,
            values.GetValueOrDefault("gitflow.prefix.feature", "feature/"),
            values.GetValueOrDefault("gitflow.prefix.release", "release/"),
            values.GetValueOrDefault("gitflow.prefix.hotfix", "hotfix/"),
            values.GetValueOrDefault("gitflow.prefix.versiontag", string.Empty));
    }

    /// <summary>Default settings for a repository: its main or master branch as production, "develop".</summary>
    public async Task<GitFlowConfig> SuggestConfigAsync(string path, CancellationToken cancellationToken = default)
    {
        var production = "main";
        foreach (var candidate in new[] { "main", "master" })
        {
            if (await BranchExistsAsync(path, candidate, cancellationToken))
            {
                production = candidate;
                break;
            }
        }

        return new GitFlowConfig(production, "develop");
    }

    public async Task InitializeAsync(string path, GitFlowConfig config, CancellationToken cancellationToken = default)
    {
        if (!await BranchExistsAsync(path, config.ProductionBranch, cancellationToken))
        {
            throw new InvalidOperationException($"The production branch '{config.ProductionBranch}' does not exist.");
        }

        if (string.Equals(config.ProductionBranch, config.DevelopBranch, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The production and development branches must be different.");
        }

        if (!await BranchExistsAsync(path, config.DevelopBranch, cancellationToken))
        {
            await _git.RunAsync(path, new[] { "branch", "--", config.DevelopBranch, config.ProductionBranch }, cancellationToken: cancellationToken);
        }

        foreach (var (key, value) in new[]
        {
            ("gitflow.branch.master", config.ProductionBranch),
            ("gitflow.branch.develop", config.DevelopBranch),
            ("gitflow.prefix.feature", config.FeaturePrefix),
            ("gitflow.prefix.release", config.ReleasePrefix),
            ("gitflow.prefix.hotfix", config.HotfixPrefix),
            ("gitflow.prefix.support", "support/"),
            ("gitflow.prefix.versiontag", config.VersionTagPrefix),
        })
        {
            await _git.RunAsync(path, new[] { "config", "--local", key, value }, cancellationToken: cancellationToken);
        }
    }

    /// <summary>Creates prefix+name from the right base branch and checks it out; returns the branch name.</summary>
    public async Task<string> StartAsync(string path, GitFlowConfig config, GitFlowKind kind, string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException("A name is needed.");
        }

        var branch = config.Prefix(kind) + name;
        var check = await _git.TryRunAsync(path, new[] { "check-ref-format", "--branch", branch }, cancellationToken: cancellationToken);
        if (!check.Success)
        {
            throw new InvalidOperationException($"'{branch}' is not a valid branch name.");
        }

        await _git.RunAsync(path, new[] { "switch", "-c", branch, config.BaseBranch(kind) }, cancellationToken: cancellationToken);
        return branch;
    }

    /// <summary>
    /// Finishes a feature (into develop) or a release / hotfix (into production with a tag, then into
    /// develop), then deletes its branch and leaves develop checked out. Stops on the first conflict;
    /// run it again once the merge is committed.
    /// </summary>
    /// <param name="tagMessage">Releases and hotfixes: the tag's message (annotated tag).</param>
    public async Task FinishAsync(string path, GitFlowConfig config, GitFlowBranch flow, string? tagMessage = null, CancellationToken cancellationToken = default)
    {
        // Like git-flow, develop gets the tag back from production (so it contains production's merge
        // commit); a feature goes straight into develop.
        var intoDevelop = flow.Branch;
        if (flow.Kind != GitFlowKind.Feature)
        {
            await MergeIntoAsync(path, flow.Branch, config.ProductionBranch, cancellationToken);
            var tag = config.VersionTagPrefix + flow.Name;
            if (!await RefExistsAsync(path, $"refs/tags/{tag}", cancellationToken))
            {
                await _git.RunAsync(path, new[] { "tag", "-a", tag, "-m", string.IsNullOrWhiteSpace(tagMessage) ? $"{flow.Kind} {flow.Name}" : tagMessage.Trim(), config.ProductionBranch }, cancellationToken: cancellationToken);
            }

            intoDevelop = tag;
        }

        await MergeIntoAsync(path, intoDevelop, config.DevelopBranch, cancellationToken);
        await _git.RunAsync(path, new[] { "branch", "-d", "--", flow.Branch }, cancellationToken: cancellationToken);
    }

    /// <summary>The local branches under the feature / release / hotfix prefixes.</summary>
    public static IReadOnlyList<GitFlowBranch> Classify(GitFlowConfig config, IEnumerable<string> localBranches)
    {
        var result = new List<GitFlowBranch>();
        foreach (var branch in localBranches)
        {
            foreach (var kind in Enum.GetValues<GitFlowKind>())
            {
                var prefix = config.Prefix(kind);
                if (prefix.Length > 0 && branch.StartsWith(prefix, StringComparison.Ordinal) && branch.Length > prefix.Length)
                {
                    result.Add(new GitFlowBranch(kind, branch[prefix.Length..], branch));
                    break;
                }
            }
        }

        return result.OrderBy(b => b.Kind).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task MergeIntoAsync(string path, string branch, string target, CancellationToken cancellationToken)
    {
        await _git.RunAsync(path, new[] { "switch", target }, cancellationToken: cancellationToken);
        var alreadyMerged = await _git.TryRunAsync(path, new[] { "merge-base", "--is-ancestor", branch, target }, cancellationToken: cancellationToken);
        if (!alreadyMerged.Success)
        {
            await _git.RunAsync(path, new[] { "merge", "--no-ff", "--no-edit", branch }, TimeSpan.FromMinutes(10), cancellationToken);
        }
    }

    private Task<bool> BranchExistsAsync(string path, string branch, CancellationToken cancellationToken) =>
        RefExistsAsync(path, $"refs/heads/{branch}", cancellationToken);

    private async Task<bool> RefExistsAsync(string path, string reference, CancellationToken cancellationToken) =>
        (await _git.TryRunAsync(path, new[] { "show-ref", "--verify", "--quiet", reference }, cancellationToken: cancellationToken)).Success;
}
