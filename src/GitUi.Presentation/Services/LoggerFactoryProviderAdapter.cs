using Microsoft.Extensions.Logging;

namespace GitUi.Presentation.Services;

internal sealed class LoggerFactoryProviderAdapter : ILoggerProvider
{
    private readonly ILoggerFactory _inner;

    public LoggerFactoryProviderAdapter(ILoggerFactory inner) => _inner = inner;

    public ILogger CreateLogger(string categoryName) => _inner.CreateLogger(categoryName);

    public void Dispose()
    {
    }
}
