#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMac.Dock;

// Appearance belongs to a particular Explorer HWND lifetime, not a display name.
// Keep permanent ownership separate from Start's short-lived journal.
internal sealed record TaskbarOpacity(long Handle, uint ProcessId, long ProcessStart,
    bool WasLayered, uint Key, byte Alpha, uint Flags)
{
    internal static readonly object Gate = new();
    private static readonly Dictionary<long, TaskbarOpacity> permanent = [];
    private static readonly Dictionary<long, TaskbarOpacity> temporary = [];
    private const long Layered = 0x80000;
    private static long Start(uint pid) { using var p = Process.GetProcessById((int)pid); return p.StartTime.ToUniversalTime().Ticks; }
    internal bool Matches(nint handle)
    {
        if (handle.ToInt64() != Handle || !IsWindow(handle)) return false;
        GetWindowThreadProcessId(handle, out uint pid);
        try { return pid == ProcessId && Start(pid) == ProcessStart; } catch { return false; }
    }
    internal static TaskbarOpacity Capture(nint handle)
    {
        lock (Gate)
            if (temporary.TryGetValue(handle.ToInt64(), out var underlying) && underlying.Matches(handle)) return underlying;
        GetWindowThreadProcessId(handle, out uint pid);
        bool layered = (GetWindowLongPtrW(handle, -20).ToInt64() & Layered) != 0;
        uint key = 0, flags = 2; byte alpha = 255;
        if (layered && !GetLayeredWindowAttributes(handle, out key, out alpha, out flags))
            throw new IOException("Could not capture taskbar opacity.");
        return new(handle.ToInt64(), pid, Start(pid), layered, key, alpha, flags);
    }
    internal static TaskbarOpacity? Permanent(nint handle)
    {
        lock (Gate) return permanent.TryGetValue(handle.ToInt64(), out var value) && value.Matches(handle) ? value : null;
    }
    internal void BeginTemporary() { lock (Gate) temporary[Handle] = this; }
    internal static void EndTemporary(nint handle) { lock (Gate) temporary.Remove(handle.ToInt64()); }
    internal void Suppress()
    {
        lock (Gate)
        {
            if (!Matches((nint)Handle)) return;
            MakeTransparent((nint)Handle);
            permanent[Handle] = this;
        }
    }
    internal static void MakeTransparent(nint handle)
    {
        SetWindowLongPtrW(handle, -20, (nint)(GetWindowLongPtrW(handle, -20).ToInt64() | Layered));
        if (!SetLayeredWindowAttributes(handle, 0, 0, 2) ||
            !GetLayeredWindowAttributes(handle, out _, out byte alpha, out uint flags) || alpha != 0 || (flags & 2) == 0)
            throw new IOException("Windows rejected taskbar transparency.");
    }
    internal void Restore()
    {
        lock (Gate)
        {
            if (temporary.TryGetValue(Handle, out var lease) && lease.Matches((nint)Handle))
            {
                MakeTransparent((nint)Handle);
                permanent.Remove(Handle);
                return;
            }
            if (Matches((nint)Handle))
            {
                nint handle = (nint)Handle;
                long style = GetWindowLongPtrW(handle, -20).ToInt64();
                SetWindowLongPtrW(handle, -20, (nint)(WasLayered ? style | Layered : style & ~Layered));
                if (WasLayered)
                {
                    if (!SetLayeredWindowAttributes(handle, Key, Alpha, Flags) ||
                        !GetLayeredWindowAttributes(handle, out uint key, out byte alpha, out uint flags) ||
                        key != Key || alpha != Alpha || flags != Flags)
                        throw new IOException("Could not restore taskbar opacity; recovery retained.");
                }
                else if ((GetWindowLongPtrW(handle, -20).ToInt64() & Layered) != 0)
                    throw new IOException("Could not restore taskbar layered style; recovery retained.");
            }
            if (permanent.TryGetValue(Handle, out var owner) && owner == this) permanent.Remove(Handle);
        }
    }
    [DllImport("user32.dll")] private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint handle, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint handle, int index, nint value);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(nint handle, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint handle, uint key, byte alpha, uint flags);
}
