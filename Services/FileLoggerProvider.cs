using Microsoft.Extensions.Logging;

namespace FTP_WebApp.Services;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly string _prefix;
    private readonly object _sync = new();
    private bool _enabled;

    public FileLoggerProvider(string directory, string prefix)
    {
        _directory = directory;
        _prefix = prefix;
        try
        {
            Directory.CreateDirectory(directory);
            _enabled = true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            _enabled = false;
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    public void Dispose()
    {
    }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        if (!_enabled)
        {
            return;
        }

        var builder = new System.Text.StringBuilder();
        builder.Append('[')
               .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
               .Append("] [")
               .Append(level)
               .Append("] ")
               .Append(category)
               .Append(": ")
               .Append(message);
        if (exception is not null)
        {
            builder.AppendLine().Append(exception);
        }

        var file = Path.Combine(_directory, $"{_prefix}-{DateTime.Now:yyyyMMdd}.log");
        lock (_sync)
        {
            try
            {
                File.AppendAllText(file, builder.AppendLine().ToString());
            }
            catch (Exception writeException) when (writeException is UnauthorizedAccessException or IOException)
            {
                _enabled = false;
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(_category, logLevel, formatter(state, exception), exception);
        }
    }
}
