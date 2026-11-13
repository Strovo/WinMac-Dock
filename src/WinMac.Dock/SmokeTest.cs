using System.Text.Json;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

public partial class App
{
    private async Task VerifyPublishedClock()
    {
        var checks = new List<string>();
        Exception? failure = null;
        void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks.Add(description);
        }
        try
        {
            await Task.Delay(1200);
            Require(Dock != null && Dock.StartupComplete && Native.IsWindowVisible(Dock.Handle), "Full dock startup");
            var clock = Dock!.ClockView;
            Require(clock.Parent is StackPanel dockRow && ReferenceEquals(dockRow.Children[^1], clock), "Clock is the last element inside the dock row");
            Require(ReferenceEquals(clock.XamlRoot, ((Microsoft.UI.Xaml.FrameworkElement)Dock.Content).XamlRoot), "Clock shares the dock window, not a floating widget");
            var time = clock.TimeLabel;
            var date = clock.DateLabel;
            Require(time.Text.Length > 0 && date.Text.Length > 0, "Clock time and date rendered");
            var before = time.Text;
            for (int attempt = 0; attempt < 20 && before == time.Text; attempt++) await Task.Delay(250);
            Require(before != time.Text, "Clock timer advances");
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                Dock.PositionEdge = edge; Dock.ApplySettings();
                await Task.Delay(400);
                var row = (StackPanel)clock.Parent;
                Require(ReferenceEquals(row.Children[^1], clock) && clock.ActualWidth > 0 && clock.ActualHeight > 0, "Clock laid out at " + edge);
                var origin = clock.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point());
                Require(edge is DockEdge.Left or DockEdge.Right ? origin.Y + clock.ActualHeight <= row.ActualHeight + 1
                    : origin.X + clock.ActualWidth <= row.ActualWidth + 1, "Clock fits the row at " + edge);
            }
            Dock.PositionEdge = DockEdge.Bottom; Dock.ApplySettings();
            await Task.Delay(400);
            Require(Dock.VerifyClockHoverLayout(), "Clock is included in hover geometry without magnification");
            Dock.Settings.DesktopClock.Enabled = false; Dock.ApplySettings();
            await Task.Delay(400);
            Require(clock.Parent == null, "Disabling clock removes its dock space");
            Dock.Settings.DesktopClock.Enabled = true; Dock.ApplySettings();
            await Task.Delay(400);
            Require(clock.Parent != null, "Clock re-enabled inside the dock");
            bool floatingClock = false;
            Native.EnumWindows((window, _) =>
            {
                Native.GetWindowThreadProcessId(window, out var process);
                if (process == Environment.ProcessId && Native.Title(window) == "WinMac Desktop Clock") floatingClock = true;
                return true;
            }, 0);
            Require(!floatingClock, "No floating clock window exists");
            await VerifyRowStress(checks);
            await VerifyMonitorPinning(checks);
            Dock.VerifyInterruptedRowCleanup(checks);
            await VerifyTaskbarAttention(checks);
            Dock.HandleCommand("--settings");
            await Task.Delay(1500);
            bool settingsVisible = false;
            Native.EnumWindows((window, _) =>
            {
                Native.GetWindowThreadProcessId(window, out var process);
                if (process == Environment.ProcessId && Native.IsWindowVisible(window)
                    && Native.Title(window).StartsWith("WinMac Dock Settings", StringComparison.Ordinal)) settingsVisible = true;
                return true;
            }, 0);
            Require(settingsVisible, "Settings window opens");
            var persisted = new SettingsStore(Path.Combine(Log.DirectoryPath, "settings.json")).Load(() => new DockSettings());
            Require(persisted.DesktopClock.Enabled && persisted.DesktopClock.ShowSeconds, "Dock clock configuration persisted to disk");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); Environment.ExitCode = 1; }
        finally
        {
            File.WriteAllText(Path.Combine(Program.SmokeTestDirectory!, "smoke-test.json"),
                JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
            monitorTimer?.Stop();
            Dock?.Close();
        }
    }
}

internal sealed partial class DockWindow
{
    internal bool VerifyClockHoverLayout()
    {
        ResetMagnification();
        try
        {
            PrepareHoverSurface();
            int clockSlot = Array.FindIndex(hoverSlots, slot => ReferenceEquals(slot.Element, ClockView));
            if (clockSlot < 0 || hoverWave == null) return false;
            var children = strip.Children.OfType<Microsoft.UI.Xaml.FrameworkElement>().ToArray();
            double expected = DockSpacing.Length(children.Select(DockElementLength).ToArray(),
                children.Select(child => child is Button).ToArray(), DisplaySpacing);
            hoverWave.RestoreGrowth(Enumerable.Repeat(PotentialHoverOverflow, hoverSlots.Length).ToArray(),
                hoverAvailableExpansion, PotentialHoverOverflow);
            PaintHoverFrame();
            return Math.Abs(hoverWave.Length - expected) < .01 && hoverWave.Growth[clockSlot] == 0;
        }
        finally { ResetMagnification(); }
    }
}
