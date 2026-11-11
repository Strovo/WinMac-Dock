#nullable enable
using System.Diagnostics;
using System.Text.Json;

namespace WinMac.Dock;

public partial class App
{
    // Opt-in only: the fixture is external, so real HSHELL_FLASH reaches the dock.
    // Taskbars are session-global; this never changes the normal profile or stops it.
    private async Task VerifyTaskbarAttention(List<string> checks)
    {
        string configuration = Path.Combine(Program.SmokeTestDirectory!, "attention-fixture.json");
        if (!File.Exists(configuration)) return;
        using var config = JsonDocument.Parse(File.ReadAllText(configuration));
        var settings = Dock!.Settings;
        bool oldHide = settings.HideWindowsTaskbar, oldBlink = settings.PreventAttentionBlinking;
        bool oldAll = settings.ShowOnAllMonitors;
        Process? fixture = null;
        try
        {
            settings.ShowOnAllMonitors = true;
            settings.HideWindowsTaskbar = true;
            settings.PreventAttentionBlinking = false;
            Dock.ApplySettings(persist: false); SyncDocks(Dock);
            if (Dock.TaskbarTestError != null) throw new InvalidOperationException(Dock.TaskbarTestError);
            var start = new ProcessStartInfo(config.RootElement.GetProperty("Dotnet").GetString()!) { UseShellExecute = false };
            start.ArgumentList.Add(config.RootElement.GetProperty("Assembly").GetString()!);
            start.ArgumentList.Add(Path.Combine(Program.SmokeTestDirectory!, "attention-fixture"));
            start.ArgumentList.Add("--external");
            fixture = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not launch");
            File.WriteAllText(Path.Combine(Program.SmokeTestDirectory!, "attention-start.ready"), Environment.ProcessId.ToString());
            var observed = new HashSet<nint>();
            var timeout = Stopwatch.StartNew();
            while (!fixture.HasExited && timeout.ElapsedMilliseconds < 35000)
            {
                foreach (var dock in secondaryDocks.Prepend(Dock))
                    foreach (var monitor in dock.FixtureAttentionMonitors((uint)fixture.Id)) observed.Add(monitor);
                await Task.Delay(10);
            }
            if (!fixture.HasExited || fixture.ExitCode != 0) throw new InvalidOperationException("Real FlashWindowEx fixture failed or timed out");
            if (observed.Count != Native.Monitors().Count) throw new InvalidOperationException("Real shell attention was not observed from both fixture displays: " + observed.Count);
            checks.Add("Published app: real FlashWindowEx on both monitors, taskbars alpha-zero throughout two 12-second flashing intervals");
            checks.Add("Published app: actual HSHELL_FLASH notifications retained by dock attention tracking on both displays");
        }
        finally
        {
            if (fixture != null) { if (!fixture.HasExited) fixture.Kill(); fixture.Dispose(); }
            settings.HideWindowsTaskbar = oldHide; settings.PreventAttentionBlinking = oldBlink;
            settings.ShowOnAllMonitors = oldAll;
            Dock.ApplySettings(persist: false); SyncDocks(Dock);
        }
    }
}

internal sealed partial class DockWindow
{
    internal string? TaskbarTestError => windowsTaskbar.Error;
    internal IEnumerable<nint> FixtureAttentionMonitors(uint pid) => attentionWindows.Where(window =>
    {
        Native.GetWindowThreadProcessId(window, out uint owner);
        return owner == pid;
    }).Select(window => Native.MonitorFromWindow(window, 2)).ToArray();
}
