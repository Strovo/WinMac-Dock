using WinMac.Dock;
using System.Reflection;

var tests = new List<(string Name, Action Run)>
{
    ("Recovery supports an isolated journal path", () =>
    {
        Check(typeof(StartTaskbarAppearance).GetMethod("Recover", BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(string), typeof(Action)]) != null, "Missing isolated recovery entry point");
    }),
    ("Recovery waits for the path lock and preserves a replacement active lease", () => InTemp(path =>
    {
        File.WriteAllText(path, "{");
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        string json = $"{{\"Owner\":{Environment.ProcessId},\"OwnerStart\":{process.StartTime.ToUniversalTime().Ticks},\"Bars\":[]}}";
        WithContendingOperation(path, () => StartTaskbarAppearance.Recover(path,
            () => throw new Exception("Active lease must not restore")), () =>
        {
            Check(File.ReadAllText(path) == "{", "Recovery touched malformed content while another owner held the lock");
            File.Move(path, path + ".invalid-original");
            StartTaskbarAppearance.PublishJournal(path, stream => stream.Write(System.Text.Encoding.UTF8.GetBytes(json)));
        });
        Check(File.ReadAllText(path) == json, "Recovery quarantined the replacement active lease");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.invalid-*").Length == 1, "Replacement lease was quarantined");
    })),
    ("Publication waits for the path lock before writing temporary content", () => InTemp(path =>
    {
        int writes = 0;
        WithContendingOperation(path, () => StartTaskbarAppearance.PublishJournal(path, stream =>
        {
            Interlocked.Increment(ref writes);
            stream.WriteByte(42);
        }), () =>
        {
            Check(Volatile.Read(ref writes) == 0, "Publisher invoked writer while another owner held the lock");
            Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 0, "Publisher created content before acquiring lock");
        });
        Check(writes == 1 && File.ReadAllBytes(path).SequenceEqual(new byte[] { 42 }), "Waiting publisher did not finish");
    })),
    ("Abandoned path lock permits recovery and is released afterward", () => InTemp(path =>
    {
        File.WriteAllText(path, "{");
        // Keep the handle alive so the next acquirer observes abandonment.
        using var abandoned = new Mutex(false, JournalMutexName(path));
        using var owned = new ManualResetEventSlim();
        var owner = new Thread(() => { abandoned.WaitOne(); owned.Set(); });
        owner.Start();
        Check(owned.Wait(TimeSpan.FromSeconds(10)) && owner.Join(TimeSpan.FromSeconds(10)), "Owner did not abandon lock");
        StartTaskbarAppearance.Recover(path);
        Check(!File.Exists(path), "Abandoned lock prevented malformed journal recovery");
        // A different thread must acquire after Recover; leaked recursive ownership deadlocks it.
        WithContendingOperation(path, () => StartTaskbarAppearance.PublishJournal(path, stream => stream.WriteByte(42)), () => { });
        Check(File.ReadAllBytes(path).SequenceEqual(new byte[] { 42 }), "Abandoned lock was not released");
    })),
    ("Malformed pre-mutation journals do not disable recovery", () =>
    {
        InTemp(path =>
        {
            foreach (var json in new[] { "", "{\"Owner\":", "null", "{}", "{\"Owner\":-1,\"OwnerStart\":1,\"Bars\":null}", "{\"Owner\":-1,\"OwnerStart\":1,\"Bars\":[null]}" })
            {
                File.WriteAllText(path, json);
                StartTaskbarAppearance.Recover(path, () => throw new Exception("Malformed journal must not restore"));
                Check(!File.Exists(path), "Malformed journal still blocks the final path");
                StartTaskbarAppearance.Recover(path);
            }
        });
    }),
    ("Publication hides partial content until the complete journal is closed", () => InTemp(path =>
    {
        const string json = "{\"Owner\":2147483647,\"OwnerStart\":1,\"Bars\":[]}";
        StartTaskbarAppearance.PublishJournal(path, stream =>
        {
            stream.Write(System.Text.Encoding.UTF8.GetBytes(json[..10]));
            stream.Flush();
            Check(!File.Exists(path), "Partial journal exposed at final path before mutation");
            StartTaskbarAppearance.Recover(path);
            stream.Write(System.Text.Encoding.UTF8.GetBytes(json[10..]));
        });
        Check(File.ReadAllText(path) == json, "Complete journal was not published");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "Temporary publication file leaked");
        StartTaskbarAppearance.Recover(path);
        Check(!File.Exists(path), "Dead-owner journal was not recovered");
    })),
    ("Writer failure cleans temporary content without publishing", () => InTemp(path =>
    {
        var failure = new IOException("interrupted write");
        ThrowsSame(failure, () => StartTaskbarAppearance.PublishJournal(path, stream =>
        {
            stream.WriteByte((byte)'{');
            throw failure;
        }));
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 0, "Failed write left journal or temporary content");
    })),
    ("Active lease is retained and a concurrent publisher cannot overwrite it", () => InTemp(path =>
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        string json = $"{{\"Owner\":{Environment.ProcessId},\"OwnerStart\":{process.StartTime.ToUniversalTime().Ticks},\"Bars\":[]}}";
        File.WriteAllText(path, json);
        StartTaskbarAppearance.Recover(path, () => throw new Exception("Active lease must not restore"));
        bool blocked = false;
        try { StartTaskbarAppearance.PublishJournal(path, stream => stream.WriteByte(1)); }
        catch (IOException) { blocked = true; }
        Check(blocked, "Concurrent lease was allowed");
        Check(File.ReadAllText(path) == json, "Active lease was changed");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "Failed move leaked temporary content");
    })),
    ("Competing publication wins without being overwritten", () => InTemp(path =>
    {
        bool blocked = false;
        try
        {
            StartTaskbarAppearance.PublishJournal(path, stream =>
            {
                stream.WriteByte(1);
                StartTaskbarAppearance.PublishJournal(path, other => other.WriteByte(2));
            });
        }
        catch (IOException) { blocked = true; }
        Check(blocked && File.ReadAllBytes(path).SequenceEqual(new byte[] { 2 }), "Losing publisher overwrote winning lease");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "Losing publisher leaked temporary content");
    })),
    ("Restoration failure propagates and retains complete journal for retry", () => InTemp(path =>
    {
        const string json = "{\"Owner\":2147483647,\"OwnerStart\":1,\"Bars\":[]}";
        File.WriteAllText(path, json);
        var failure = new IOException("restoration failed");
        ThrowsSame(failure, () => StartTaskbarAppearance.Recover(path, () => throw failure));
        Check(File.ReadAllText(path) == json, "Failed restoration lost recovery data");
        int restored = 0;
        StartTaskbarAppearance.Recover(path, () => restored++);
        Check(restored == 1 && !File.Exists(path), "Successful retry did not release journal");
    })),
    ("Interrupted unpublished temporary journal does not prevent recovery", () => InTemp(path =>
    {
        File.WriteAllText(path + ".tmp-interrupted", "{");
        StartTaskbarAppearance.Recover(path);
        StartTaskbarAppearance.PublishJournal(path, stream =>
            System.Text.Json.JsonSerializer.Serialize(stream, new { Owner = int.MaxValue, OwnerStart = 1L, Bars = Array.Empty<object>() }));
        StartTaskbarAppearance.Recover(path);
        Check(!File.Exists(path), "Unpublished content blocked a subsequent lease");
    }))
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception e) { failures++; Console.WriteLine($"FAIL {test.Name}: {e}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed");
return failures == 0 ? 0 : 1;

// This is the cross-process lock contract, independently computed by callers.
static string JournalMutexName(string path) => @"Local\WinMac.Dock.StartTaskbarAppearance." +
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));

