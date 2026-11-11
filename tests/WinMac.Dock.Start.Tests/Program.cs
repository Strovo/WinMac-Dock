using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class Program
{
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, nint data);
    internal delegate bool EnumProc(nint hwnd, nint data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder s, int n);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);
    static bool Drawn(nint hwnd) => IsWindowVisible(hwnd) && !((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x80000) != 0
        && GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out uint flags) && (flags & 2) != 0 && alpha == 0);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hwnd, uint attr, out int value, int size);
    [DllImport("gdi32.dll")] static extern nint CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint h);
    [DllImport("user32.dll")] static extern int GetWindowRgn(nint h,nint region);
    static int Region(nint h) { var r=CreateRectRgn(0,0,0,0);try{return GetWindowRgn(h,r);}finally{DeleteObject(r);} }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, uint attr, ref int value, int size);
    [DllImport("user32.dll")] static extern bool PostMessage(nint hwnd, uint msg, nuint w, nint l);
    static string Class(nint h) { var s=new StringBuilder(256); GetClassName(h,s,256); return s.ToString(); }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint FindWindowEx(nint parent,nint after,string? cls,string? title);
    static List<nint> Windows(Func<nint,bool> filter) { var list=new List<nint>(); nint h=0; while((h=FindWindowEx(0,h,null,null))!=0) {if(filter(h))list.Add(h);} return list; }
    static readonly HashSet<uint> startProcesses = System.Diagnostics.Process.GetProcessesByName("StartMenuExperienceHost").Select(p => { using (p) return (uint)p.Id; }).ToHashSet();
    static List<nint> StartWindows() => Windows(h=> {
        if (Class(h) != "Windows.UI.Core.CoreWindow") return false;
        GetWindowThreadProcessId(h,out var p);
        return startProcesses.Contains(p) && IsWindowVisible(h) && (DwmGetWindowAttribute(h,14,out var c,4)!=0 || c==0);
    });
    [STAThread] static int Main(string[] args)
    {
        var results=new List<object>(); bool success=true;
        if(args.Contains("cloak-probe")) {
            foreach(var h in Windows(h=>Class(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) {
                DwmGetWindowAttribute(h,14,out int original,4); int value=1;
                int hr=DwmSetWindowAttribute(h,13,ref value,4); DwmGetWindowAttribute(h,14,out int actual,4);
                Console.WriteLine($"{h:X}: cloak hr={hr:X8} before={original} after={actual}");
                value=original&1; DwmSetWindowAttribute(h,13,ref value,4);
            } return 0;
        }

        if(StartWindows().Count>0) { Console.WriteLine("BLOCKED: Start already open; not closing user surface."); return 2; }
        int assemblyArg=Array.IndexOf(args,"--assembly");
        var assembly=assemblyArg>=0?Assembly.LoadFrom(args[assemblyArg+1]):typeof(Program).Assembly;
        var routing=assembly.GetType("WinMac.Dock.StartMenuRouting");
        if(routing==null && !args.Contains("baseline")) { Console.WriteLine("FAIL: monitor-targeted Start entry point is missing"); return 1; }
        foreach(var screen in Screen.AllScreens)
        {
            using var owner=new Form { ShowInTaskbar=false, StartPosition=FormStartPosition.Manual, Bounds=new System.Drawing.Rectangle(screen.Bounds.X+100,100,100,100), Text="Start routing test fixture" };
            owner.Show(); Application.DoEvents(); var monitor=MonitorFromWindow(owner.Handle,2);
            var bars=Windows(h=>Class(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"); var before=bars.ToDictionary(h=>h,IsWindowVisible);
            var samples=new List<object>(); bool observed=false, wrong=false, revealed=false;
            var originalRegions=bars.ToDictionary(h=>h,Region);
            try
            {
                if(args.Contains("baseline")) {
                    var asm=Assembly.LoadFrom("E:/Apps/WinMac-Dock/WinMac-Dock-reloj-integrado-x64/WinMac.Dock.dll");
                    asm.GetType("WinMac.Dock.Native")!.GetMethod("OpenStartMenu")!.Invoke(null,null);
                } else if(assemblyArg>=0) assembly.GetType("WinMac.Dock.Native")!.GetMethod("OpenStartMenuForDock")!.Invoke(null,[owner.Handle,monitor]);
                else routing!.GetMethod("Open",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,[owner.Handle,monitor]);
                var timer=System.Diagnostics.Stopwatch.StartNew();
                while(timer.ElapsedMilliseconds<2200) {
                    Application.DoEvents(); var start=StartWindows();
                    if(start.Count>0) { observed=true; wrong |= start.Any(h=>MonitorFromWindow(h,2)!=monitor); }
                    bool visible=bars.Any(Drawn); if(start.Count>0 && visible) revealed=true;
                    if(samples.Count<30 || timer.ElapsedMilliseconds%100<10) samples.Add(new { ms=timer.ElapsedMilliseconds, start=start.Select(h=>new {hwnd=h.ToInt64(),monitor=MonitorFromWindow(h,2).ToInt64()}).ToArray(),bars=bars.Select(h=>new {hwnd=h.ToInt64(),visible=IsWindowVisible(h),region=Region(h)}).ToArray() });
                    Thread.Sleep(5);
                }
            }
            catch(Exception e) { Console.WriteLine(e); success=false; }
            finally {
                // Escape is posted only to a surface opened by this test, never global input.
                if (routing != null && StartWindows().Count > 0) {
                    if(args.Contains("baseline")) routing.GetMethod("Open")!.Invoke(null,[owner.Handle,monitor]);
                    else routing.GetMethod("DismissOpened",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
                }
                else foreach(var h in StartWindows()) PostMessage(h,0x100,0x1B,0);
                var t=System.Diagnostics.Stopwatch.StartNew();
                while(t.ElapsedMilliseconds<900) {
                    Application.DoEvents(); var closing=StartWindows();
                    if(closing.Count>0 && bars.Any(Drawn)) revealed=true;
                    samples.Add(new {closingMs=t.ElapsedMilliseconds,start=closing.Select(h=>new {hwnd=h.ToInt64(),monitor=MonitorFromWindow(h,2).ToInt64()}).ToArray(),bars=bars.Select(h=>new {hwnd=h.ToInt64(),visible=IsWindowVisible(h)}).ToArray()});
                    Thread.Sleep(5);
                }
            }
            bool restored=bars.All(h=>IsWindowVisible(h)==before[h] && Region(h)==originalRegions[h]);
            bool passed=observed&&!wrong&&!revealed&&restored;
            results.Add(new {screen=screen.DeviceName,monitor=monitor.ToInt64(), observed,wrong,revealed,restored,passed,originalVisibility=before.Select(p=>new {hwnd=p.Key.ToInt64(),visible=p.Value}).ToArray(),samples}); success &= passed;
            Console.WriteLine($"{screen.DeviceName}: observed={observed}, wrong={wrong}, revealed={revealed}, restored={restored}, error={routing?.GetProperty("LastError", BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null)}");

            if(StartWindows().Count>0) { Console.WriteLine("Cleanup failed; stopping to avoid toggling user Start."); success=false; break; }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,args.Contains("baseline")?"baseline.json":assemblyArg>=0?"assembly-routing.json":"routing.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        return success?0:1;
    }
}
