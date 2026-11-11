using System.Globalization;
using WinMac.Dock.Core;

var now = new DateTime(2026, 11, 9, 0, 5, 7);
var options = new DesktopClockSettings();
Equal("00:05", DesktopClock.TimeText(now, options, CultureInfo.GetCultureInfo("es-ES")));
Console.WriteLine("PASS: digital clock displays 24-hour time without seconds by default.");
options.ShowSeconds = true;
Equal("00:05:07", DesktopClock.TimeText(now, options, CultureInfo.GetCultureInfo("es-ES")));
options.Use24Hour = false;
Equal("12:05:07 AM", DesktopClock.TimeText(now, options, CultureInfo.GetCultureInfo("en-US")));
options.ShowSeconds = false;
Equal("1:05 PM", DesktopClock.TimeText(now.AddHours(13), options, CultureInfo.GetCultureInfo("en-US")));
Console.WriteLine("PASS: optional seconds and 12-hour time, including midnight and afternoon.");
Equal(now.ToString("D", CultureInfo.GetCultureInfo("es-ES")), DesktopClock.DateText(now, CultureInfo.GetCultureInfo("es-ES")));
Equal(now.AddDays(1).ToString("D", CultureInfo.GetCultureInfo("en-US")), DesktopClock.DateText(now.AddDays(1), CultureInfo.GetCultureInfo("en-US")));
Console.WriteLine("PASS: localized date updates across midnight.");
options.Enabled = true;
var saved = System.Text.Json.JsonSerializer.Serialize(options);
var restored = System.Text.Json.JsonSerializer.Deserialize<DesktopClockSettings>(saved)!;
Equal(options, restored);
Equal(false, System.Text.Json.JsonSerializer.Deserialize<DesktopClockSettings>("{}")!.Enabled);
Console.WriteLine("PASS: clock preferences round-trip; old configurations keep the existing enabled preference.");
var dockSettings = System.Text.Json.JsonSerializer.Deserialize<DockSettings>("{}")!;
Equal(false, dockSettings.DesktopClock.Enabled);
dockSettings.DesktopClock = options;
var dockRestored = System.Text.Json.JsonSerializer.Deserialize<DockSettings>(System.Text.Json.JsonSerializer.Serialize(dockSettings))!;
Equal(options, dockRestored.DesktopClock);
Console.WriteLine("PASS: clock integrates with persisted dock settings without changing existing configurations.");
Equal(108d, DesktopClock.AlongLength(new DesktopClockSettings { Enabled = true }, false, 60));
Equal(60d, DesktopClock.AlongLength(new DesktopClockSettings { Enabled = true }, true, 60));
Equal(0d, DesktopClock.AlongLength(new DesktopClockSettings { Enabled = false }, false, 60));
Equal(now.ToString("d", CultureInfo.GetCultureInfo("es-ES")), DesktopClock.CompactDateText(now, CultureInfo.GetCultureInfo("es-ES")));
var clockWidths = new[] { 60d, 60d, DesktopClock.AlongLength(options, false, 60) };
Equal(228d, DockSpacing.Length(clockWidths, new[] { true, true, false }, 0));
Console.WriteLine("PASS: dock clock reserves its own non-magnifying row space and uses a compact date.");

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}; got {actual}.");
}
