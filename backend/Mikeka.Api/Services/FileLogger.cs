namespace Mikeka.Api.Services;

/// <summary>
/// Writes everything the API window shows to logs/api-yyyyMMdd.log as well (one file per day, the last 14 days kept), so a
/// problem can still be read after the window was closed or the PC was turned off.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int KeepDays = 14;
    private readonly string _dir;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private DateOnly _day;

    public FileLoggerProvider(string dir) => _dir = dir;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (_writer is null || today != _day)
                {
                    _writer?.Dispose();
                    Directory.CreateDirectory(_dir);
                    _writer = new StreamWriter(new FileStream(Path.Combine(_dir, $"api-{today:yyyyMMdd}.log"),
                        FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true }; // flushed at once: power cuts happen
                    _day = today;
                    DeleteOld(today);
                }
                _writer.WriteLine(line);
            }
            catch { /* logging must never break a run */ }
        }
    }

    private void DeleteOld(DateOnly today)
    {
        foreach (var file in Directory.GetFiles(_dir, "api-*.log"))
            if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file)[4..], "yyyyMMdd", out var day) && day < today.AddDays(-KeepDays))
                File.Delete(file);
    }

    public void Dispose()
    {
        lock (_gate) { _writer?.Dispose(); _writer = null; }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTime.Now:HH:mm:ss} {Short(logLevel)} {category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            provider.Write(line);
        }

        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FAIL",
            _ => "CRIT",
        };
    }
}
