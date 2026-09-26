# Taskbar Proximity Opacity

Windows taskbar opacity follows the mouse distance independently on each display. The taskbar stays in place; both its background and its contents (including icons) are faded as part of the taskbar window.

## Current scope

- Windows 11, x64.
- The standard primary and secondary Windows taskbars.
- Independent calculations for every monitor that has a taskbar. Turn on **Show my taskbar on all displays** in Windows settings to have a taskbar on each display.
- Settings window for distance range, far opacity, per-display enable/disable, and optional startup.
- Tray menu to pause or exit. A paused taskbar is restored to full opacity.
- Follows **Windows Settings > Personalization > Colors > Transparency effects** without changing that setting. When ON, a native acrylic surface behind each enabled taskbar blurs the background while the taskbar remains visible; OFF keeps the existing opacity-only behavior. Changes are picked up within about one second, even with a stationary cursor. High contrast disables the added blur. Full transparency, pause, disabling a display, and exit remove the blur surface.
- Maximized windows on enabled displays use the entire screen, including the taskbar area. The utility temporarily expands each enabled display's Windows work area; this also affects snapping and other apps that use that area. Pause, exit, or disabling a display restores its previous work area. Changes are checked every second, independently of cursor movement. A recovery file restores work areas on the next launch after a forced termination. Already-maximized windows may need to be restored and maximized again when starting or pausing the utility.
- Settings live in `%LOCALAPPDATA%\TaskbarProximityOpacity\settings.json`.

The distance uses the shortest distance from the cursor to that monitor's taskbar rectangle. Opacity is applied directly from the current distance, without time-based animation. The distance range is a percentage of the display dimension perpendicular to the taskbar, so it scales with resolution and rotation. At or beyond the configured range, opacity is zero by default. The calculation is independent for each display.

## Build and run

Requires the .NET 10 Windows Desktop Runtime. Build with:

```powershell
dotnet build .\TaskbarProximityOpacity.csproj -c Release -o .\artifact
```

Run `artifact\TaskbarProximityOpacity.exe`. To make a self-contained single-file package on a machine with NuGet access, publish to `artifact\publish`:

```powershell
dotnet publish .\TaskbarProximityOpacity.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\artifact\publish
```

## Settings file

The app creates its JSON settings after saving in the settings window. Distance and opacity values can also be changed by editing this file while the app is closed. `DisabledDisplays` contains Windows display device names (for example `\\.\DISPLAY2`). An empty list enables all detected displays.

```json
{
  "FadeDistanceRatio": 0.35,
  "FarOpacityPercent": 0,
  "StartWithWindows": false,
  "DisabledDisplays": []
}
```

`FadeDistanceRatio` is a fraction: `0.35` means 35% of the display dimension perpendicular to the taskbar. Values supported by the settings UI are 0.01–1.00. `FarOpacityPercent` can be 0–40; use 0 for complete transparency. Opacity changes immediately as cursor distance changes.

## Install and remove

This is a portable tray utility; there is no installer. Keep the generated `.exe`, `.dll`, `.deps.json`, and `.runtimeconfig.json` together, then run it. Optional startup is managed from the settings window. To remove it, exit the app, turn off startup if enabled, then delete the app files and `%LOCALAPPDATA%\TaskbarProximityOpacity`. On normal exit the original taskbar window styles and opacity are restored. If the app is force-terminated, the next launch reads a recovery record and restores them. If you force-terminate it and will not launch it again, restart Windows Explorer before deleting the recovery data.

## Performance notes

The utility samples the cursor every 30 ms and writes taskbar opacity only when the value changes. With transparency effects enabled, it creates one taskbar-sized acrylic surface immediately below each visible controlled taskbar. The surfaces do not activate or cover taskbar icons, and Windows handles the blur rendering. Acrylic cannot be alpha-faded with layered windows on Windows 11: blur strength is constant while the taskbar is visible and the surface is removed at zero opacity. A borderless game may still cause opacity writes while the cursor moves through the distance range; the tray menu can pause updates and restore full opacity.

Windows taskbar window classes, layering, and the acrylic composition API are implementation details, so a future Windows update can change behavior. If the acrylic API is unavailable, opacity control continues without the added blur. The app does not relocate taskbars or toggle Windows auto-hide.


## Verification

Run `dotnet run --project tests/Validation.csproj -c Release` on Windows with .NET 10. This integration test briefly changes each display's work area and opens test windows, then restores the original work areas in a finally block. It checks maximization, disabled displays, repeat updates, restoration, recovery from the journal, and blur surface lifecycle, placement, and focus. Use `-- --blur-only` to skip work-area tests, or `-- --blur-demo` for a visual striped-background blur check (closes automatically after two minutes).
