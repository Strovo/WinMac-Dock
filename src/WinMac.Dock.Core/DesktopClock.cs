using System.Globalization;

namespace WinMac.Dock.Core;

public sealed record DesktopClockSettings
{
    public bool Enabled { get; set; }
    public bool Use24Hour { get; set; } = true;
    public bool ShowSeconds { get; set; }

}

public static class DesktopClock
{
    public static double AlongLength(DesktopClockSettings settings, bool vertical, double buttonSize)
        => !settings.Enabled ? 0 : vertical ? buttonSize : 108;
    public static string CompactDateText(DateTime now, CultureInfo culture) => now.ToString("d", culture);

    public static string DateText(DateTime now, CultureInfo culture) => now.ToString("D", culture);
    public static string TimeText(DateTime now, DesktopClockSettings settings, CultureInfo culture)
        => now.ToString(settings.Use24Hour ? (settings.ShowSeconds ? "HH:mm:ss" : "HH:mm")
            : (settings.ShowSeconds ? "h:mm:ss tt" : "h:mm tt"), culture);
}
