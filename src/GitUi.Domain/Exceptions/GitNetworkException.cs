namespace GitUi.Domain.Exceptions;

public sealed class GitNetworkException : GitOperationException
{
    public GitNetworkException(string message, string? details = null, Exception? innerException = null)
        : base(message, ErrorCode.NetworkError, details, innerException)
    {
    }
}
