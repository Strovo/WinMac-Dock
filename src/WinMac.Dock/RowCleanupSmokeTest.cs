using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

public partial class App
{
    private async Task VerifyRowStress(List<string> checks)
    {
        string config = Path.Combine(Program.SmokeTestDirectory!, "row-fixture.json");
        if (!File.Exists(config)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(config));
        string fixturePath = document.RootElement.GetProperty("Executable").GetString()!;
        var settings = Dock!.Settings;
        var items = settings.Items.ToList();
        bool all = settings.ShowOnAllMonitors, grouped = settings.GroupWindows, reduced = settings.ReduceMotion;
        int size = settings.IconSize;
        var edges = Docks().ToDictionary(d => d, d => d.PositionEdge);
        var snapshots = new List<string>();
        int activeSamples = 0, intermediateSamples = 0, settledSamples = 0;
        Exception? failure = null;
        var fixtures = new List<Process>();
        DockWindow[] Docks() => secondaryDocks.Prepend(Dock).ToArray();
        async Task Wait(Func<bool> condition, string reason)
        {
            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 8000)
            {
                if (condition()) return;
                foreach (var dock in Docks())
                {
                    if (dock.RowActive) activeSamples++;
                    if (dock.RowIntermediate) intermediateSamples++;
                }
                await Task.Delay(25);
            }
            throw new InvalidOperationException(reason + " / " + string.Join(" / ", Docks().Select(d => d.RowSnapshot)));
        }
        try
        {
            settings.ShowOnAllMonitors = true;
            settings.GroupWindows = false;
            settings.Items = [new DockItem { Id = "row-a", Kind = ItemKind.Folder, Name = "Row A", Target = Program.SmokeTestDirectory! },
                new DockItem { Id = "row-b", Kind = ItemKind.Folder, Name = "Row B", Target = Program.SmokeTestDirectory! }];
            Dock.ApplySettings(persist: false); SyncDocks(Dock);
            await Task.Delay(500);
            for (int cycle = 0; cycle < 8; cycle++)
            {
                var fixture = Process.Start(new ProcessStartInfo(fixturePath) { UseShellExecute = false })!;
                fixtures.Add(fixture);
                uint pid = (uint)fixture.Id;
                await Wait(() => Docks().All(d => d.RepresentsFixture(pid)), "Fixture did not enter each monitor-local row");
                // Change geometry/settings while the insertion tween is live.
                if (cycle % 3 == 0) settings.IconSize = size + (cycle % 2 == 0 ? 4 : 8);
                if (cycle % 3 == 1) settings.ReduceMotion = true;
                foreach (var dock in Docks())
                {
                    dock.PositionEdge = Enum.GetValues<DockEdge>()[cycle % 4];
                    dock.ApplySettings(persist: false);
                }
                await Wait(() => Docks().All(d => !d.RowActive), "Interrupted entry did not settle");
                await Task.Delay(150);
                foreach (var dock in Docks()) { dock.VerifyRowSurface(pid, true); settledSamples++; snapshots.Add(dock.RowSnapshot); }
                settings.ReduceMotion = false; settings.IconSize = size;
                foreach (var dock in Docks()) dock.ApplySettings(persist: false);
                await Wait(() => Docks().All(d => !d.RowActive), "Settings restoration did not settle");
                // Finish -> Reconcile -> BeginRowTransition is genuinely reentrant.
                Dock.RetargetRowForRegression();
                await Wait(() => Docks().All(d => !d.RowActive), "Replacement transition did not complete");
                foreach (var dock in Docks()) { dock.VerifyRowSurface(pid, true); settledSamples++; }
                var owned = new List<nint>();
                Native.EnumWindows((window, _) => { Native.GetWindowThreadProcessId(window, out uint owner); if (owner == pid) owned.Add(window); return true; }, 0);
                foreach (var window in owned) Native.PostMessage(window, 0x10, 0, 0);
                await Wait(() => Docks().All(d => !d.RepresentsFixture(pid) && !d.RowActive), "Fixture close left clones or tasks");
                await Task.Delay(150);
                foreach (var dock in Docks()) { dock.VerifyRowSurface(pid, false); settledSamples++; snapshots.Add(dock.RowSnapshot); }
            }
            if (activeSamples == 0 || intermediateSamples == 0) throw new InvalidOperationException("Stress observed no intermediate animation geometry");
            checks.Add("Eight full-dock open/close/reorder cycles on every monitor; four edges and size/motion interruption; no orphan clones, moves or cover");
            checks.Add("Reentrant replacement transitions retain artwork and complete; material HWND bounds and monitor ownership match each dock");
            File.WriteAllText(Path.Combine(Program.SmokeTestDirectory!, "row-capture.ready"), Environment.ProcessId.ToString());
            await Task.Delay(8000); // bounded external compositor capture window
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            foreach (var fixture in fixtures) { if (!fixture.HasExited) fixture.Kill(); fixture.Dispose(); }
            File.WriteAllText(Path.Combine(Program.SmokeTestDirectory!, "row-stress.json"), JsonSerializer.Serialize(new
            { Passed = failure == null, Error = failure?.ToString(), ActiveSamples = activeSamples, IntermediateSamples = intermediateSamples, SettledSamples = settledSamples, Snapshots = snapshots }, new JsonSerializerOptions { WriteIndented = true }));
            settings.Items = items; settings.ShowOnAllMonitors = all; settings.IconSize = size; settings.ReduceMotion = reduced; settings.GroupWindows = grouped;
            foreach (var pair in edges) pair.Key.PositionEdge = pair.Value;
            Dock.ApplySettings(persist: false); SyncDocks(Dock);
        }
    }
}

