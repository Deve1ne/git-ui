namespace GitUi.Domain.Exceptions;

public sealed class GitMergeConflictException : GitOperationException
{
    public GitMergeConflictException(string message, string? details = null, Exception? innerException = null)
        : base(message, ErrorCode.MergeConflict, details, innerException)
    {
    }
}
