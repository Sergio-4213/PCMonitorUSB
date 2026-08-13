namespace PCMonitorUSB.Config;

public sealed class AppConfig
{
    public int Port { get; set; } = 8765;
    public int UpdateIntervalMs { get; set; } = 1000;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowVram { get; set; } = true;
    public bool ShowNetwork { get; set; }
    public bool ShowDisk { get; set; }
    public bool ShowFps { get; set; }
    public float CpuElevatedTemperature { get; set; } = 75;
    public float CpuCriticalTemperature { get; set; } = 90;
    public float GpuElevatedTemperature { get; set; } = 75;
    public float GpuCriticalTemperature { get; set; } = 90;
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "auto";
    public bool AutoInstallApk { get; set; } = true;
    public bool EnableWakeOnLan { get; set; } = true;
    public bool RestrictAndroidModels { get; set; }
    public List<string> AllowedModelPrefixes { get; set; } = [];
    public List<ButtonConfig> Buttons { get; set; } = ButtonConfig.CreateDefaults();

    public void Normalize()
    {
        Port = Math.Clamp(Port, 1024, 65535);
        UpdateIntervalMs = UpdateIntervalMs switch
        {
            <= 750 => 500,
            <= 1500 => 1000,
            _ => 2000
        };
        CpuElevatedTemperature = Math.Clamp(CpuElevatedTemperature, 30, 110);
        CpuCriticalTemperature = Math.Clamp(CpuCriticalTemperature, CpuElevatedTemperature + 1, 120);
        GpuElevatedTemperature = Math.Clamp(GpuElevatedTemperature, 30, 110);
        GpuCriticalTemperature = Math.Clamp(GpuCriticalTemperature, GpuElevatedTemperature + 1, 120);
        Buttons ??= ButtonConfig.CreateDefaults();
        AllowedModelPrefixes ??= [];
        Language = Language?.ToLowerInvariant() is "pt" or "en" ? Language.ToLowerInvariant() : "auto";
    }
}

public sealed class ButtonConfig
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Target { get; set; }
    public bool Enabled { get; set; } = true;
    public bool BuiltIn { get; set; } = true;

    public static List<ButtonConfig> CreateDefaults() =>
    [
        new() { Id = "media_previous", Label = "ANTERIOR", Icon = "⏮", Action = "media_previous" },
        new() { Id = "media_play_pause", Label = "PLAY/PAUSE", Icon = "▶", Action = "media_play_pause" },
        new() { Id = "media_next", Label = "PRÓXIMA", Icon = "⏭", Action = "media_next" },
        new() { Id = "mute", Label = "MUTE", Icon = "M", Action = "mute" },
        new() { Id = "volume_down", Label = "VOL -", Icon = "−", Action = "volume_down" },
        new() { Id = "volume_up", Label = "VOL +", Icon = "+", Action = "volume_up" },
        new() { Id = "show_desktop", Label = "DESKTOP", Icon = "▣", Action = "show_desktop" },
        new() { Id = "open_task_manager", Label = "TAREFAS", Icon = "TM", Action = "open_task_manager", Enabled = false },
        new() { Id = "open_steam", Label = "STEAM", Icon = "S", Action = "open_steam", Enabled = false },
        new() { Id = "open_amd", Label = "AMD", Icon = "A", Action = "open_amd", Enabled = false },
        new() { Id = "custom_1", Label = "PERSONALIZADO 1", Action = "none", Enabled = false, BuiltIn = false },
        new() { Id = "custom_2", Label = "PERSONALIZADO 2", Action = "none", Enabled = false, BuiltIn = false },
        new() { Id = "custom_3", Label = "PERSONALIZADO 3", Action = "none", Enabled = false, BuiltIn = false },
        new() { Id = "custom_4", Label = "PERSONALIZADO 4", Action = "none", Enabled = false, BuiltIn = false }
    ];
}
