using GitUi.Application.Abstractions;
using GitUi.Domain.Entities;

namespace GitUi.Application.Services;

/// <summary>Outcome of checking the checked-out branch against its merge target.</summary>
public sealed record ConflictCheckResult(string? BranchName, string? Target, MergePrediction Prediction)
{
    public static ConflictCheckResult NotApplicable { get; } = new(null, null, MergePrediction.Clean);

    public bool IsApplicable => BranchName is not null && Target is not null;
}

/// <summary>
/// Proactive conflict detection, like GitKraken's Conflict Prevention: merges the checked-out branch
/// with its target branch in memory and reports the files that would conflict, before anyone merges.
/// The target is the one chosen for the repository, otherwise the remote's default branch.
/// </summary>
public sealed class ConflictDetectionService
{
    private static readonly string[] DefaultTargetNames = { "main", "master", "develop" };

    private readonly IGitRepositoryReader _reader;
    private readonly ISettingsStore _settingsStore;

    public ConflictDetectionService(IGitRepositoryReader reader, ISettingsStore settingsStore)
    {
        _reader = reader;
        _settingsStore = settingsStore;
    }

    /// <summary>
    /// Picks the branch to compare against: the repository's configured target if it still exists,
    /// else the branch origin/HEAD points at, else origin/main, origin/master, origin/develop, then
    /// the local ones. Never the checked-out branch itself.
    /// </summary>
    public static string? ChooseTarget(string headBranch, IReadOnlyList<GitBranch> branches, string? configuredTarget)
    {
        bool Usable(string name) => name != headBranch && branches.Any(b => b.Name == name);

        if (configuredTarget is not null && Usable(configuredTarget))
        {
            return configuredTarget;
        }

        // origin/HEAD is a symbolic ref to the remote's default branch: find the branch at its commit.
        var remoteHead = branches.FirstOrDefault(b => b.IsRemote && b.Name == "origin/HEAD");
        if (remoteHead is not null)
        {
            var defaultBranch = branches.FirstOrDefault(b => b.IsRemote && b.Name != "origin/HEAD" && b.Name.StartsWith("origin/", StringComparison.Ordinal)
                && b.TipSha == remoteHead.TipSha && DefaultTargetNames.Contains(b.Name["origin/".Length..]))
                ?? branches.FirstOrDefault(b => b.IsRemote && b.Name != "origin/HEAD" && b.TipSha == remoteHead.TipSha);
            if (defaultBranch is not null && Usable(defaultBranch.Name))
            {
                return defaultBranch.Name;
            }
        }

        return DefaultTargetNames.Select(n => "origin/" + n).Concat(DefaultTargetNames).FirstOrDefault(Usable);
    }

    public async Task<ConflictCheckResult> CheckAsync(string path, CancellationToken cancellationToken = default)
    {
        var repository = await _reader.GetRepositoryAsync(path, cancellationToken);
        if (repository.HeadSha is null || repository.CurrentBranch.StartsWith('('))
        {
            return ConflictCheckResult.NotApplicable;
        }

        var branches = await _reader.GetBranchesAsync(path, cancellationToken);
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var configured = settings.ConflictTargetByRepository?.GetValueOrDefault(Key(path));
        var target = ChooseTarget(repository.CurrentBranch, branches, configured);
        if (target is null)
        {
            return ConflictCheckResult.NotApplicable;
        }

        // When one side contains the other, merging is a fast-forward or a no-op: nothing can conflict.
        var ancestry = await _reader.GetAncestryAsync(path, repository.HeadSha, target, cancellationToken);
        var prediction = ancestry == GitAncestry.Diverged
            ? await _reader.PredictMergeAsync(path, repository.HeadSha, target, cancellationToken)
            : MergePrediction.Clean;

        return new ConflictCheckResult(repository.CurrentBranch, target, prediction);
    }

    /// <summary>Predicts a merge of two arbitrary revisions (for annotating merge / rebase menu entries).</summary>
    public async Task<MergePrediction> PredictAsync(string path, string ours, string theirs, CancellationToken cancellationToken = default)
    {
        var ancestry = await _reader.GetAncestryAsync(path, ours, theirs, cancellationToken);
        return ancestry == GitAncestry.Diverged
            ? await _reader.PredictMergeAsync(path, ours, theirs, cancellationToken)
            : MergePrediction.Clean;
    }

    public Task SetTargetAsync(string path, string target, CancellationToken cancellationToken = default) =>
        _settingsStore.UpdateAsync(settings =>
        {
            var targets = new Dictionary<string, string>(settings.ConflictTargetByRepository ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
            {
                [Key(path)] = target,
            };
            return settings with { ConflictTargetByRepository = targets };
        }, cancellationToken);

    private static string Key(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToLowerInvariant();
}
