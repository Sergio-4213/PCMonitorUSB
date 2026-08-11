using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PCMonitorUSB.Config;

namespace PCMonitorUSB.Commands;

public sealed record CommandResult(bool Success, string? Error = null);

public sealed class CommandService
{
    private readonly ConfigStore _config;

    public CommandService(ConfigStore config) => _config = config;

    public CommandResult Execute(string commandId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 64)
                return Fail("Comando inválido.");

            var button = _config.Current.Buttons.FirstOrDefault(x =>
                x.Enabled && string.Equals(x.Id, commandId, StringComparison.OrdinalIgnoreCase));
            if (button is null)
                return Fail("Comando não permitido ou desativado.");

            var success = button.Action.ToLowerInvariant() switch
            {
                "media_play_pause" => InputSender.Tap(0xB3),
                "media_next" => InputSender.Tap(0xB0),
                "media_previous" => InputSender.Tap(0xB1),
                "volume_up" => InputSender.Tap(0xAF),
                "volume_down" => InputSender.Tap(0xAE),
                "mute" => InputSender.Tap(0xAD),
                "show_desktop" => InputSender.Shortcut([0x5B, 0x44]),
                "open_task_manager" => StartExecutable("taskmgr.exe"),
                "open_steam" => OpenSteam(),
                "open_amd" => OpenAmd(),
                "open_program" when !button.BuiltIn => OpenConfiguredProgram(button.Target),
                "open_url" when !button.BuiltIn => OpenConfiguredUrl(button.Target),
                "hotkey" when !button.BuiltIn => InputSender.ConfiguredShortcut(button.Target),
                _ => false
            };

            if (!success) return Fail("A ação não pôde ser executada.");
            SimpleLog.Info($"Comando executado: {button.Id}.");
            return new CommandResult(true);
        }
        catch (Exception ex)
        {
            SimpleLog.Error($"Erro de comando: {commandId}.", ex);
            return new CommandResult(false, "Falha ao executar comando.");
        }
    }

    public bool IsAvailable(ButtonConfig button) => button.Action.ToLowerInvariant() switch
    {
        "open_amd" => FindAmdExecutable() is not null || Process.GetProcessesByName("RadeonSoftware").Length > 0,
        "open_steam" => FindSteamExecutable() is not null || Process.GetProcessesByName("steam").Length > 0,
        "open_program" when !button.BuiltIn => IsAllowedProgram(button.Target),
        "open_url" when !button.BuiltIn => IsAllowedUrl(button.Target),
        "hotkey" when !button.BuiltIn => InputSender.CanParseShortcut(button.Target),
        "none" => false,
        _ => true
    };

    private static CommandResult Fail(string message)
    {
        SimpleLog.Warn(message);
        return new CommandResult(false, message);
    }

    private static bool StartExecutable(string path)
    {
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        return true;
    }

    private static bool OpenConfiguredProgram(string? target) =>
        IsAllowedProgram(target) && StartExecutable(Path.GetFullPath(target!));

    private static bool OpenConfiguredUrl(string? target)
    {
        if (!IsAllowedUrl(target)) return false;
        return StartExecutable(target!);
    }

    private static bool IsAllowedProgram(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        try
        {
            var full = Path.GetFullPath(target);
            var extension = Path.GetExtension(full);
            return File.Exists(full) && (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                                         extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static bool IsAllowedUrl(string? target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
        (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals("steam", StringComparison.OrdinalIgnoreCase));

    private static bool OpenSteam()
    {
        var running = Process.GetProcessesByName("steam").FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);
        if (running is not null)
        {
            ShowWindow(running.MainWindowHandle, 9);
            return SetForegroundWindow(running.MainWindowHandle);
        }

        var path = FindSteamExecutable();
        return path is not null ? StartExecutable(path) : StartExecutable("steam://open/main");
    }

    private static bool OpenAmd()
    {
        var running = Process.GetProcessesByName("RadeonSoftware").FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);
        if (running is not null)
        {
            ShowWindow(running.MainWindowHandle, 9);
            return SetForegroundWindow(running.MainWindowHandle);
        }

        var path = FindAmdExecutable();
        return path is not null && StartExecutable(path);
    }

    public static string? FindAmdExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AMD", "CNext", "CNext", "RadeonSoftware.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AMD", "CNext", "CNext", "AMDRSSrcExt.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? FindSteamExecutable()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Valve\Steam");
                var installPath = key?.GetValue("SteamPath") as string ?? key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(installPath))
                {
                    var executable = Path.Combine(installPath.Replace('/', Path.DirectorySeparatorChar), "steam.exe");
                    if (File.Exists(executable)) return executable;
                }
            }
            catch { }
        }

        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}

internal static class InputSender
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;

    private static readonly IReadOnlyDictionary<string, ushort> NamedKeys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        ["CTRL"] = 0x11, ["CONTROL"] = 0x11, ["ALT"] = 0x12, ["SHIFT"] = 0x10,
        ["WIN"] = 0x5B, ["WINDOWS"] = 0x5B, ["ENTER"] = 0x0D, ["ESC"] = 0x1B,
        ["SPACE"] = 0x20, ["TAB"] = 0x09, ["UP"] = 0x26, ["DOWN"] = 0x28,
        ["LEFT"] = 0x25, ["RIGHT"] = 0x27, ["HOME"] = 0x24, ["END"] = 0x23,
        ["PGUP"] = 0x21, ["PGDN"] = 0x22, ["DELETE"] = 0x2E, ["BACKSPACE"] = 0x08
    };

    public static bool Tap(ushort key) => Shortcut([key]);

    public static bool Shortcut(IReadOnlyList<ushort> keys)
    {
        if (keys.Count == 0 || keys.Count > 5) return false;
        var inputs = new List<Input>(keys.Count * 2);
        foreach (var key in keys) inputs.Add(Create(key, false));
        for (var i = keys.Count - 1; i >= 0; i--) inputs.Add(Create(keys[i], true));
        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) == (uint)inputs.Count;
    }

    public static bool ConfiguredShortcut(string? text) => TryParseShortcut(text, out var keys) && Shortcut(keys);
    public static bool CanParseShortcut(string? text) => TryParseShortcut(text, out _);

    private static bool TryParseShortcut(string? text, out ushort[] keys)
    {
        keys = Array.Empty<ushort>();
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 5) return false;
        var parsed = new List<ushort>(parts.Length);
        foreach (var part in parts)
        {
            if (NamedKeys.TryGetValue(part, out var named)) parsed.Add(named);
            else if (part.Length == 1 && char.IsLetterOrDigit(part[0])) parsed.Add(char.ToUpperInvariant(part[0]));
            else if (part.Length is 2 or 3 && part[0] is 'F' or 'f' && int.TryParse(part[1..], out var f) && f is >= 1 and <= 12)
                parsed.Add((ushort)(0x70 + f - 1));
            else return false;
        }
        keys = parsed.ToArray();
        return true;
    }

    private static Input Create(ushort key, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput { VirtualKey = key, Flags = keyUp ? KeyUp : 0 }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamL, ParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
