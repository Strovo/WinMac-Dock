using System.Runtime.InteropServices;
using System.Text;
namespace WinMac.Dock;
// Real Win32 seam, no mocked visibility, appbar state, flashing or opacity.
internal static class Log { internal static string DirectoryPath = ""; internal static void Write(Exception ex) => Console.Error.WriteLine(ex); }
internal static class Native
{
    internal delegate bool EnumProc(nint h,nint p);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback,nint p);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint h,int command);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int ht,uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint h,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(nint h,StringBuilder text,int size);
    internal static string Class(nint h) { var s=new StringBuilder(256); GetClassName(h,s,256);return s.ToString(); }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left,Top,Right,Bottom; public int Width=>Right-Left; public int Height=>Bottom-Top; }
    [StructLayout(LayoutKind.Sequential)] internal struct APPBARDATA { public uint Size; public nint Window; public uint CallbackMessage,Edge; public RECT Rect; public nint Param; }
    [DllImport("shell32.dll",EntryPoint="SHAppBarMessage")] static extern nuint ShellAppBarMessage(uint msg,ref APPBARDATA data);
    internal static bool FailPositionRead;
    internal static nuint SHAppBarMessage(uint msg,ref APPBARDATA data) {
        if(msg==5&&FailPositionRead) throw new IOException("Injected unavailable Explorer configuration read");
        return ShellAppBarMessage(msg,ref data);
    }
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint h,out RECT r);
    internal static RECT Bounds(nint h) {GetWindowRect(h,out var r);return r;}
    internal sealed record Display(nint Handle,string Device,RECT Bounds);
    internal static List<Display> Monitors()=>Screen.AllScreens.Select(s=> {
        using var f=new Form {StartPosition=FormStartPosition.Manual,Location=new(s.Bounds.X+40,s.Bounds.Y+40)};
        return new Display(MonitorFromWindow(f.Handle,2),s.DeviceName,new RECT{Left=s.Bounds.Left,Top=s.Bounds.Top,Right=s.Bounds.Right,Bottom=s.Bounds.Bottom});
    }).ToList();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint SendMessageTimeout(nint h,uint msg,nuint wp,nint lp,uint flags,uint timeout,out nint result);
    internal static bool SetTaskbarEdge(nint h,uint edge)=>SendMessageTimeout(h,0x400+0x1CA,6,(nint)edge,2,1000,out _)!=0;
}
