using System.Globalization;

namespace PCMonitorUSB.Localization;

public static class AppLanguage
{
    private static bool _english = DetectEnglish();

    public static bool IsEnglish => _english;
    public static string CurrentCode => _english ? "en" : "pt";

    public static void Configure(string? setting)
    {
        _english = setting?.ToLowerInvariant() switch
        {
            "en" => true,
            "pt" => false,
            _ => DetectEnglish()
        };
    }

    public static string T(string portuguese, string english) => _english ? english : portuguese;

    public static string BuiltInButtonLabel(string id, string fallback) => id switch
    {
        "media_previous" => T("ANTERIOR", "PREVIOUS"),
        "media_play_pause" => "PLAY/PAUSE",
        "media_next" => T("PRÓXIMA", "NEXT"),
        "mute" => "MUTE",
        "volume_down" => "VOL -",
        "volume_up" => "VOL +",
        "show_desktop" => "DESKTOP",
        "open_task_manager" => T("TAREFAS", "TASK MANAGER"),
        "open_steam" => "STEAM",
        "open_amd" => "AMD",
        _ => fallback
    };

    private static bool DetectEnglish() =>
        !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase);
}
