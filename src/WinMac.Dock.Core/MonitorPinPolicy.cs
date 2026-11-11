namespace WinMac.Dock.Core;

public static class MonitorPinPolicy
{
    public static DockItem PrepareNewPin(DockSettings settings, DockItem item, string displayIdentity, string? module = null)
    {
        item = ItemPreferences.Restore(settings, item, module) ?? item;
        // Scope is ownership, never a remembered appearance preference.
        item.MonitorIdentity = item.Kind is ItemKind.Application or ItemKind.File
            || (module == null && item.Kind == ItemKind.Folder) ? displayIdentity : "";
        return item;
    }

    public static bool IsVisible(DockItem item, string displayIdentity) =>
        string.IsNullOrEmpty(item.MonitorIdentity)
        || StringComparer.OrdinalIgnoreCase.Equals(item.MonitorIdentity, displayIdentity);

    // Retain references and global-list order: mutations target the exact pin ID,
    // not an application identity shared by independently pinned monitors.
    public static List<DockItem> ForDisplay(IEnumerable<DockItem> items, string displayIdentity) =>
        items.Where(item => IsVisible(item, displayIdentity)).ToList();
}
