using System.Text.Json.Serialization;

namespace PCMonitorUSB.Core;

public sealed record CpuStats(
    string Name,
    float? Temperature,
    float? Usage,
    float? Clock,
    float? Power);

public sealed record GpuStats(
    string Name,
    float? Temperature,
    float? Hotspot,
    float? Usage,
    float? Clock,
    float? VramClock,
    float? VramUsed,
    float? VramTotal,
    float? Power,
    float? FanRPM,
    float? FanPercent);

public sealed record RamStats(float Used, float Total, float Usage);
public sealed record NetworkStats(float? Download, float? Upload);
public sealed record DiskStats(float? Activity, float? MainUsage);

public sealed record SystemProfile(
    string ComputerName,
    string OperatingSystem,
    string Motherboard,
    string Cpu,
    string PrimaryGpu,
    IReadOnlyList<string> Gpus,
    float RamTotal)
{
    public static SystemProfile Empty { get; } = new(
        Environment.MachineName,
        "Windows não identificado",
        "Placa-mãe não identificada",
        "CPU não identificada",
        "GPU não identificada",
        Array.Empty<string>(),
        0);
}

public sealed record StatsSnapshot(
    DateTimeOffset Timestamp,
    CpuStats Cpu,
    GpuStats Gpu,
    RamStats Ram,
    NetworkStats Network,
    DiskStats Disk,
    float? Fps)
{
    public static StatsSnapshot Empty { get; } = new(
        DateTimeOffset.UtcNow,
        new CpuStats("CPU não identificada", null, null, null, null),
        new GpuStats("GPU não identificada", null, null, null, null, null, null, null, null, null, null),
        new RamStats(0, 0, 0),
        new NetworkStats(null, null),
        new DiskStats(null, null),
        null);
}

public sealed record RawSensor(
    string Hardware,
    string HardwareType,
    string Sensor,
    string SensorType,
    float? Value,
    string Identifier,
    string HardwareIdentifier = "");

public interface IStatsProvider
{
    StatsSnapshot Current { get; }
    SystemProfile Profile { get; }
    IReadOnlyList<RawSensor> DetectedSensors { get; }
}
