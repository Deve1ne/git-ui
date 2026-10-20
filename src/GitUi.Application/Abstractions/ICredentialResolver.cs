namespace GitUi.Application.Abstractions;

public interface ICredentialResolver
{
    Task EnsureCredentialHelperConfiguredAsync(string repositoryPath, CancellationToken cancellationToken = default);
}
