using GitUi.Application.Abstractions;
using GitUi.Domain.Entities;
using GitUi.Domain.ValueObjects;

namespace GitUi.Application.Services;

/// <summary>GitKraken's interactive rebase: list HEAD's commits since a base, then replay them as edited.</summary>
public sealed class InteractiveRebaseService
{
    private readonly IGitRepositoryReader _reader;
    private readonly IGitRepositoryWriter _writer;

    public InteractiveRebaseService(IGitRepositoryReader reader, IGitRepositoryWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    /// <summary>The commits to edit, oldest first.</summary>
    public async Task<IReadOnlyList<GitCommit>> GetCommitsAsync(string path, string ontoSha, CancellationToken cancellationToken = default)
    {
        var commits = await _reader.GetCommitsSinceAsync(path, ontoSha, cancellationToken);
        if (commits.Count == 0)
        {
            throw new InvalidOperationException("There is nothing to rebase: HEAD has no commits on top of this one.");
        }

        if (commits.Any(c => c.ParentShas.Count > 1))
        {
            throw new InvalidOperationException("These commits include a merge: an interactive rebase would flatten it. Pick a base after the merge.");
        }

        return commits;
    }

    /// <summary>
    /// For "Edit commit message": the base to rebase onto (the commit's parent) and the commits to
    /// replay, refusing a root commit or a commit that is not in HEAD's history.
    /// </summary>
    public async Task<(string OntoSha, IReadOnlyList<GitCommit> Commits)> PrepareRewordAsync(string path, string sha, CancellationToken cancellationToken = default)
    {
        var details = await _reader.GetCommitDetailsAsync(path, sha, cancellationToken);
        var parent = details.ParentShas.FirstOrDefault()
            ?? throw new InvalidOperationException("The first commit of the repository has no parent to rebase onto: its message cannot be edited here.");
        var commits = await GetCommitsAsync(path, parent, cancellationToken);
        if (!commits.Any(c => c.Sha == sha))
        {
            throw new InvalidOperationException("This commit is not part of the checked-out branch's history: check out a branch that contains it first.");
        }

        return (parent, commits);
    }

    public async Task RunAsync(string path, string ontoSha, IReadOnlyList<RebaseStep> steps, CancellationToken cancellationToken = default)
    {
        RebaseTodoBuilder.Validate(steps);

        // Reword messages live in files the todo's exec lines read; they must outlive this call,
        // since a rebase stopped on conflicts runs the remaining exec lines on Continue.
        var messageDirectory = Path.Combine(Path.GetTempPath(), "gitui-rebase", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(messageDirectory);
        var files = new Dictionary<RebaseStep, string>();
        foreach (var step in steps.Where(s => s.Action == RebaseAction.Reword))
        {
            var file = Path.Combine(messageDirectory, $"{step.Sha}.txt");
            await File.WriteAllTextAsync(file, step.NewMessage!.Trim() + "\n", new System.Text.UTF8Encoding(false), cancellationToken);
            files[step] = file;
        }

        var todo = RebaseTodoBuilder.Build(steps, step => files[step]);
        await _writer.InteractiveRebaseAsync(path, ontoSha, todo, cancellationToken);
    }
}
