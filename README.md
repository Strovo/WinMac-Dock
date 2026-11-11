<p align="center">
  <img src="assets/logo.svg" alt="WinMac Dock" width="128">
</p>

<h1 align="center">
  <span>WinMac Dock</span>
</h1>
<h3 align="center">
  <span align="center">WinUI3 dock for Windows that looks and feels native</span>
</h3>
<p align="center">
  <span align="center">A standalone Windows 11 dock built with .NET 10, WinUI3, Windows Composition and the Windows shell. It offers native Windows Acrylic and Mica, solid/transparent backgrounds, system light/dark colors, multi-monitor setup, native WinUI menus, and macOS-style icon magnification.</span>
</p>

## Cambios de este fork

Este fork de [Asteski/WinMac-Dock](https://github.com/Asteski/WinMac-Dock) añade las siguientes mejoras al proyecto original:

### Reloj y fecha integrados en el dock

- Hora y fecha local al final de la fila de iconos, dentro del propio dock: no se abre una ventana flotante.
- Formato de 12 o 24 horas y segundos opcionales.
- Sigue el tema, la posición, la orientación y la visibilidad del dock.
- Se activa en **Settings → General → Clock in dock**. Las preferencias se guardan junto con la configuración del dock.
- El espacio del reloj se tiene en cuenta al ajustar el tamaño, ampliar los iconos y animar la fila; el reloj no se magnifica.

### Aplicaciones independientes por monitor

- Selecciona **Settings → General → Display → All Monitors** para mostrar un dock en cada pantalla.
- Cada dock muestra únicamente las ventanas abiertas en ese monitor, incluso al agrupar varias ventanas de una aplicación.
- Al mover una ventana entre pantallas, su entrada pasa al dock correspondiente.
- Las ventanas minimizadas permanecen asociadas al monitor de su posición de restauración.
- Los lanzadores anclados se mantienen en todos los docks; sus ventanas e indicadores de ejecución se calculan por monitor.
- El reloj y la fecha se conservan en cada dock. El modo de un único monitor mantiene el comportamiento anterior.

### Compilación y pruebas

Requiere Windows 11 y el **SDK de .NET 10** para compilar. Los recursos originales necesarios y los scripts de compilación se incluyen en este fork; su procedencia está documentada en [build/RESOURCE-SOURCES.md](build/RESOURCE-SOURCES.md).

```powershell
dotnet publish src/WinMac.Dock/WinMac.Dock.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o artifacts/WinMac-Dock-x64
dotnet run --project tests/WinMac.Dock.Clock.Tests
dotnet run --project tests/WinMac.Dock.Clock.UI.Tests
```

Ejecuta `artifacts/WinMac-Dock-x64/WinMac.Dock.exe` y conserva todos los archivos de la carpeta publicada. La compilación incluye los runtimes de .NET y Windows App SDK. Esta variante se ha compilado y probado en **Windows 11 x64**; no se ha verificado ARM64.

La verificación del paquete utiliza una configuración aislada y no modifica las preferencias habituales:

```powershell
powershell -NoProfile -File build/VerifyPublishedApp.ps1 -Executable artifacts/WinMac-Dock-x64/WinMac.Dock.exe -ReportDirectory C:/ruta/a/informe-nuevo
```

Con el dock ejecutándose en modo **All Monitors**, la prueba de integración requiere exactamente dos monitores conectados. Abre ventanas de prueba propias, comprueba el filtrado inicial, el cambio de pantalla y la minimización, y las cierra al terminar:

```powershell
powershell -NoProfile -File tests/VerifyMonitorDocks.ps1 -ReportDirectory C:/ruta/a/informe-monitores
```

Las pruebas del reloj y la integración en dos monitores se han ejecutado localmente. Los informes generados y las copias de la instalación no se versionan. Consulta [tests/README.md](tests/README.md) para más detalles sobre las pruebas del reloj.

> **Descargas del proyecto original:** los botones y estadísticas siguientes apuntan a Asteski/WinMac-Dock. Sus paquetes no incluyen estas modificaciones; para obtener esta variante, compila el código de este fork.

<p align="center">
  <a href="https://github.com/Asteski/WinMac-Dock/releases/download/v0.9.0/WinMac-Dock-0.9.0-beta-x64.zip"><img alt="Download x64" src="https://img.shields.io/badge/Download-x64-0078D6?style=for-the-badge"></a>
  <a href="https://github.com/Asteski/WinMac-Dock/releases/download/v0.9.0/WinMac-Dock-0.9.0-beta-arm64.zip"><img alt="Download ARM64" src="https://img.shields.io/badge/Download-ARM64-00A86B?style=for-the-badge"></a>
</p>
<p align="center">
  <img alt="Latest Release" src="https://img.shields.io/github/v/release/Asteski/WinMac-Dock">
  <img alt="Release Date" src="https://img.shields.io/github/release-date/Asteski/WinMac-Dock">
</p>
<p align="center">
  <img alt="Downloads (all releases)" src="https://img.shields.io/github/downloads/Asteski/WinMac-Dock/total">
  <img alt="Downloads (latest release)" src="https://img.shields.io/github/downloads/Asteski/WinMac-Dock/latest/total">
</p>

</p>
<p align="center">
  <img src="https://github.com/Asteski/WinMac-Dock/blob/3b64ea8fa7581786ee5bda0c204ddc84bfaeb117/img/img01.png" alt="WinMac Dock Light Mode">
</p>

<p align="center">
  <img src="https://github.com/Asteski/WinMac-Dock/blob/f12d496c8021cd875650275d6865c8c71e558030/img/img02.gif" alt="WinMac Dock Dark Mode">
</p>

**WinMac Dock is a customizable Windows 11 dock with macOS-style magnification, smooth animations, task grouping, window controls, and flexible positioning and alignment on any screen edge or display.**

- **Apps, files, folders and modules:** Pin applications (including _PWA_), folders, shortcuts and separators. Built-in modules include _Downloads, Recycle Bin, Start Menu, Task View_ and _Search_, with app-specific commands for _File Explorer, Settings, Microsoft Store, Terminal, Edge, Xbox_, _Snipping Tool_ and _Task Manager_.

- **Appearance:** Customize icon sizes, magnification, spacing, margins, borders, highlights, running indicators, and light/dark appearance. Choose _Acrylic_, _Mica_, _solid_ or _transparent_ backgrounds, or use _Default_ to match the Windows taskbar’s native background material.

- **Clock in dock:** Enable the digital clock in _Settings → General → Clock in dock_. It displays the local time and a compact date after the last dock icon, supports 12/24-hour time and optional seconds, and follows the dock's theme, position and visibility. It is part of the dock, not a floating widget.

- **Positioning and displays:** Place the dock along the top, bottom, left or right edge; adjust alignment, float it away from the edge, or span the full edge. Show it on one display or across all monitors, with independent positioning and behavior per display. In _All Monitors_ mode, each dock shows running windows from its own monitor only; pinned launchers remain on every dock. Moving a window between monitors moves its running entry, and minimized windows remain associated with their restore monitor.

- **Window management:** Activate, minimize, restore or close windows from the dock. Optionally group windows by application, display only minimized applications, and direct Windows minimize/restore animations toward their dock icons.

- **Visibility:** Configure automatic hiding, edge reveal, fullscreen behavior and exclusions, screen-space reservation, and a customizable show/hide keyboard shortcut.

- **Folders and Downloads:** Browse folder contents in grid, list, or menu views, with sorting and folder-stack previews. Automatically pin newly downloaded files to a separate dock section, limit how many are retained, and optionally unpin them after opening. Automatically pin attached external disks, USB sticks and memory cards to a separate dock section.
  
- **Recycle Bin:** Drop files and folders onto its icon to recycle them, empty the bin from its context menu, and optionally play sounds for both actions. Customize its empty and full icons separately.

- **Per-item customization:** Change names, icons, launch arguments, working directories, and supported administrator-launch options. Customized item and module properties are remembered when unpinned and restored when pinned again.

- **Custom icons:** Use _ICO, PNG, JPG/JPEG, BMP, GIF, WebP_ or _SVG_ images, or select icons embedded in _DLL_ and _EXE_ files. Assign separate icons for light and dark mode.

- **Exclusions:** Exclude any running app from appearing in the dock by window title, process name, or window class.

- **Task View integration:** Keep the dock visible during Task View using the optional [Windhawk bridge mod](https://github.com/Asteski/WinMac-Dock/blob/a1e8460b41cd09a1d9ab7bce7c2282145a4a7200/mods/winmac-dock-task-view.wh.cpp).

### Mouse shortcuts

- **Alt+drag:** Rearrange items while items positions are locked.
- **Alt+right-click** open Dock's main menu or separator sub-menu.
- **Ctrl+click:** Minimize or restore all windows belonging to the clicked application.
- **Shift+click:** Launch the item again, opening another window where supported.
- **Ctrl+Shift+click:** Request an administrator launch where supported.

### Getting started

Download the x64 or ARM64 release package, extract it, and run **WinMac.Dock.exe**. Keep the extracted files together; the required .NET and Windows App SDK runtimes are bundled.
