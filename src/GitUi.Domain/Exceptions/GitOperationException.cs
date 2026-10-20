namespace GitUi.Domain.Exceptions;

public class GitOperationException : Exception
{
    public ErrorCode ErrorCode { get; }
    public string? Details { get; }

    public GitOperationException(string message, ErrorCode errorCode, string? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Details = details;
    }
}
