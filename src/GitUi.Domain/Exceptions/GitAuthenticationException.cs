namespace GitUi.Domain.Exceptions;

public sealed class GitAuthenticationException : GitOperationException
{
    public GitAuthenticationException(string message, string? details = null, Exception? innerException = null)
        : base(message, ErrorCode.AuthenticationFailed, details, innerException)
    {
    }
}
