using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

// Exercised only by the isolated --smoke-test profile. Never run on the user's settings.
public partial class App
{
    private async Task VerifyMonitorPinning(List<string> checks)
    {
        if (Program.SmokeTestDirectory == null) throw new InvalidOperationException("Isolated profile required");
        var displays = Native.Monitors();
        if (displays.Count < 2) throw new InvalidOperationException("Pinning UI checks require two connected displays");
        var settings = Dock!.Settings;
        var originalItems = settings.Items.ToList();
        bool originalAll = settings.ShowOnAllMonitors;
        try
        {
            settings.ShowOnAllMonitors = true;
            SyncDocks();
            var a = Dock;
            var b = secondaryDocks.First(d => d.DisplayIdentity != a.DisplayIdentity);
            settings.Items = [new DockItem { Id = "pin-test-global", Kind = ItemKind.Folder, Name = "Legacy global", Target = Program.SmokeTestDirectory }];
            a.ApplySettings(persist: false); b.ApplySettings(persist: false);
            await Task.Delay(400);
            foreach (var kind in new[] { ItemKind.Application, ItemKind.File, ItemKind.Folder })
            {
                string target = kind == ItemKind.Folder ? Program.SmokeTestDirectory : Environment.ProcessPath!;
                var remembered = new DockItem { Kind = kind, Target = target, MonitorIdentity = "old-monitor", CustomIcon = "" };
                ItemPreferences.Remember(settings, remembered);
                var first = new DockItem { Id = "pin-test-A-" + kind, Kind = kind, Target = target, Name = "Pin A " + kind };
                var second = first with { Id = "pin-test-B-" + kind, Name = "Pin B " + kind };
                a.AddItem(first);
                // Force the open-folder path as well as ordinary AddItem.
                b.VerifyPinFromOpenFolder(second);
                await Task.Delay(400);
                a.VerifyPinRow(first.Id, second.Id);
                b.VerifyPinRow(second.Id, first.Id);
                var pin = settings.Items.Single(p => p.Id == first.Id);
                if (pin.MonitorIdentity != a.DisplayIdentity || settings.Items.Single(p => p.Id == second.Id).MonitorIdentity != b.DisplayIdentity)
                    throw new InvalidOperationException("New pin did not use the clicked display identity");
                a.VerifyUnpin(pin);
                await Task.Delay(400);
                a.VerifyPinRow(null, first.Id);
                b.VerifyPinRow(second.Id, first.Id);
                var persisted = new SettingsStore(Path.Combine(Log.DirectoryPath, "settings.json")).Load(() => throw new InvalidOperationException("Missing test settings"));
                if (persisted.Items.Any(p => p.Id == first.Id) || !persisted.Items.Any(p => p.Id == second.Id && p.MonitorIdentity == b.DisplayIdentity))
                    throw new InvalidOperationException("Independent pin ownership did not survive SettingsStore reload");
                checks.Add("Real dock " + kind + " pins: clicked-display creation, open-folder insertion, independent duplicate, local unpin and disk reload");
            }
        }
        finally
        {
            settings.Items = originalItems;
            settings.ShowOnAllMonitors = originalAll;
            Dock.ApplySettings();
            SyncDocks(Dock);
        }
    }
}

internal sealed partial class DockWindow
{
    internal void VerifyPinFromOpenFolder(DockItem item)
    {
        popupCount++;
        try { PinFromFolder(item); }
        finally { popupCount--; }
    }

    internal void VerifyUnpin(DockItem item) => UnpinItem(item);

    internal void VerifyPinRow(string? present, string absent)
    {
        Reconcile();
        var pins = buttons.Where(b => b.Entry.IsPinned).Select(b => b.Entry.Item.Id).ToArray();
        if ((present != null && !pins.Contains(present)) || pins.Contains(absent) || !pins.Contains("pin-test-global"))
            throw new InvalidOperationException("Rendered pin row leaked another display or lost a local/global pin: " + DisplayIdentity);
        if (!VerifyClockHoverLayout()) throw new InvalidOperationException("Display-local pin row disagrees with hover geometry");
    }
}
