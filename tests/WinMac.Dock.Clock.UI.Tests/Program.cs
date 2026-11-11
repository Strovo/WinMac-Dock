using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using WinMac.Dock;
using WinMac.Dock.Core;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _ = new ClockTestApp();
        });
    }
}

internal sealed class ClockTestApp : Application
{
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string report = Path.Combine(AppContext.BaseDirectory, "clock-ui-test.txt");
        Window? window = null;
        bool preview = Environment.GetCommandLineArgs().Contains("--preview");
        try
        {
            var settings = new DesktopClockSettings { Enabled = true, ShowSeconds = true };
            var clock = new DockClockView(() => settings);
            clock.Configure(false, 60, 68);
            var row = new StackPanel { Orientation = Orientation.Horizontal, RequestedTheme = ElementTheme.Dark };
            var lastIcon = new TextBlock { Text = "Last dock icon", Width = 60 };
            row.Children.Add(lastIcon); row.Children.Add(clock);
            window = new Window { Content = row };
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(400, 160));
            window.AppWindow.Show(false);
            if (preview)
            {
                window.Closed += (_, _) => Exit();
                return;
            }
            await Task.Delay(1200);
            Check(ReferenceEquals(row.Children[^1], clock) && ReferenceEquals(clock.XamlRoot, lastIcon.XamlRoot), "Clock is a child of the same dock row");
            var time = clock.TimeLabel;
            var date = clock.DateLabel;
            Check(!string.IsNullOrWhiteSpace(time.Text) && !string.IsNullOrWhiteSpace(date.Text), "Time and date rendered");
            string before = time.Text;
            await Task.Delay(1300);
            Check(time.Text != before, "Timer updates seconds");
            Check(clock.Width == 108 && clock.Height == 68, "Clock uses reserved horizontal dock space");
            settings = new DesktopClockSettings { Enabled = true, Use24Hour = false };
            clock.Configure(true, 60, 68);
            Check(time.Text == DesktopClock.TimeText(DateTime.Now, settings, CultureInfo.CurrentCulture), "Replaced settings applied");
            Check(clock.Width == 68 && clock.Height == 60, "Vertical dock layout stays within the icon cross size");
            row.RequestedTheme = ElementTheme.Light;
            await Task.Delay(100);
            Check(clock.ActualTheme == ElementTheme.Light, "Theme inherited from dock");
            settings.Enabled = false; clock.Configure(false, 60, 68);
            Check(clock.Visibility == Visibility.Collapsed, "Clock can be disabled");
            before = time.Text;
            await Task.Delay(1300);
            Check(time.Text == before, "Disabled clock stops its timer");
            settings.Enabled = true; clock.Configure(false, 60, 68);
            Check(clock.Visibility == Visibility.Visible, "Clock can be re-enabled");
            clock.Stop();
            window.Close(); window = null;
            File.WriteAllText(report, "PASS: same-row embedding, rendering, timer, horizontal and vertical sizing, replaced settings, inherited theme, disable/re-enable and timer shutdown.");
        }
        catch (Exception ex)
        {
            preview = false;
            File.WriteAllText(report, "FAIL: " + ex);
            Environment.ExitCode = 1;
            window?.Close();
        }
        finally { if (!preview) Exit(); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
