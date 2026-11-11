#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace WinMac.Dock;

// Keep Explorer visually transparent while Start is open, even if it toggles
// WS_VISIBLE. Journal the reversible change before touching another process.
internal sealed class StartTaskbarAppearance : IDisposable
{
    private const long Layered = 0x80000;
    private sealed record Bar(long Handle, uint ProcessId, long ProcessStart, bool WasLayered, uint Key, byte Alpha, uint Flags, bool Visible);
    private sealed record Journal(int Owner, long OwnerStart, List<Bar> Bars);
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "Dock", "start-taskbar-appearance.json");
    private readonly Journal journal;
    private bool disposed;
    private StartTaskbarAppearance(Journal value) => journal = value;
    private static long ProcessStart(int id) { using var process = Process.GetProcessById(id); return process.StartTime.ToUniversalTime().Ticks; }
    private static bool Alive(int id, long start) { try { return ProcessStart(id) == start; } catch { return false; } }
    // Use one cross-process, recursive lock for every lifecycle operation on a
    // final path. Normalize Windows path casing before deriving the identifier.
    private sealed class JournalLock : IDisposable
    {
        private readonly Mutex mutex;
        internal JournalLock(string filePath)
        {
            string normalized = Path.GetFullPath(filePath).ToUpperInvariant();
            string name = @"Local\WinMac.Dock.StartTaskbarAppearance." +
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
            mutex = new Mutex(false, name);
            try { mutex.WaitOne(); }
            catch (AbandonedMutexException) { /* Ownership was granted; recover under the lock. */ }
            catch { mutex.Dispose(); throw; }
        }
        public void Dispose()
        {
            try { mutex.ReleaseMutex(); }
            finally { mutex.Dispose(); }
        }
    }
    internal static void Recover() => Recover(FilePath);
    internal static void Recover(string filePath, Action? restore = null)
    {
        lock (TaskbarOpacity.Gate) RecoverCore(filePath, restore);
    }
    private static void RecoverCore(string filePath, Action? restore)
    {
        using var journalLock = new JournalLock(filePath);
        if (!File.Exists(filePath)) return;
        Journal? journal;
        try
        {
            journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(filePath),
                new JsonSerializerOptions { RespectRequiredConstructorParameters = true });
            if (journal == null || journal.Owner <= 0 || journal.OwnerStart <= 0 || journal.Bars == null ||
                journal.Bars.Exists(bar => bar == null || bar.Handle == 0 || bar.ProcessId == 0 || bar.ProcessStart <= 0))
                throw new JsonException("Invalid taskbar appearance recovery journal.");
        }
        catch (JsonException)
        {
            // An incomplete pre-mutation journal cannot restore anything. Keep it
            // for diagnosis without leaving the exclusive lease path blocked.
            File.Move(filePath, filePath + ".invalid-" + Guid.NewGuid().ToString("N"));
            return;
        }
        if (Alive(journal.Owner, journal.OwnerStart)) return;
        if (restore != null) restore();
        else Restore(journal, restoreVisibility: true);
        File.Delete(filePath);
    }
    internal static StartTaskbarAppearance Acquire(IEnumerable<nint> handles)
    {
        lock (TaskbarOpacity.Gate) return AcquireCore(handles);
    }
    private static StartTaskbarAppearance AcquireCore(IEnumerable<nint> handles)
    {
        Recover();
        var bars = new List<Bar>();
        foreach (var handle in handles)
        {
            GetWindowThreadProcessId(handle, out uint pid);
            bool layered = (GetWindowLongPtrW(handle, -20).ToInt64() & Layered) != 0;
            uint key = 0, flags = 2; byte alpha = 255;
            if (layered && !GetLayeredWindowAttributes(handle, out key, out alpha, out flags))
                throw new InvalidOperationException("Could not capture existing taskbar opacity.");
            // Journal the underlying original appearance, not permanent alpha zero.
            // Dispose chooses zero while permanent ownership is still active; if
            // Hide is switched off during Start it restores this true original.
            var permanent = TaskbarOpacity.Permanent(handle);
            if (permanent != null)
            {
                layered = permanent.WasLayered; key = permanent.Key;
                alpha = permanent.Alpha; flags = permanent.Flags;
            }
            bars.Add(new(handle.ToInt64(), pid, ProcessStart((int)pid), layered, key, alpha, flags, IsWindowVisible(handle)));
        }
        var value = new Journal(Environment.ProcessId, ProcessStart(Environment.ProcessId), bars);
        PublishJournal(FilePath, stream => JsonSerializer.Serialize(stream, value));
        var lease = new StartTaskbarAppearance(value);
        try
        {
            foreach (var bar in bars)
            {
                nint handle = (nint)bar.Handle;
                new TaskbarOpacity(bar.Handle, bar.ProcessId, bar.ProcessStart, bar.WasLayered, bar.Key, bar.Alpha, bar.Flags).BeginTemporary();
                SetWindowLongPtrW(handle, -20, (nint)(GetWindowLongPtrW(handle, -20).ToInt64() | Layered));
                if (!SetLayeredWindowAttributes(handle, 0, 0, 2)) throw new InvalidOperationException("Windows rejected temporary taskbar transparency.");
                if (!GetLayeredWindowAttributes(handle, out _, out byte actual, out uint actualFlags) || actual != 0 || (actualFlags & 2) == 0)
                    throw new InvalidOperationException("Taskbar transparency could not be verified.");
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    internal static void PublishJournal(string filePath, Action<Stream> write)
    {
        using var journalLock = new JournalLock(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        string temporaryPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }
            // Same-directory rename publishes only complete, durable contents.
            // Never overwrite another lease, even if it won the publication race.
            File.Move(temporaryPath, filePath, overwrite: false);
        }
        finally { File.Delete(temporaryPath); }
    }
    private static void Restore(Journal value, bool restoreVisibility)
    {
        lock (TaskbarOpacity.Gate) RestoreCore(value, restoreVisibility);
    }
    private static void RestoreCore(Journal value, bool restoreVisibility)
    {
        foreach (var bar in value.Bars)
        {
            nint handle = (nint)bar.Handle;
            TaskbarOpacity.EndTemporary(handle);
            if (!IsWindow(handle)) continue;
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid != bar.ProcessId || !Alive((int)pid, bar.ProcessStart)) continue;
            if (TaskbarOpacity.Permanent(handle) != null)
            {
                TaskbarOpacity.MakeTransparent(handle);
                continue;
            }
            long style = GetWindowLongPtrW(handle, -20).ToInt64();
            if (bar.WasLayered)
            {
                SetWindowLongPtrW(handle, -20, (nint)(style | Layered));
                if (!SetLayeredWindowAttributes(handle, bar.Key, bar.Alpha, bar.Flags)) throw new IOException("Could not restore taskbar opacity; recovery journal retained.");
            }
            else
            {
                SetWindowLongPtrW(handle, -20, (nint)(style & ~Layered));
                if ((GetWindowLongPtrW(handle, -20).ToInt64() & Layered) != 0) throw new IOException("Could not restore taskbar window style; recovery journal retained.");
            }
            if (restoreVisibility) ShowWindow(handle, bar.Visible ? 8 : 0);
        }
    }
    public void Dispose()
    {
        lock (TaskbarOpacity.Gate) DisposeCore();
    }
    private void DisposeCore()
    {
        using var journalLock = new JournalLock(FilePath);
        if (disposed) return;
        Restore(journal, restoreVisibility: false);
        File.Delete(FilePath);
        disposed = true;
    }
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
}