internal sealed partial class DockWindow
{
    // Full WinUI regression, only entered from the isolated smoke profile.
    internal void VerifyInterruptedRowCleanup(List<string> checks)
    {
        if (Program.SmokeTestDirectory == null) throw new InvalidOperationException("Isolated profile required");
        Settings.ReduceMotion = false; Settings.AnimationMs = 240;
        ApplySettings(persist: false); Reconcile(); ResetMagnification();
        FinishRowTransition();
        if (!Motion) throw new InvalidOperationException("Animations must be enabled for regression");
        BeginRowTransition(CaptureRowScene());
        if (!rowAnimating || rowArtwork.Children.Count == 0) throw new InvalidOperationException("Regression did not stage real cloned artwork");
        // WinUI dependency-property callbacks are synchronous and can reenter or
        // fail while StopRowTransition reveals the live row during handoff.
        bool injected = false;
        long token = strip.RegisterPropertyChangedCallback(UIElement.OpacityProperty, (_, _) =>
        {
            if (strip.Opacity != 1) return;
            injected = true;
            throw new InvalidOperationException("row-cleanup injected handoff failure");
        });
        try
        {
            try { FinishRowTransition(); }
            catch (Exception ex) when (injected && ex.Message.Contains("row-cleanup")) { }
        }
        finally { strip.UnregisterPropertyChangedCallback(UIElement.OpacityProperty, token); }
        if (!injected) throw new InvalidOperationException("Handoff failure injection was not reached");
        VerifySettledRow();
        checks.Add("Real WinUI handoff failure leaves no cloned row, moves or cover");
    }

    internal bool RepresentsFixture(uint pid)
    {
        Reconcile();
        return buttons.Any(b => b.Entry.Windows.Any(w => { Native.GetWindowThreadProcessId(w.Handle, out uint owner); return owner == pid; }));
    }

    internal bool RowActive => rowAnimating;
    internal bool RowIntermediate => rowAnimating && rowMoves.Any(move => move.Current != move.From && move.Current != move.To);
    internal string RowSnapshot => $"{DisplayIdentity}: active={rowAnimating}, moves={rowMoves.Count}, clones={rowArtwork.Children.Count}, host={hostBounds}, material={materialHostBounds}, clock={ClockView.TimeLabel.Text}";

    internal void VerifyRowSurface(uint fixturePid, bool fixturePresent)
    {
        VerifySettledRow();
        root.UpdateLayout();
        if (ClockView.Parent != strip || !ClockView.IsLoaded || ClockView.TimeLabel.Text.Length == 0)
            throw new InvalidOperationException("Missing live clock: " + RowSnapshot);
        if (buttons.Count != strip.Children.OfType<Button>().Count()) throw new InvalidOperationException("Duplicate live icons");
        if (materialWindow == null || ReferenceEquals(materialRoot, root) || Native.Bounds(materialHandle) != materialHostBounds
            || Native.MonitorFromWindow(materialHandle, 2) != monitor.Handle || !materialVisible)
            throw new InvalidOperationException("Material surface detached from dock: " + RowSnapshot);
        if (RepresentsFixture(fixturePid) != fixturePresent) throw new InvalidOperationException("Wrong fixture membership: " + RowSnapshot);
        foreach (var window in buttons.SelectMany(b => b.Entry.Windows))
        {
            Native.GetWindowThreadProcessId(window.Handle, out uint owner);
            if (owner == fixturePid && Native.MonitorFromWindow(window.Handle, 2) != monitor.Handle)
                throw new InvalidOperationException("Fixture leaked across monitor");
        }
    }

    internal void RetargetRowForRegression()
    {
        FinishRowTransition(); ResetMagnification();
        BeginRowTransition(CaptureRowScene());
        Settings.Items.Reverse();
        FinishRowTransition();
        if (!rowAnimating || !root.Children.Contains(rowArtwork) || rowMoves.Count == 0)
            throw new InvalidOperationException("Reentrant reconciliation lost replacement transition");
    }

    private void VerifySettledRow()
    {
        if (rowAnimating || rowMoves.Count != 0 || rowArtwork.Children.Count != 0 || root.Children.Contains(rowArtwork)
            || strip.Opacity != 1 || hoverArtwork.Opacity != 1 || dockBorder.Opacity != 1)
            throw new InvalidOperationException($"Orphan row: animating={rowAnimating}, moves={rowMoves.Count}, artwork={rowArtwork.Children.Count}, attached={root.Children.Contains(rowArtwork)}, opacities={strip.Opacity}/{hoverArtwork.Opacity}/{dockBorder.Opacity}");
    }
}
