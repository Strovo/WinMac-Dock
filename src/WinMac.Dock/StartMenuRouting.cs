#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WinMac.Dock;

// The shell interfaces are private and can disappear on a Windows update.
// Fail closed instead of falling back to SC_TASKLIST on the primary display.
internal static class StartMenuRouting
{
    private static readonly object gate = new();
    internal static bool IsOpen { get { lock (gate) return running; } }
    private static bool running;
    private static volatile bool dismissRequested;
    internal static void DismissOpened() => dismissRequested = true;
    internal static Exception? LastError { get; private set; }
    public static void Open(nint owner, nint monitor)
    {
        if (!IsWindow(owner)) throw new ArgumentException("A live dock HWND is required.", nameof(owner));
        if (monitor == 0) monitor = MonitorFromWindow(owner, 2);
        if (monitor != MonitorFromWindow(owner, 2)) throw new ArgumentException("The monitor must contain the dock.", nameof(monitor));
        lock (gate)
        {
            if (running) { dismissRequested = true; return; }
            dismissRequested = false;
            running = true; LastError = null;
        }
        var thread = new Thread(() =>
        {
            try { Run(monitor); }
            catch (Exception error) { LastError = error; }
            finally { lock (gate) running = false; }
        }) { IsBackground = true, Name = "Monitor-local Start" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
    internal static string Phase { get; private set; } = "idle";
    private static void Run(nint monitor)
    {
        Phase = "activating shell";
        object? shell = null, monitors = null, launcher = null, target = null;
        var saved = new Dictionary<nint, bool>();
        StartTaskbarAppearance? appearance = null;

        int restored = 0;
        void Restore()
        {
            if (Interlocked.Exchange(ref restored, 1) != 0) return;
            bool suppressed = false;
            Enumerate(h => { if (GetPropW(h, "WinMac.Dock.HideWindowsTaskbar") != 0) suppressed = true; return true; });
            appearance?.Dispose();
            foreach (var bar in saved) if (IsWindow(bar.Key)) ShowWindow(bar.Key, bar.Value && !suppressed ? 8 : 0);

        }
        EventHandler exiting = (_, _) => Restore();
        AppDomain.CurrentDomain.ProcessExit += exiting;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239"))!);
            var provider = (IServiceProvider)shell!;
            monitors = provider.QueryService(new Guid("47094E3A-0CF2-430F-806F-CF9E4F0F12DD"), typeof(IMonitorService).GUID);
            launcher = provider.QueryService(new Guid("6F86E01C-C649-4D61-BE23-F1322DDECA9D"), typeof(ILauncher).GUID);
            Phase = "querying launcher";
            var start = (ILauncher)launcher;
            Phase = "checking visibility";
            if (start.IsVisible()) { start.Dismiss(); return; }
            Phase = "resolving monitor";
            target = ((IMonitorService)monitors).GetFromHandle(monitor);
            // Capture visibility only. No taskbar edge, appbar state or user preference changes.
            Enumerate(h => { if (IsBar(h)) saved[h] = IsWindowVisible(h); return true; });
            appearance = StartTaskbarAppearance.Acquire(saved.Keys);
            foreach (var bar in saved.Keys) ShowWindow(bar, 0);
            Phase = "connecting monitor";
            start.ConnectToMonitor(target);
            Phase = "showing Start";
            start.ShowStartView(11, 0);
            Phase = "watching visibility";
            long deadline = Environment.TickCount64 + 3000;
            bool appeared = false;
            while (true)
            {
                if (dismissRequested) { start.Dismiss(); dismissRequested = false; }
                bool visible = start.IsVisible(); appeared |= visible;
                foreach (var bar in saved.Keys) if (IsWindowVisible(bar)) ShowWindow(bar, 0);
                if ((!visible && appeared) || (!appeared && Environment.TickCount64 >= deadline)) break;
                Thread.Sleep(5);
            }
            if (!appeared) throw new InvalidOperationException("Windows did not show Start on the requested monitor.");
            // Launcher visibility changes before the shell's closing animation ends.
            Thread.Sleep(250);
        }
        finally
        {
            Restore();
            AppDomain.CurrentDomain.ProcessExit -= exiting;
            foreach (var value in new[] { target, launcher, monitors, shell })
                if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }
    private static bool IsBar(nint hwnd)
    {
        var name = new StringBuilder(128); GetClassNameW(hwnd, name, name.Capacity);
        return name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
    private static void Enumerate(Func<nint, bool> callback)
    {
        nint hwnd = 0;
        while ((hwnd = FindWindowExW(0, hwnd, null, null)) != 0) if (!callback(hwnd)) break;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowExW(nint parent, nint after, string? name, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetPropW(nint hwnd, string name);
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider { [return: MarshalAs(UnmanagedType.IUnknown)] object QueryService([In, MarshalAs(UnmanagedType.LPStruct)] Guid service, [In, MarshalAs(UnmanagedType.LPStruct)] Guid iid); }
    [ComImport, Guid("4D4C1E64-E410-4FAA-BAFA-59CA069BFEC2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMonitorService
    {
        void GetCount(); void GetConnectedCount(); void GetAt();
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetFromHandle(nint monitor);
    }
    [ComImport, Guid("D8D60399-A0F1-F987-5551-321FD1B49864"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ILauncher
    {
        void ShowStartView(int method, int flags); void Dismiss(); void Method5(); void Method6();
        [return: MarshalAs(UnmanagedType.Bool)] bool IsVisible();
        void Method8(); void Method9();
        void ConnectToMonitor([MarshalAs(UnmanagedType.IUnknown)] object monitor);
    }
}
