# Restored build resources

The icon, font, SVG and default-settings files missing from this source checkout were recovered from the matching upstream v0.9.0 x64 distribution, not replaced with generated artwork or guessed defaults.

- Source: https://github.com/Asteski/WinMac-Dock/releases/download/v0.9.0/WinMac-Dock-0.9.0-beta-x64.zip
- SHA-256: `216321967e7a82fc28ba1a8577af4f6637aacaab2db75b04410b0d5d45cac083` (verified against the release API metadata).
- `assets/fonts/*.ttf`: original distribution files.
- `assets/*.ico`, `assets/defaults/*.ico`, `assets/download-badge/*.ico`: reconstructed ICO containers around the original native icon-image resources in `WinMac.Dock.exe` and `WinMac.Dock.Icons.dll`.
- `assets/download-badge/downloads-badge.svg`: original `WinMac.Dock.Icons.DownloadsBadge.svg` assembly resource.
- `src/WinMac.Dock.Core/Defaults/beta-defaults.json`: original `WinMac.Dock.BetaDefaults.json` assembly resource.

`CompileIconResources.ps1` is a new local build helper. It locates the resource compiler provided by the restored Windows SDK BuildTools NuGet package (or PATH), compiles the existing RC files and supports the SDK-generated manifest.

The application project excludes `*Checks.cs` and `SeparatorSpacingAudit.cs`: those development checks reference instrumentation absent from this checkout and contain duplicate production members. Production `TaskViewBand.cs` remains compiled. The independent clock tests under `tests/` remain available.

## Build and verify

With the .NET 10 SDK on Windows:

```shell
dotnet publish src/WinMac.Dock/WinMac.Dock.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o artifacts/WinMac-Dock-con-reloj-x64
powershell -NoProfile -ExecutionPolicy Bypass -File build/VerifyPublishedApp.ps1 -Executable artifacts/WinMac-Dock-con-reloj-x64/WinMac.Dock.exe -ReportDirectory C:/path/to/a/fresh/verification-directory
```

Use an absolute report-directory path. Verification starts the complete published application with a unique single-instance identity and isolated settings/log paths, checks the dock, embedded clock, timer, layout at all four edges, hover geometry, preference persistence and Settings window, then closes it. It verifies that no floating clock window exists and skips startup registration. Normal launch behavior is unchanged. Baseline nullable-annotation warnings remain in the source; the build completes with zero errors.