static void WithContendingOperation(string path, Action operation, Action whileLocked)
{
    using var mutex = new Mutex(false, JournalMutexName(path));
    using var started = new ManualResetEventSlim();
    using var finished = new ManualResetEventSlim();
    Exception? failure = null;
    var worker = new Thread(() =>
    {
        started.Set();
        try { operation(); }
        catch (Exception e) { failure = e; }
        finally { finished.Set(); }
    }) { IsBackground = true };
    mutex.WaitOne();
    try
    {
        worker.Start();
        Check(started.Wait(TimeSpan.FromSeconds(10)), "Contender did not start");
        // Observe actual blocking, not a sleep-based guess that it was scheduled.
        Check(SpinWait.SpinUntil(() => finished.IsSet ||
            (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10)), "Contender neither blocked nor finished");
        Check(!finished.IsSet, "Operation completed while another owner held the journal mutex");
        whileLocked();
    }
    finally
    {
        mutex.ReleaseMutex();
        Check(worker.Join(TimeSpan.FromSeconds(10)), "Contender did not finish after lock release");
    }
    if (failure != null) throw new Exception("Contender failed", failure);
}

static void ThrowsSame(Exception expected, Action action)
{
    try { action(); }
    catch (Exception actual) when (ReferenceEquals(expected, actual)) { return; }
    throw new Exception("Expected original failure to propagate");
}

static void InTemp(Action<string> test)
{
    string directory = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "winmac-journal-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { test(Path.Combine(directory, "journal.json")); }
    finally { Directory.Delete(directory, true); }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
