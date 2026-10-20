using GitUi.Domain.Exceptions;
using Xunit;

namespace GitUi.Domain.Tests;

public class ExceptionsTests
{
    [Fact]
    public void GitAuthenticationException_SetsAuthenticationFailedErrorCode()
    {
        var exception = new GitAuthenticationException("auth failed", details: "stderr text");

        Assert.Equal(ErrorCode.AuthenticationFailed, exception.ErrorCode);
        Assert.Equal("stderr text", exception.Details);
    }

    [Fact]
    public void GitOperationException_WrapsInnerException()
    {
        var inner = new InvalidOperationException("boom");
        var exception = new GitOperationException("wrapped", ErrorCode.Unknown, innerException: inner);

        Assert.Same(inner, exception.InnerException);
    }
}
