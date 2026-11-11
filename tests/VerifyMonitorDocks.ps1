param([Parameter(Mandatory=$true)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class MonitorDockNative {
 public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
 public static IntPtr[] Docks(int processId) {
  var result = new List<IntPtr>();
  EnumWindows((h,p)=>{ uint pid; GetWindowThreadProcessId(h,out pid); if(pid==processId){ var s=new StringBuilder(512); GetWindowText(h,s,512); if(s.ToString()=="WinMac Dock") result.Add(h); } return true; },IntPtr.Zero);
  return result.ToArray();
 }
}
'@
$null = New-Item -ItemType Directory -Path $ReportDirectory -Force
$commandFile = Join-Path $ReportDirectory 'fixture-command.txt'
$report = @{ Passed=$false; Checks=@(); Snapshots=@(); Error=$null }
$fixture = $null
function Check([bool]$value,[string]$message) { if (-not $value) { throw $message }; $report.Checks += $message }
function Snapshot {
    $dockProcess = Get-Process -Name 'WinMac.Dock' -ErrorAction Stop
    $windows = [MonitorDockNative]::Docks($dockProcess.Id)
    $result = @()
    foreach ($handle in $windows) {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        $elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
        $names = @($elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button } | ForEach-Object { $_.Current.Name })
        $clock = @($elements | Where-Object { $_.Current.Name -eq 'Dock clock' }).Count -eq 1
        $result += @{ Handle=$handle.ToInt64(); Monitor=[MonitorDockNative]::MonitorFromWindow($handle,2).ToInt64(); Screen=[System.Windows.Forms.Screen]::FromHandle($handle).DeviceName; Buttons=$names; Clock=$clock }
    }
    return $result
}
function VerifyPhase([string]$phase,[bool]$swapped) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $snap = @(Snapshot)
        $correct = $snap.Count -eq 2
        foreach ($dock in $snap) {
            $wantA = $dock.Screen -eq $screens[$(if ($swapped) { 1 } else { 0 })].DeviceName
            $hasA = @($dock.Buttons | Where-Object { $_ -eq 'DockMonitorFixture-A' }).Count -eq 1
            $hasB = @($dock.Buttons | Where-Object { $_ -eq 'DockMonitorFixture-B' }).Count -eq 1
            $correct = $correct -and ($hasA -eq $wantA) -and ($hasB -eq (-not $wantA))
        }
        if ($correct) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    $report.Snapshots += @{Phase=$phase; Docks=$snap}
    Check $correct "$phase : each fixture appears only on its own monitor"
    foreach ($dock in $snap) {
        Check $dock.Clock "$phase : clock remains embedded on $($dock.Screen)"
        Check ($dock.Buttons -contains 'Start Menu') "$phase : pinned launcher remains on $($dock.Screen)"
    }
}
try {
    $screens = [System.Windows.Forms.Screen]::AllScreens
    Check ($screens.Count -eq 2) 'Two connected monitors available for integration test'
    $initial = @(Snapshot)
    Check ($initial.Count -eq 2) 'One real dock window exists on each monitor'
    [System.IO.File]::WriteAllText($commandFile,'initial')
    $fixtureScript = Join-Path $PSScriptRoot 'MonitorFixture.ps1'
    $fixture = Start-Process powershell.exe -ArgumentList @('-NoProfile','-WindowStyle','Hidden','-File',('"'+$fixtureScript+'"'),'-CommandFile',('"'+$commandFile+'"')) -PassThru
    VerifyPhase 'Initial placement' $false
    [System.IO.File]::WriteAllText($commandFile,'swap')
    VerifyPhase 'After moving between monitors' $true
    [System.IO.File]::WriteAllText($commandFile,'minimize')
    Start-Sleep -Seconds 2
    VerifyPhase 'Minimized windows retain their monitor' $true
    $report.Passed = $true
} catch { $report.Error = $_.Exception.Message }
finally {
    if ($fixture) {
        [System.IO.File]::WriteAllText($commandFile,'close')
        if (-not $fixture.WaitForExit(5000)) { $fixture.Kill(); $fixture.WaitForExit() }
    }
    $json = $report | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText((Join-Path $ReportDirectory 'monitor-docks-report.json'),$json)
    Write-Output $json
}
if (-not $report.Passed) { exit 1 }
