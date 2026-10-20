using GitUi.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GitUi.Infrastructure.Tests;

public class SerilogConfiguratorTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"gitui-logs-{Guid.NewGuid()}");

    [Fact]
    public void CreateLoggerFactory_LoggedMessage_WritesToLogFile()
    {
        using (var factory = SerilogConfigurator.CreateLoggerFactory(_logDirectory))
        {
            var logger = factory.CreateLogger("test");
            logger.LogInformation("hello from test");
        }

        var files = Directory.GetFiles(_logDirectory, "gitui-*.log");
        Assert.NotEmpty(files);
        Assert.Contains("hello from test", File.ReadAllText(files[0]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }
}
