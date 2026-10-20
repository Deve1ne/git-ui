using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace GitUi.Infrastructure.Logging;

public static class SerilogConfigurator
{
    public static ILoggerFactory CreateLoggerFactory(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDirectory, "gitui-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return new SerilogLoggerFactory(serilogLogger, dispose: true);
    }
}
