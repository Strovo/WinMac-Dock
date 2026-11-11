# Integrated dock clock checks

Requires Windows 11 and the .NET 10 SDK.

```shell
dotnet run --project tests/WinMac.Dock.Clock.Tests
dotnet run --project tests/WinMac.Dock.Clock.UI.Tests
```

The console checks cover time formats, seconds, localized dates, reserved clock length, and persistence through `DockSettings` serialization. A failing assertion exits with an error.

The UI checks mount the actual `DockClockView` after an icon in the same row, verify the timer, horizontal and vertical sizing, replaced settings, inherited theme, disable/re-enable and timer shutdown. They leave `clock-ui-test.txt` in the UI test output directory and exit with a nonzero code on failure. They do not modify the user's dock configuration.

For a manual preview:

```shell
dotnet run --project tests/WinMac.Dock.Clock.UI.Tests -- --preview
```

The test projects compile the actual clock/core source files directly so they can run independently of bundled icons, fonts, defaults and resource-compilation scripts. Passing them does **not** verify a complete dock build or the Settings UI integration. The regular application must still be built from `src/WinMac.Dock/WinMac.Dock.csproj` with its original build resources present.

`build/VerifyPublishedApp.ps1` verifies the complete published application using isolated settings. It checks that the clock shares the dock's XamlRoot, stays after the last icon at all four edges, reserves its own hover slot without magnification, can be disabled/re-enabled, has no floating window, opens Settings and persists its preferences.
