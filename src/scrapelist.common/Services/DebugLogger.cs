namespace Scrapelist.Services;

public class DebugLogger : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly Lock _lock = new();

    public bool IsEnabled => _writer is not null;

    public DebugLogger(string? outputDirectory, bool enabled)
    {
        if (!enabled || outputDirectory is null) return;

        var dir = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(dir);
        var fileName = $"!debug-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var path = Path.Combine(dir, fileName);
        _writer = new StreamWriter(path, append: false) { AutoFlush = true };
        Log($"Debug logging started — output: {dir}");
    }

    public void Log(string message)
    {
        if (_writer is null) return;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [T{Environment.CurrentManagedThreadId:D3}] {message}";
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Log(string category, string message) => Log($"[{category}] {message}");

    public void Dispose()
    {
        if (_writer is null) return;
        Log("Debug logging stopped");
        _writer.Dispose();
    }
}
