# Taskbar attention regression

These tests use **real Explorer taskbars** and owned WinForms background windows calling `FlashWindowEx(FLASHW_ALL | FLASHW_TIMERNOFG)` for 12 seconds per display. They sample `WS_VISIBLE` and layered `LWA_ALPHA`, not visibility alone. Do not run desktop tests concurrently. Explorer taskbars are session-global: isolated files/mutexes do **not** provide visual isolation from a normal running dock. No registry/preferences, FlashWindowEx interception, injection, or pointer movement is required.

From the repository root, with a .NET 10 SDK (`$DOTNET` below is its absolute executable path):

```bash
"$DOTNET" run --project tests/WinMac.Dock.Taskbar.Tests -- E:/Apps/WinMac-Dock/test-results/attention-new
"$DOTNET" run --project tests/WinMac.Dock.Taskbar.Tests -- E:/Apps/WinMac-Dock/test-results/lifecycle-new --lifecycle
"$DOTNET" run --project tests/WinMac.Dock.Start.Journal.Tests
```

Use a fresh absolute report directory. Lifecycle checks cover forced Explorer-style re-show on both taskbars; opacity serialization; old journals without opacity; nested Start appearance leases; Hide on/off during Start; exact pre-existing layered color key/alpha/flags using an owned window; stale process identity; journal cleanup; and restoration despite an injected appbar-read failure. That last test deliberately logs an IOException but must exit zero and restore the actual taskbar appearance.

For genuine crash recovery, build first, then run the test DLL in a separate process with `REPORT --crash` (expected exit **23**, no Dispose/finally), followed immediately by a second process with the same `REPORT --recover`. Compare `before-crash.json` and `recovered.json` and require removal of `taskbar-visibility.json`. Never leave the crash phase unrecovered. Recovery validates HWND/process-start identity; no Explorer process is killed by this test.

## Full published app

```bash
"$DOTNET" publish src/WinMac.Dock/WinMac.Dock.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o E:/Apps/WinMac-Dock/artifacts/WinMac-Dock-attention-fix-x64
python tests/run_taskbar_attention_smoke.py --dotnet "$DOTNET" --publish E:/Apps/WinMac-Dock/artifacts/WinMac-Dock-attention-fix-x64 --report E:/Apps/WinMac-Dock/test-results/published-attention-new
```

The runner writes the opt-in `attention-fixture.json` in the isolated smoke profile. The full app starts an external fixture, verifies actual shell attention tracking on both monitors while flashing continues, and the runner invokes **actual Start buttons** via UI Automation on the two test-process dock windows. It checks Start placement, no taskbar rendering and appearance restoration, then verifies original Explorer appearance, normal app PID/path, and SHA-256 of normal settings are unchanged. Full clock/pinning/Settings smoke checks also run. The fixture is never suppressed with `FlashWindowEx(...STOP)` until its 12-second sampling interval finishes.

## Recorded evidence

- `test-results/taskbar-attention-red/attention.json`: before production edits, real FlashWindowEx caused one rendered taskbar sample on DISPLAY1; DISPLAY2 had no sampled reveal. Both taskbars were enumerated; the existing normal app/bridge remained running.
- `test-results/taskbar-attention-contract-red/attention.json`: guaranteed RED on missing persistent zero alpha.
- `test-results/taskbar-attention-opacity-probe/attention.json`: diagnostic temporary-alpha probe, no sampled reveals on either display.
- `test-results/taskbar-attention-green/attention.json`: permanent-opacity sustained attention GREEN.
- `test-results/taskbar-lifecycle-final-green/attention.json`: lifecycle checks GREEN.
- `test-results/taskbar-real-crash-recovery/result.json`: real terminated owner, new recovery process, exact original appearance restored, journal removed.
- `test-results/taskbar-published-final/`: final full app smoke, external attention samples, two actual dock Start clicks, unchanged settings/normal process, restored taskbar appearance. `integration.json` passed; app/click exits zero.

The highest-ranked cause was Explorer re-showing taskbars between periodic `SW_HIDE` calls. Baseline reproduced without Start, ruling out Start as a prerequisite; both bars were enumerated, ruling out missing-monitor enumeration. Persistent alpha zero addresses re-show drawing without stopping app alerts.

Limits: polling is not proof of compositor-level zero-frame invisibility. A pre-existing normal dock/suppression bridge was not disabled. Physical monitor hotplug and a real Explorer restart were not performed to avoid disturbing the normal desktop. Process-lifetime identity validation is tested at the owned-window/journal seam; real Explorer replacement-handle recapture and topology changes were not verified live. No deployment, commit or push is performed.
