using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace PCMonitorUSB.Core;

public sealed class HardwareMonitor : IStatsProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly Computer _computer;
    private readonly int _intervalMs;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private StatsSnapshot _current = StatsSnapshot.Empty;
    private SystemProfile _profile = SystemProfile.Empty;
    private IReadOnlyList<RawSensor> _detected = Array.Empty<RawSensor>();
    private string _cpuTemperatureSource = "Aguardando leitura";
    private long _lastNetworkBytesReceived;
    private long _lastNetworkBytesSent;
    private DateTimeOffset _lastNetworkSample;
    private string? _selectedGpuKey;
    private ulong _lastCpuIdle;
    private ulong _lastCpuKernel;
    private ulong _lastCpuUser;
    private bool _hasCpuTimeSample;

    public HardwareMonitor(int intervalMs)
    {
        _intervalMs = Math.Clamp(intervalMs, 500, 2000);
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsNetworkEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = false,
            IsBatteryEnabled = false
        };
    }

    public StatsSnapshot Current
    {
        get { lock (_gate) return _current; }
    }

    public IReadOnlyList<RawSensor> DetectedSensors
    {
        get { lock (_gate) return _detected; }
    }

    public SystemProfile Profile
    {
        get { lock (_gate) return _profile; }
    }

    public string CpuTemperatureSource
    {
        get { lock (_gate) return _cpuTemperatureSource; }
    }

    public void Start()
    {
        if (_loop is not null) return;
        _computer.Open();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
        SimpleLog.Info($"Coleta de sensores iniciada em {_intervalMs} ms.");
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_intervalMs));
        do
        {
            try
            {
                Collect();
            }
            catch (Exception ex)
            {
                SimpleLog.Error("Erro ao atualizar sensores.", ex);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        } while (!cancellationToken.IsCancellationRequested);
    }

    public void Collect()
    {
        var raw = new List<RawSensor>(128);
        foreach (var hardware in _computer.Hardware)
            VisitHardware(hardware, raw);

        var now = DateTimeOffset.UtcNow;
        var cpuHardware = raw.Where(x => IsHardware(x, "Cpu")).ToArray();
        var allGpuHardware = raw.Where(x => IsHardware(x, "GpuAmd", "GpuNvidia", "GpuIntel")).ToArray();
        var gpuHardware = SelectPrimaryGpuSensors(allGpuHardware, _selectedGpuKey, out var selectedGpuKey);
        _selectedGpuKey = selectedGpuKey;
        var motherboardHardware = raw.Where(x => IsHardware(x, "Motherboard", "SuperIO", "EmbeddedController")).ToArray();

        var cpuName = FirstHardwareName(cpuHardware, GetWindowsCpuName());
        var gpuName = FirstHardwareName(gpuHardware, "GPU não identificada");

        var cpuTemperature = PickCpuTemperature(cpuHardware, motherboardHardware, out var cpuTemperatureSource);
        var cpu = new CpuStats(
            cpuName,
            cpuTemperature,
            Pick(cpuHardware, "Load", CpuUsageScore) ?? GetWindowsCpuUsage(),
            PickCpuClock(cpuHardware),
            Pick(cpuHardware, "Power", CpuPowerScore));

        var vramTotal = NormalizeGigabytes(Pick(gpuHardware, ["SmallData", "Data"], VramTotalScore));
        var vramUsed = NormalizeGigabytes(Pick(gpuHardware, ["SmallData", "Data"], VramUsedScore));
        var gpu = new GpuStats(
            gpuName,
            Pick(gpuHardware, "Temperature", GpuCoreTemperatureScore),
            Pick(gpuHardware, "Temperature", GpuHotspotScore),
            Pick(gpuHardware, "Load", GpuUsageScore),
            Pick(gpuHardware, "Clock", GpuCoreClockScore),
            Pick(gpuHardware, "Clock", GpuMemoryClockScore),
            vramUsed,
            vramTotal,
            Pick(gpuHardware, "Power", GpuPowerScore),
            Pick(gpuHardware, "Fan", GpuFanScore),
            Pick(gpuHardware, "Control", GpuFanPercentScore));

        var memory = GetMemoryStats();
        var gpuNames = allGpuHardware
            .GroupBy(GpuGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Select(x => x.Hardware).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => string.Equals(x, gpuName, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var profile = new SystemProfile(
            Environment.MachineName,
            GetWindowsName(),
            GetMotherboardName(_computer.Hardware),
            cpuName,
            gpuName,
            gpuNames,
            memory.Total);
        var network = GetNetworkStats(now);
        var diskActivity = Pick(raw.Where(x => IsHardware(x, "Storage")).ToArray(), "Load", DiskActivityScore);
        var diskUsage = GetMainDiskUsage();
        var snapshot = new StatsSnapshot(now, cpu, gpu, memory, network, new DiskStats(diskActivity, diskUsage), null);

        lock (_gate)
        {
            _detected = raw;
            _current = snapshot;
            _profile = profile;
            _cpuTemperatureSource = cpuTemperatureSource;
        }
    }

    private static void VisitHardware(IHardware hardware, List<RawSensor> raw)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            raw.Add(new RawSensor(
                hardware.Name,
                hardware.HardwareType.ToString(),
                sensor.Name,
                sensor.SensorType.ToString(),
                sensor.Value,
                sensor.Identifier.ToString(),
                hardware.Identifier.ToString()));
        }

        foreach (var subHardware in hardware.SubHardware)
            VisitHardware(subHardware, raw);
    }

    public static RawSensor[] SelectPrimaryGpuSensors(
        IReadOnlyList<RawSensor> sensors,
        string? preferredKey,
        out string? selectedKey)
    {
        var candidates = sensors
            .GroupBy(GpuGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Key = group.Key,
                Sensors = group.ToArray(),
                Score = GpuCapabilityScore(group.ToArray())
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            selectedKey = null;
            return [];
        }

        var best = candidates[0];
        var preferred = candidates.FirstOrDefault(x => string.Equals(x.Key, preferredKey, StringComparison.OrdinalIgnoreCase));
        var selected = preferred is not null && preferred.Score >= best.Score - 150 ? preferred : best;
        selectedKey = selected.Key;
        return selected.Sensors;
    }

    private static string GpuGroupKey(RawSensor sensor) =>
        !string.IsNullOrWhiteSpace(sensor.HardwareIdentifier)
            ? sensor.HardwareIdentifier
            : sensor.HardwareType + "|" + sensor.Hardware;

    private static int GpuCapabilityScore(IReadOnlyList<RawSensor> sensors)
    {
        if (sensors.Count == 0) return 0;
        var score = sensors.Count(x => x.Value.HasValue) * 3;
        var hardwareType = sensors[0].HardwareType;
        if (hardwareType.Equals("GpuAmd", StringComparison.OrdinalIgnoreCase) ||
            hardwareType.Equals("GpuNvidia", StringComparison.OrdinalIgnoreCase)) score += 250;

        if (Pick(sensors, "Temperature", GpuCoreTemperatureScore).HasValue) score += 100;
        if (Pick(sensors, "Load", GpuUsageScore).HasValue) score += 100;
        if (Pick(sensors, "Clock", GpuCoreClockScore).HasValue) score += 100;
        if (Pick(sensors, "Power", GpuPowerScore).HasValue) score += 80;
        if (Pick(sensors, "Fan", GpuFanScore).HasValue) score += 40;
        var vram = NormalizeGigabytes(Pick(sensors, ["SmallData", "Data"], VramTotalScore));
        if (vram.HasValue) score += 100 + (int)Math.Min(640, vram.Value * 40);
        return score;
    }

    private static string GetWindowsCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var name = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch
        {
            // LibreHardwareMonitor remains the primary CPU name source.
        }
        return "CPU não identificada";
    }

    private static string GetWindowsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string;
            var displayVersion = key?.GetValue("DisplayVersion") as string;
            var build = key?.GetValue("CurrentBuildNumber") as string;
            if (int.TryParse(build, out var buildNumber) && buildNumber >= 22000 &&
                product?.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) == true)
                product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
            var parts = new[] { product, displayVersion, string.IsNullOrWhiteSpace(build) ? null : "build " + build }
                .Where(x => !string.IsNullOrWhiteSpace(x));
            var result = string.Join(" • ", parts);
            if (!string.IsNullOrWhiteSpace(result)) return result;
        }
        catch
        {
            // RuntimeInformation below is a safe fallback.
        }
        return RuntimeInformation.OSDescription.Trim();
    }

    private static string GetMotherboardName(IEnumerable<IHardware> hardware)
    {
        var lhmName = hardware
            .Where(x => x.HardwareType == HardwareType.Motherboard)
            .Select(x => x.Name)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("Motherboard", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(lhmName)) return lhmName;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var manufacturer = key?.GetValue("BaseBoardManufacturer") as string;
            var product = key?.GetValue("BaseBoardProduct") as string;
            var result = string.Join(" ", new[] { manufacturer, product }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
            if (!string.IsNullOrWhiteSpace(result)) return result;
        }
        catch
        {
            // A motherboard may not expose SMBIOS strings.
        }
        return "Placa-mãe não identificada";
    }

    private float? GetWindowsCpuUsage()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return null;
        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);
        if (!_hasCpuTimeSample)
        {
            _lastCpuIdle = idle;
            _lastCpuKernel = kernel;
            _lastCpuUser = user;
            _hasCpuTimeSample = true;
            return null;
        }

        var idleDelta = idle - _lastCpuIdle;
        var kernelDelta = kernel - _lastCpuKernel;
        var userDelta = user - _lastCpuUser;
        _lastCpuIdle = idle;
        _lastCpuKernel = kernel;
        _lastCpuUser = user;
        var total = kernelDelta + userDelta;
        if (total == 0 || idleDelta > total) return null;
        return MathF.Round((float)(total - idleDelta) / total * 100f, 1);
    }

    private static ulong ToUInt64(NativeFileTime value) => ((ulong)value.High << 32) | value.Low;

    private static string FirstHardwareName(IEnumerable<RawSensor> sensors, string fallback) =>
        sensors.Select(x => x.Hardware).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? fallback;

    private static bool IsHardware(RawSensor sensor, params string[] types) =>
        types.Any(type => string.Equals(sensor.HardwareType, type, StringComparison.OrdinalIgnoreCase));

    public static float? Pick(IReadOnlyList<RawSensor> sensors, string sensorType, Func<RawSensor, int> score) =>
        Pick(sensors, [sensorType], score);

    public static float? Pick(IReadOnlyList<RawSensor> sensors, IReadOnlyList<string> sensorTypes, Func<RawSensor, int> score)
    {
        var best = sensors
            .Where(x => x.Value.HasValue && sensorTypes.Any(t => string.Equals(x.SensorType, t, StringComparison.OrdinalIgnoreCase)))
            .Select(x => new { Sensor = x, Score = score(x) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Sensor.Identifier, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return best is null ? null : Round(best.Sensor.Value);
    }

    private static float? PickCpuClock(IReadOnlyList<RawSensor> sensors)
    {
        var preferred = Pick(sensors, "Clock", CpuClockScore);
        if (preferred.HasValue) return preferred;

        var cores = sensors.Where(x =>
            x.Value.HasValue &&
            x.Value.Value > 100 &&
            string.Equals(x.SensorType, "Clock", StringComparison.OrdinalIgnoreCase) &&
            ContainsAny(x.Sensor, "core #", "cpu core"))
            .Select(x => x.Value!.Value)
            .ToArray();
        return cores.Length == 0 ? null : Round(cores.Average());
    }

    private static int CpuTemperatureScore(RawSensor x) => !IsPlausibleTemperature(x.Value) ? 0 : ScoreName(x.Sensor,
        (110, "cpu package"), (105, "core (tctl/tdie)"), (100, "tctl/tdie"), (95, "tdie"),
        (90, "tctl"), (85, "core max"), (40, "ccd"));
    private static int MotherboardCpuTemperatureScore(RawSensor x)
    {
        if (!IsPlausibleTemperature(x.Value) || ContainsAny(x.Sensor,
                "vrm", "mos", "pch", "chipset", "system", "ambient", "aux", "gpu")) return 0;
        return ScoreName(x.Sensor,
            (100, "cpu core"), (98, "cpu package"), (95, "cpu (tctl/tdie)"),
            (90, "cpu socket"), (85, "cpu"), (80, "tctl"));
    }
    private static int CpuUsageScore(RawSensor x) => ScoreName(x.Sensor,
        (100, "cpu total"), (95, "total cpu"), (90, "total"), (80, "cpu core max"));
    private static int CpuClockScore(RawSensor x) => x.Value is null or < 100 or > 10000 ? 0 : ScoreName(x.Sensor,
        (100, "cores (average)"), (100, "core average"), (95, "cpu core average"), (80, "effective clock"), (20, "core #"));
    private static int CpuPowerScore(RawSensor x) => x.Value is null or <= 0 or > 1000 ? 0 : ScoreName(x.Sensor,
        (100, "cpu package"), (95, "package"), (90, "cpu cores"), (80, "ppt"));
    private static int GpuCoreTemperatureScore(RawSensor x)
    {
        if (!IsPlausibleTemperature(x.Value) || ContainsAny(x.Sensor, "hot spot", "hotspot", "junction", "memory")) return 0;
        return ScoreName(x.Sensor, (100, "gpu core"), (90, "core"), (60, "gpu"));
    }
    private static int GpuHotspotScore(RawSensor x) => !IsPlausibleTemperature(x.Value) ? 0 : ScoreName(x.Sensor,
        (100, "gpu hot spot"), (100, "gpu hotspot"), (95, "hot spot"), (95, "hotspot"), (90, "junction"));
    private static int GpuUsageScore(RawSensor x)
    {
        if (ContainsAny(x.Sensor, "memory", "controller", "video engine", "d3d")) return 0;
        return ScoreName(x.Sensor, (100, "gpu core"), (90, "gpu load"), (70, "core"), (50, "gpu"));
    }
    private static int GpuCoreClockScore(RawSensor x)
    {
        if (ContainsAny(x.Sensor, "memory", "vram", "soc")) return 0;
        return ScoreName(x.Sensor, (100, "gpu core"), (90, "core"), (50, "gpu"));
    }
    private static int GpuMemoryClockScore(RawSensor x) => ScoreName(x.Sensor,
        (100, "gpu memory"), (95, "vram"), (90, "memory"));
    private static int VramUsedScore(RawSensor x) => ScoreName(x.Sensor,
        (100, "gpu memory used"), (98, "vram used"), (90, "dedicated memory used"), (70, "memory used"));
    private static int VramTotalScore(RawSensor x) => ScoreName(x.Sensor,
        (100, "gpu memory total"), (98, "vram total"), (90, "dedicated memory total"), (70, "memory total"));
    private static int GpuPowerScore(RawSensor x) => ScoreName(x.Sensor,
        (100, "gpu package"), (95, "total board"), (90, "gpu total"), (80, "gpu core"), (60, "gpu"));
    private static int GpuFanScore(RawSensor x) => ScoreName(x.Sensor,
        (100, "gpu fan"), (90, "fan #1"), (80, "fan"));
    private static int GpuFanPercentScore(RawSensor x) => ScoreName(x.Sensor,
        (100, "gpu fan"), (90, "fan #1"), (80, "fan"));
    private static int DiskActivityScore(RawSensor x) => ScoreName(x.Sensor,
        (100, "total activity"), (90, "activity"), (80, "total"));

    private static int ScoreName(string name, params (int Score, string Token)[] choices)
    {
        foreach (var choice in choices)
            if (name.Contains(choice.Token, StringComparison.OrdinalIgnoreCase)) return choice.Score;
        return 0;
    }

    private static bool ContainsAny(string text, params string[] tokens) =>
        tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool IsPlausibleTemperature(float? value) => value is > 0 and <= 125;

    internal static float? PickCpuTemperature(
        IReadOnlyList<RawSensor> cpuSensors,
        IReadOnlyList<RawSensor> motherboardSensors,
        out string source)
    {
        var direct = Pick(cpuSensors, "Temperature", CpuTemperatureScore);
        if (direct.HasValue)
        {
            source = "CPU Package (LibreHardwareMonitor)";
            return direct;
        }

        var fallback = Pick(motherboardSensors, "Temperature", MotherboardCpuTemperatureScore);
        if (fallback.HasValue)
        {
            source = "Sensor CPU da placa-mãe (LibreHardwareMonitor)";
            return fallback;
        }

        source = "Indisponível — execute como administrador e verifique o suporte do hardware";
        return null;
    }

    private static float? NormalizeGigabytes(float? value)
    {
        if (!value.HasValue || value.Value < 0) return null;
        return Round(value.Value > 256 ? value.Value / 1024f : value.Value);
    }

    private static float? Round(float? value) => value.HasValue && float.IsFinite(value.Value)
        ? MathF.Round(value.Value, 2)
        : null;

    private static RamStats GetMemoryStats()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status) || status.TotalPhysical == 0)
            return new RamStats(0, 0, 0);
        var total = status.TotalPhysical / 1024f / 1024f / 1024f;
        var available = status.AvailablePhysical / 1024f / 1024f / 1024f;
        var used = Math.Max(0, total - available);
        return new RamStats(MathF.Round(used, 2), MathF.Round(total, 2), MathF.Round(used / total * 100f, 1));
    }

    private NetworkStats GetNetworkStats(DateTimeOffset now)
    {
        long received = 0;
        long sent = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                var stats = nic.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch
            {
                // Adapters can disappear while enumerating.
            }
        }

        if (_lastNetworkSample == default || received < _lastNetworkBytesReceived || sent < _lastNetworkBytesSent)
        {
            _lastNetworkBytesReceived = received;
            _lastNetworkBytesSent = sent;
            _lastNetworkSample = now;
            return new NetworkStats(null, null);
        }

        var seconds = (now - _lastNetworkSample).TotalSeconds;
        if (seconds <= 0) return new NetworkStats(null, null);
        var download = (received - _lastNetworkBytesReceived) / seconds / 1024d / 1024d;
        var upload = (sent - _lastNetworkBytesSent) / seconds / 1024d / 1024d;
        _lastNetworkBytesReceived = received;
        _lastNetworkBytesSent = sent;
        _lastNetworkSample = now;
        return new NetworkStats((float)Math.Round(download, 2), (float)Math.Round(upload, 2));
    }

    private static float? GetMainDiskUsage()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            var drive = DriveInfo.GetDrives().FirstOrDefault(x =>
                x.IsReady && string.Equals(x.RootDirectory.FullName, root, StringComparison.OrdinalIgnoreCase));
            if (drive is null || drive.TotalSize <= 0) return null;
            return MathF.Round((float)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100f, 1);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        _computer.Close();
        _cts?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out NativeFileTime idleTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);
}
