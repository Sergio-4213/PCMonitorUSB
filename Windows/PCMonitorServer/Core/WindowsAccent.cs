using System.Runtime.InteropServices;

namespace PCMonitorUSB.Core;

/// <summary>Reads the interactive user's Windows color without modifying their settings.</summary>
public static class WindowsAccent
{
    public const string Fallback = "#2AC7DA";

    public static string Read()
    {
        try
        {
            if (DwmGetColorizationColor(out var argb, out _) == 0)
                return ToRgbHex(argb);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return Fallback;
    }

    // DWM returns AARRGGBB, not the BBGGRR format of COLORREF.
    internal static string ToRgbHex(uint argb) => $"#{argb & 0x00FFFFFF:X6}";

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetColorizationColor(out uint color, [MarshalAs(UnmanagedType.Bool)] out bool opaque);
}
