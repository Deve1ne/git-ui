namespace GitUi.Domain.Exceptions;

public enum ErrorCode
{
    Unknown,
    AuthenticationFailed,
    MergeConflict,
    NetworkError,
    RepositoryCorrupt,
}
