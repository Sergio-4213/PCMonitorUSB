using System.Text;

namespace PCMonitorUSB;

public static class SimpleLog
{
    private static readonly object Gate = new();
    private static string _path = Path.Combine(AppContext.BaseDirectory, "logs", "app.log");
    private const long MaxBytes = 1_048_576;

    public static void Initialize(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? error = null) => Write("ERROR", message, error);

    private static void Write(string level, string message, Exception? error)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                    File.Move(_path, _path + ".1", true);

                var safeMessage = message.Replace('\r', ' ').Replace('\n', ' ');
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {safeMessage}";
                if (error is not null)
                    line += $" | {error.GetType().Name}: {error.Message.Replace('\r', ' ').Replace('\n', ' ')}";
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never terminate the monitor.
        }
    }
}
