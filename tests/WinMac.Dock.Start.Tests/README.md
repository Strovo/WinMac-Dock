# Start routing verification

## Final published dock-click verification

`powershell -NoProfile -File tests/VerifyDockStartClicks.ps1 -ReportDirectory C:/path/to/fresh-report` invokes each actual dock's Start button through UI Automation. It checks the real Start window's monitor, taskbar display state and restoration of the taskbar styles, then closes only the Start menu opened by the test. It refuses to begin if Start is already open. The final published package passed twice on both connected monitors. Generated reports stay local and are ignored by Git.

The original polling-only implementation below was superseded after the real click test detected a taskbar flash. The final implementation also applies zero-alpha layered appearance before opening Start, journals original opacity/style before mutation, restores it after closure, and recovers abandoned leases on the next startup using process-start and Explorer-window identity checks. WS_VISIBLE alone no longer indicates a drawn taskbar: tests also inspect zero-alpha layered state. No cursor movement, appbar/registry preference changes or system injection is added. Millisecond sampling verifies this environment, not every possible compositor frame or future Windows version.

Run from the repository using the supplied .NET 10 SDK:

```
dotnet build src/WinMac.Dock/WinMac.Dock.csproj -p:Platform=x64
dotnet run --project tests/WinMac.Dock.Start.Tests -- --assembly E:/Apps/WinMac-Dock/src/WinMac.Dock/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WinMac.Dock.dll
```

Without `--assembly`, the harness compiles production StartMenuRouting.cs and StartTaskbarAppearance.cs directly. `baseline` invokes Native.OpenStartMenu from the installed assembly (use an old package to reproduce the original bug). `cloak-probe` tests and restores temporary DWM cloaking capability; this machine returns E_ACCESSDENIED (80070005).

The harness creates one owned test form per real screen, invokes the production entry point, observes actual StartMenuExperienceHost HWNDs, monitor handles, DWM cloaking and both taskbar HWND visibility flags, then dismisses only Start opened by the test using the shell's COM Dismiss method. Existing Start surfaces cause a refusal before opening anything. No global key injection, mouse movement, OS preference changes, app deployment or user app shutdown occurs. FindWindowEx traversal is intentional: EnumWindows excluded the immersive Start HWND here and produced false-negative observations.

`reports/baseline.json` records the legacy method opening on the wrong monitor for DISPLAY2. `reports/assembly-routing.json` records both monitors passing through the newly built Native.OpenStartMenuForDock entry point, including closure observation and restoration of taskbar visibility/region type. The installed process remained PID 9348 at its original executable path.

## Scope and limitations

- Both taskbars were already hidden by the user's running dock. Tests did not modify that preference or disable its existing Windhawk suppression bridge. The observations prove monitor routing and no *observed* taskbar visibility in that environment, not bridge-independent zero-frame suppression when HideWindowsTaskbar is false.
- Production now combines pre-hide and temporary zero-alpha appearance with a 5 ms worker loop, restoring captured appearance and visibility after Start closes unless a dock advertises global suppression. It never changes ABM state, taskbar edge, registry preferences or cursor position. These are real test observations, not a compositor-atomic universal guarantee.
- Safe alternatives investigated: cross-process DWMWA_CLOAK was denied; empty taskbar regions were overwritten by Explorer during the menu lifetime. The latter experiment was removed and original regions restored. No system code injection was added.
- The shell monitor service and launcher COM interfaces are private; Windows updates may remove them. Failures are exposed via StartMenuRouting.LastError; the targeted path deliberately does not fall back to primary-display activation.
- This C# harness is feature-level verification. The separate VerifyDockStartClicks.ps1 script additionally tests the newly published installed WinUI dock buttons. Normal and Shift launch branches are wired to Native.OpenStartMenuForDock(Handle).
- Normal process exit restores the transient appearance lease. A forced process kill cannot run cleanup, so the final implementation records a recovery journal before applying opacity; the next dock startup restores abandoned appearance changes.

The final actual-click tests passed with the user's existing permanent-hiding option enabled; visibility modes and future Windows updates beyond this environment have not all been exercised.
