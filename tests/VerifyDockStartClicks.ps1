param([Parameter(Mandatory=$true)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
public static class DockStartClickNative {
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p,IntPtr a,string c,string t);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
 [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr h,out uint key,out byte alpha,out uint flags);
 public static bool Drawn(IntPtr h){if(!IsWindowVisible(h))return false;uint k,f;byte a;return !((GetWindowLongPtr(h,-20).ToInt64()&0x80000)!=0&&GetLayeredWindowAttributes(h,out k,out a,out f)&&(f&2)!=0&&a==0);}
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,uint attr,out int value,int size);
 public static IntPtr[] All() { var a=new List<IntPtr>(); IntPtr h=IntPtr.Zero; while((h=FindWindowEx(IntPtr.Zero,h,null,null))!=IntPtr.Zero)a.Add(h);return a.ToArray(); }
 public static string Class(IntPtr h) {var s=new StringBuilder(256);GetClassName(h,s,256);return s.ToString();}
 public static IntPtr[] Docks(int processId) {return All().Where(h=>{uint p;GetWindowThreadProcessId(h,out p);var s=new StringBuilder(256);GetWindowText(h,s,256);return p==processId&&s.ToString()=="WinMac Dock";}).ToArray();}
 public static IntPtr[] Bars(){return All().Where(h=>Class(h)=="Shell_TrayWnd"||Class(h)=="Shell_SecondaryTrayWnd").ToArray();}
 public static IntPtr[] StartWindows(){var ids=new HashSet<uint>(Process.GetProcessesByName("StartMenuExperienceHost").Select(p=>{using(p)return (uint)p.Id;}));return All().Where(h=>{uint p;int c;GetWindowThreadProcessId(h,out p);return ids.Contains(p)&&Class(h)=="Windows.UI.Core.CoreWindow"&&IsWindowVisible(h)&&(DwmGetWindowAttribute(h,14,out c,4)!=0||c==0);}).ToArray();}
 public static void Dismiss(){object shell=null,launcher=null;try{shell=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239")));launcher=((Services)shell).QueryService(new Guid("6F86E01C-C649-4D61-BE23-F1322DDECA9D"),typeof(Launcher).GUID);((Launcher)launcher).Dismiss();}finally{if(launcher!=null&&Marshal.IsComObject(launcher))Marshal.ReleaseComObject(launcher);if(shell!=null&&Marshal.IsComObject(shell))Marshal.ReleaseComObject(shell);}}
 [ComImport,Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Services{[return:MarshalAs(UnmanagedType.IUnknown)]object QueryService([In,MarshalAs(UnmanagedType.LPStruct)]Guid s,[In,MarshalAs(UnmanagedType.LPStruct)]Guid i);}
 [ComImport,Guid("D8D60399-A0F1-F987-5551-321FD1B49864"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface Launcher{void ShowStartView(int m,int f);void Dismiss();void M5();void M6();[return:MarshalAs(UnmanagedType.Bool)]bool IsVisible();}
}
'@
$report = @{Passed=$false; Error=$null; Docks=@()}
$opened = $false
try {
    if ([DockStartClickNative]::StartWindows().Count -gt 0) { throw 'Start is already open; refusing to close the user surface.' }
    $process = Get-Process -Name 'WinMac.Dock' -ErrorAction Stop
    $docks = [DockStartClickNative]::Docks($process.Id)
    if ($docks.Count -ne 2) { throw 'Expected two running dock windows.' }
    foreach ($handle in $docks) {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Start Menu')
        $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
        if (-not $button) { throw 'Start Menu button is absent.' }
        $requested = [DockStartClickNative]::MonitorFromWindow($handle,2).ToInt64()
        $observed = $false; $wrong = $false; $revealed = $false; $samples = 0
        $styles = @{}
        foreach ($bar in [DockStartClickNative]::Bars()) { $styles[$bar.ToInt64()] = [DockStartClickNative]::GetWindowLongPtr($bar,-20).ToInt64() }
        $opened = $true
        ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        while ($watch.ElapsedMilliseconds -lt 2500) {
            $start = [DockStartClickNative]::StartWindows()
            if ($start.Count -gt 0) {
                $observed = $true
                foreach ($window in $start) { if ([DockStartClickNative]::MonitorFromWindow($window,2).ToInt64() -ne $requested) { $wrong = $true } }
                foreach ($bar in [DockStartClickNative]::Bars()) { if ([DockStartClickNative]::Drawn($bar)) { $revealed = $true } }
            }
            $samples++
            Start-Sleep -Milliseconds 10
        }
        [DockStartClickNative]::Dismiss()
        $deadline = [DateTime]::UtcNow.AddSeconds(3)
        while ([DockStartClickNative]::StartWindows().Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
        $closed = [DockStartClickNative]::StartWindows().Count -eq 0
        if ($closed) { $opened = $false }
        Start-Sleep -Milliseconds 700
        $restored = $true
        foreach ($bar in [DockStartClickNative]::Bars()) { if ([DockStartClickNative]::GetWindowLongPtr($bar,-20).ToInt64() -ne $styles[$bar.ToInt64()]) { $restored = $false } }
        $report.Docks += @{Handle=$handle.ToInt64(); RequestedMonitor=$requested; StartObserved=$observed; WrongMonitor=$wrong; TaskbarObservedVisible=$revealed; Closed=$closed; AppearanceRestored=$restored; Samples=$samples}
        if (-not $observed -or $wrong -or $revealed -or -not $closed -or -not $restored) { throw 'Actual dock Start click did not satisfy monitor, visibility or cleanup checks.' }
    }
    $report.Passed = $true
} catch { $report.Error = $_.Exception.Message }
finally {
    if ($opened) { [DockStartClickNative]::Dismiss() }
    $null = New-Item -ItemType Directory -Path $ReportDirectory -Force
    $json = $report | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText((Join-Path $ReportDirectory 'dock-start-clicks.json'),$json)
    Write-Output $json
}
if (-not $report.Passed) { exit 1 }
