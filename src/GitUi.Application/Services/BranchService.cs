using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

public sealed class BranchService
{
    private readonly IGitRepositoryReader _reader;
    private readonly IGitRepositoryWriter _writer;

    public BranchService(IGitRepositoryReader reader, IGitRepositoryWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public Task CreateBranchAsync(string path, string branchName, CancellationToken cancellationToken = default)
        => _writer.CreateBranchAsync(path, branchName, startPoint: null, cancellationToken);

    public Task CheckoutAsync(string path, string branchName, CancellationToken cancellationToken = default)
        => _writer.CheckoutAsync(path, branchName, cancellationToken);

    public async Task DeleteBranchAsync(string path, string branchName, bool force, CancellationToken cancellationToken = default)
    {
        var repository = await _reader.GetRepositoryAsync(path, cancellationToken);
        if (repository.CurrentBranch == branchName)
        {
            throw new InvalidOperationException($"Cannot delete the currently checked out branch '{branchName}'.");
        }

        await _writer.DeleteBranchAsync(path, branchName, force, cancellationToken);
    }
}
