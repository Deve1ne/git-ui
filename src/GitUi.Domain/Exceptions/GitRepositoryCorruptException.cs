namespace GitUi.Domain.Exceptions;

public sealed class GitRepositoryCorruptException : GitOperationException
{
    public GitRepositoryCorruptException(string message, string? details = null, Exception? innerException = null)
        : base(message, ErrorCode.RepositoryCorrupt, details, innerException)
    {
    }
}
