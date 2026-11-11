using WinMac.Dock.Core;

var tests = new List<(string Name, Action Run)>
{
    ("scope serialization through SettingsStore", Serialization),
    ("local pin filtering and legacy global compatibility", Filtering),
    ("remembered preferences cannot resurrect or overwrite pin scope", RestoreScope),
    ("independent duplicate app pins, local unpin and persisted restart", IndependentPins),
    ("recognized application launchers remain monitor-local", ApplicationModules),
    ("dock readers and additions use monitor-local pin policy", DockWiring),
};
int failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine("PASS: " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL: " + name + ": " + ex.Message); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;

static DockItem App(string id = "pin") => new() { Id = id, Kind = ItemKind.Application, Target = @"C:\Fixture\app.exe", Name = "Fixture" };
static void Scope(DockItem item, string identity)
{
    var property = typeof(DockItem).GetProperty("MonitorIdentity");
    Check(property != null, "DockItem must persist stable MonitorIdentity scope");
    property!.SetValue(item, identity);
}
static string Identity(DockItem item) => (string)typeof(DockItem).GetProperty("MonitorIdentity")!.GetValue(item)!;
static void Serialization()
{
    string dir = Path.Combine(Path.GetTempPath(), "WinMac-Pinning-" + Guid.NewGuid().ToString("N"));
    try
    {
        var pin = App(); Scope(pin, @"DISPLAY\STABLE-A");
        var settings = new DockSettings { Items = [pin, new() { Kind = ItemKind.Separator }] };
        Scope(settings.Items[1], @"DISPLAY\STABLE-B");
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        store.Save(settings);
        var loaded = store.Load(() => throw new Exception("Unexpected defaults"));
        Check(Identity(loaded.Items[0]) == @"DISPLAY\STABLE-A", "App scope lost on reload");
        Check(Identity(loaded.Items[1]) == @"DISPLAY\STABLE-B", "Separator scope lost on reload");
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
static List<DockItem> Pins(DockSettings settings, string identity)
{
    var policy = typeof(DockItem).Assembly.GetType("WinMac.Dock.Core.MonitorPinPolicy");
    Check(policy != null, "MonitorPinPolicy must filter pins before reconciliation and geometry");
    return (List<DockItem>)policy!.GetMethod("ForDisplay")!.Invoke(null, [settings.Items, identity])!;
}
static void Filtering()
{
    var legacy = System.Text.Json.JsonSerializer.Deserialize<DockItem>("{\"Id\":\"legacy\",\"Kind\":\"Application\",\"Target\":\"legacy.exe\"}")!;
    var a = App("a"); Scope(a, "stable-A");
    var b = App("b"); Scope(b, "stable-B");
    var settings = new DockSettings { Items = [legacy, a, b] };
    Check(Pins(settings, "STABLE-a").Select(p => p.Id).SequenceEqual(["legacy", "a"]), "A must see only global and A pins, with case-insensitive stable identity");
    Check(Pins(settings, "stable-B").Select(p => p.Id).SequenceEqual(["legacy", "b"]), "B must see only global and B pins");
    Check(Pins(settings, "disconnected").Single().Id == "legacy", "Unknown display must not acquire another display's pins");
    Check(Pins(settings, "").Single().Id == "legacy", "Unresolved display must see only global pins");
    Check(Identity(legacy) == "", "Legacy pins remain global without migration");
}
static void RestoreScope()
{
    var settings = new DockSettings();
    var old = App("old"); old.MonitorIdentity = "old-display"; old.CustomIcon = "remembered.ico";
    ItemPreferences.Remember(settings, old);
    foreach (string scope in new[] { "new-display", "" })
    {
        var requested = App("requested"); requested.MonitorIdentity = scope;
        var restored = ItemPreferences.Restore(settings, requested)!;
        Check(restored.MonitorIdentity == scope, "Restore must preserve requested scope, including explicitly global scope");
        Check(restored.Id == "requested" && restored.CustomIcon == "remembered.ico", "Restore must retain new pin ID and remembered customization");
    }
}
static DockItem NewPin(DockSettings settings, DockItem item, string display)
{
    var method = typeof(MonitorPinPolicy).GetMethod("PrepareNewPin");
    Check(method != null, "New pins must receive current display scope after restoring preferences");
    return (DockItem)method!.Invoke(null, [settings, item, display, null])!;
}
static void IndependentPins()
{
    var settings = new DockSettings();
    var old = App(); old.MonitorIdentity = "obsolete"; old.CustomIcon = "custom.ico";
    ItemPreferences.Remember(settings, old);
    var a = NewPin(settings, App("a"), "stable-A");
    var b = NewPin(settings, App("b"), "stable-B");
    settings.Items.AddRange([a, b]);
    settings.Validate();
    Check(Pins(settings, "stable-A").Single().Id == "a" && Pins(settings, "stable-B").Single().Id == "b", "Same executable must be pinnable independently on both displays");
    Check(a.CustomIcon == "custom.ico" && a.MonitorIdentity == "stable-A", "New pin must apply scope after restore");
    var task = new TaskWindow(42, "Running", a.Target, "", new(0, 0, 100, 100), false, false);
    Check(TaskReconciler.Reconcile(Pins(settings, "stable-A"), [task]).Single().IsPinned, "Local pin must combine with local running app");
    Check(!TaskReconciler.Reconcile(Pins(settings, "stable-C"), [task]).Single().IsPinned, "Other-monitor pin must not consume a running app");
    ItemPreferences.Remember(settings, a);
    settings.Items.Remove(a);
    Check(Pins(settings, "stable-A").Count == 0 && Pins(settings, "stable-B").Single().Id == "b", "Unpin A must preserve B");
    var reloaded = System.Text.Json.JsonSerializer.Deserialize<DockSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
    reloaded.Validate();
    Check(Pins(reloaded, "stable-A").Count == 0 && Pins(reloaded, "stable-B").Single().Id == "b", "Restart must preserve independent unpin");
    var repin = NewPin(reloaded, App("repin"), "stable-A");
    Check(repin.MonitorIdentity == "stable-A", "Repin must not resurrect the remembered monitor");
}
static void ApplicationModules()
{
    foreach (string module in new[] { "fileexplorer", "settings", "store", "taskmanager" })
    {
        var pin = MonitorPinPolicy.PrepareNewPin(new DockSettings(), App(module), "stable-A", module);
        Check(pin.MonitorIdentity == "stable-A", "Recognizing app commands must not make a new application pin global: " + module);
    }
    var start = MonitorPinPolicy.PrepareNewPin(new DockSettings(), new() { Kind = ItemKind.StartMenu }, "stable-A", "start");
    Check(start.MonitorIdentity == "", "The built-in Start module stays shared");
}
static void DockWiring()
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src", "WinMac.Dock"))) root = root.Parent;
    Check(root != null, "Run this fixture from its repository build");
    string dock = File.ReadAllText(Path.Combine(root!.FullName, "src", "WinMac.Dock", "DockWindow.cs"));
    string minimize = File.ReadAllText(Path.Combine(root.FullName, "src", "WinMac.Dock", "MinimizedTasks.cs"));
    Check(dock.Contains("MonitorPinPolicy.ForDisplay(Settings.Items, DisplayIdentity)"), "Dock must expose display-local pins using stable DisplayIdentity");
    Check(dock.Contains("DownloadPinLayout.OrderPins(DisplayPins)"), "Reconcile/auto-fit/hover row must start with display-local pins");
    Check(minimize.Contains("DownloadPinLayout.OrderPins(DisplayPins)"), "Pending minimize geometry must use the same filtered ordered row");
    Check(dock.Contains("MonitorPinPolicy.PrepareNewPin(Settings, item, DisplayIdentity, special)"), "Context-menu/drag-drop additions must scope after restore");
    Check(dock.Contains("MonitorPinPolicy.PrepareNewPin(Settings, item, DisplayIdentity, SpecialItemKey(item))"), "Open folder additions must scope after restore");
    Check(!dock.Contains("Settings.Items.Any(pin => TaskReconciler.Matches(pin, additionalWindow))"), "Context menu must not match foreign pins");
    Check(dock.Contains("ReadDockWindows(true, allMonitors: true)"), "Primary taskbar synchronization must remain global");
    Check(dock.Contains("DisplayPins.Any(IsDownloads)"), "Module menu must not treat a foreign Downloads pin as local");
    Check(dock.Contains("DisplayPins.Any(i => i.IsDownloadedPin"), "Bulk unpin availability must be display-local");
    Check(dock.Contains("MonitorPinPolicy.IsVisible(i, DisplayIdentity) && i.IsDownloadedPin"), "Bulk unpin must preserve another display's download pins");
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
