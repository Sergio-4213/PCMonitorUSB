using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using static PCMonitorUSB.Localization.AppLanguage;

namespace PCMonitorUSB.Core;

public interface IFpsProvider : IDisposable
{
    float? CurrentFps { get; }
    string Status { get; }
    string ActiveProcess { get; }
    void Start();
}

public sealed class PresentMonFpsProvider : IFpsProvider
{
    public const string Version = "2.5.1";
    public const string ExpectedSha256 = "9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191";
    private const string ResourceName = "PCMonitorUSB.Assets.PresentMon.PresentMon-2.5.1-x64.exe";
    private const string SessionName = "PCMonitorUSB-FPS";
    private static readonly TimeSpan SampleLifetime = TimeSpan.FromSeconds(2.5);

    private readonly object _gate = new();
    private readonly string _executablePath;
    private readonly Dictionary<FrameKey, Queue<FrameSample>> _samples = [];
    private readonly Dictionary<int, string> _processNames = [];
    private Process? _process;
    private int _applicationIndex = -1;
    private int _processIdIndex = -1;
    private int _swapChainIndex = -1;
    private int _frameTimeIndex = -1;
    private long _lastStartAttempt;
    private bool _disposed;
    private string _status = T("Aguardando o PresentMon", "Waiting for PresentMon");
    private string _activeProcess = "";

    public PresentMonFpsProvider(string dataRoot)
    {
        _executablePath = Path.Combine(dataRoot, "presentmon", Version, "PresentMon.exe");
    }

    public string Status { get { lock (_gate) return _status; } }
    public string ActiveProcess { get { lock (_gate) return _activeProcess; } }
    internal string ExecutablePath => _executablePath;
    internal void PrepareExecutable() => ExtractVerifiedBinary();

    public float? CurrentFps
    {
        get
        {
            EnsureRunning();
            var foregroundProcessId = GetForegroundProcessId();
            if (foregroundProcessId == 0) return null;
            var fps = GetFpsForProcess(foregroundProcessId);
            lock (_gate)
            {
                if (!fps.HasValue)
                {
                    _activeProcess = "";
                    _status = T("Ativo — aguardando jogo em primeiro plano", "Active — waiting for a foreground game");
                    return null;
                }
                _activeProcess = _processNames.GetValueOrDefault(foregroundProcessId, "");
                _status = $"{T("Ativo", "Active")} — {_activeProcess} — {fps.Value:0} FPS";
                return fps;
            }
        }
    }

    public void Start() => EnsureRunning();

