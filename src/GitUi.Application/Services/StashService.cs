using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

public sealed class StashService
{
    private readonly IGitRepositoryWriter _writer;

    public StashService(IGitRepositoryWriter writer) => _writer = writer;

    public Task SaveAsync(string path, string? message, CancellationToken cancellationToken = default)
        => _writer.StashSaveAsync(path, message, cancellationToken);

    public Task ApplyAsync(string path, int stashIndex, CancellationToken cancellationToken = default)
        => _writer.StashApplyAsync(path, stashIndex, cancellationToken);

    public Task PopAsync(string path, int stashIndex, CancellationToken cancellationToken = default)
        => _writer.StashPopAsync(path, stashIndex, cancellationToken);

    public Task DropAsync(string path, int stashIndex, CancellationToken cancellationToken = default)
        => _writer.StashDropAsync(path, stashIndex, cancellationToken);
}
