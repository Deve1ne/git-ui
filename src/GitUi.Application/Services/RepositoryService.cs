using GitUi.Application.Abstractions;
using GitUi.Domain.Entities;
using GitUi.Domain.Exceptions;

namespace GitUi.Application.Services;

public sealed class RepositoryService
{
    private readonly IGitRepositoryReader _reader;

    public RepositoryService(IGitRepositoryReader reader) => _reader = reader;

    public async Task<GitRepository> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!_reader.IsValidRepository(path))
        {
            throw new GitRepositoryCorruptException($"'{path}' is not a valid git repository.", details: path);
        }

        return await _reader.GetRepositoryAsync(path, cancellationToken);
    }
}