    private void EnsureRunning()
    {
        lock (_gate)
        {
            if (_disposed || _process is { HasExited: false }) return;
            var now = Environment.TickCount64;
            if (now - _lastStartAttempt < 10_000) return;
            _lastStartAttempt = now;
        }

        try
        {
            ExtractVerifiedBinary();
            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                Arguments = "--output_stdout --no_console_stats --session_name " + SessionName +
                            " --stop_existing_session --no_track_gpu --no_track_input --no_track_display",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_executablePath)!
            };
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => { if (args.Data is not null) ProcessLine(args.Data); };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data) && args.Data.Contains("error", StringComparison.OrdinalIgnoreCase))
                    SetStatus("PresentMon: " + args.Data.Trim());
            };
            process.Exited += (_, _) => SetStoppedStatus();
            if (!process.Start()) throw new InvalidOperationException("PresentMon did not start.");
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (_gate)
            {
                if (_disposed)
                {
                    process.Kill(entireProcessTree: true);
                    process.Dispose();
                    return;
                }
                _process?.Dispose();
                _process = process;
                _status = T("Ativo — aguardando jogo em primeiro plano", "Active — waiting for a foreground game");
            }
            SimpleLog.Info($"FPS real ativado com PresentMon {Version}.");
        }
        catch (Exception ex)
        {
            SetStatus(T("PresentMon indisponível", "PresentMon unavailable"));
            SimpleLog.Error("Não foi possível iniciar a coleta real de FPS.", ex);
        }
    }

    internal void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var columns = ParseCsvLine(line);
        if (columns.Count == 0) return;
        if (string.Equals(columns[0], "Application", StringComparison.OrdinalIgnoreCase))
        {
            lock (_gate)
            {
                _applicationIndex = IndexOf(columns, "Application");
                _processIdIndex = IndexOf(columns, "ProcessID");
                _swapChainIndex = IndexOf(columns, "SwapChainAddress");
                _frameTimeIndex = IndexOf(columns, "MsBetweenPresents");
            }
            return;
        }

        int applicationIndex;
        int processIdIndex;
        int swapChainIndex;
        int frameTimeIndex;
        lock (_gate)
        {
            applicationIndex = _applicationIndex;
            processIdIndex = _processIdIndex;
            swapChainIndex = _swapChainIndex;
            frameTimeIndex = _frameTimeIndex;
        }
        var highestIndex = Math.Max(Math.Max(applicationIndex, processIdIndex), Math.Max(swapChainIndex, frameTimeIndex));
        if (applicationIndex < 0 || processIdIndex < 0 || swapChainIndex < 0 || frameTimeIndex < 0 ||
            highestIndex < 0 || columns.Count <= highestIndex ||
            !int.TryParse(columns[processIdIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
            !double.TryParse(columns[frameTimeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var frameTime) ||
            frameTime is < 0.5 or > 1000)
            return;

        var now = Stopwatch.GetTimestamp();
        var key = new FrameKey(processId, columns[swapChainIndex]);
        lock (_gate)
        {
            if (!_samples.TryGetValue(key, out var queue))
            {
                queue = new Queue<FrameSample>(256);
                _samples[key] = queue;
            }
            queue.Enqueue(new FrameSample(now, frameTime));
            while (queue.Count > 1000) queue.Dequeue();
            _processNames[processId] = columns[applicationIndex];
            if (_samples.Count > 256) Prune(now);
        }
    }

    internal static float? CalculateFps(IEnumerable<double> frameTimes)
    {
        var valid = frameTimes.Where(value => value is >= 0.5 and <= 1000 && double.IsFinite(value)).ToArray();
        if (valid.Length < 2) return null;
        var averageFrameTime = valid.Average();
        if (averageFrameTime <= 0) return null;
        return MathF.Round((float)Math.Clamp(1000d / averageFrameTime, 1d, 1000d), 1);
    }

    internal float? GetFpsForProcess(int processId)
    {
        var now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            Prune(now);
            var candidate = _samples
                .Where(pair => pair.Key.ProcessId == processId)
                .Select(pair => new
                {
                    Values = pair.Value.Select(sample => sample.FrameTimeMs).ToArray(),
                    Count = pair.Value.Count
                })
                .Where(item => item.Count >= 2)
                .OrderByDescending(item => item.Count)
                .FirstOrDefault();
            return candidate is null ? null : CalculateFps(candidate.Values);
        }
    }

    internal static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder(line.Length);
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else current.Append(character);
        }
        result.Add(current.ToString());
        return result;
    }

    private void Prune(long now)
    {
        var expiredKeys = new List<FrameKey>();
        foreach (var pair in _samples)
        {
            while (pair.Value.Count > 0 && Stopwatch.GetElapsedTime(pair.Value.Peek().Timestamp, now) > SampleLifetime)
                pair.Value.Dequeue();
            if (pair.Value.Count == 0) expiredKeys.Add(pair.Key);
        }
        foreach (var key in expiredKeys) _samples.Remove(key);
        var activePids = _samples.Keys.Select(key => key.ProcessId).ToHashSet();
        foreach (var pid in _processNames.Keys.Where(pid => !activePids.Contains(pid)).ToArray()) _processNames.Remove(pid);
    }

    private void ExtractVerifiedBinary()
    {
        if (File.Exists(_executablePath) && VerifyHash(_executablePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_executablePath)!);
        var temporaryPath = _executablePath + ".tmp";
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded PresentMon binary was not found.");
        using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            resource.CopyTo(destination);
        if (!VerifyHash(temporaryPath))
        {
            File.Delete(temporaryPath);
            throw new InvalidDataException("Embedded PresentMon SHA-256 verification failed.");
        }
        File.Move(temporaryPath, _executablePath, true);
    }

    private static bool VerifyHash(string path)
    {
        using var stream = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), ExpectedSha256, StringComparison.Ordinal);
    }

    private static int IndexOf(IReadOnlyList<string> columns, string value)
    {
        for (var index = 0; index < columns.Count; index++)
            if (string.Equals(columns[index], value, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }

    private void SetStatus(string status)
    {
        lock (_gate) _status = status;
    }

    private void SetStoppedStatus()
    {
        lock (_gate)
        {
            if (_status.StartsWith("PresentMon:", StringComparison.OrdinalIgnoreCase)) return;
            _status = T("PresentMon interrompido — nova tentativa automática", "PresentMon stopped — automatic retry pending");
        }
    }

    public void Dispose()
    {
        Process? process;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            process = _process;
            _process = null;
        }
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
            process?.WaitForExit(3000);
        }
        catch { }
        finally { process?.Dispose(); }
    }

    private static int GetForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out var processId);
        return unchecked((int)processId);
    }

    private readonly record struct FrameKey(int ProcessId, string SwapChain);
    private readonly record struct FrameSample(long Timestamp, double FrameTimeMs);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
