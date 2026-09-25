# Taskbar Proximity Opacity

Windows taskbar opacity follows the mouse distance independently on each display. The taskbar stays in place; both its background and its contents (including icons) are faded as part of the taskbar window.

## Current scope

- Windows 11, x64.
- The standard primary and secondary Windows taskbars.
- Independent calculations for every monitor that has a taskbar. Turn on **Show my taskbar on all displays** in Windows settings to have a taskbar on each display.
- Settings window for fade distance, fade time, far opacity, per-display enable/disable, and optional startup.
- Tray menu to pause or exit. A paused taskbar is restored to full opacity.
- Settings live in `%LOCALAPPDATA%\TaskbarProximityOpacity\settings.json`.

The distance uses the shortest distance from the cursor to that monitor's taskbar rectangle. The fade range is a percentage of the display dimension perpendicular to the taskbar, so it scales with resolution and rotation. At or beyond the configured range, opacity is zero by default. The calculation and fade are independent for each display.

## Build and run

Requires the .NET 10 Windows Desktop Runtime. Build with:

```powershell
dotnet build .\TaskbarProximityOpacity.csproj -c Release
```

Run `bin\Release\net10.0-windows\win-x64\TaskbarProximityOpacity.exe` (or the output location selected by the build). To make a self-contained single-file package on a machine with NuGet access:

```powershell
dotnet publish .\TaskbarProximityOpacity.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Settings file

The app creates its JSON settings after saving in the settings window. Distance and opacity values can also be changed by editing this file while the app is closed. `DisabledDisplays` contains Windows display device names (for example `\\.\DISPLAY2`). An empty list enables all detected displays.

```json
{
  "FadeDistanceRatio": 0.35,
  "FadeDurationMs": 100,
  "FarOpacityPercent": 0,
  "StartWithWindows": false,
  "DisabledDisplays": []
}
```

`FadeDistanceRatio` is a fraction: `0.35` means 35% of the display dimension perpendicular to the taskbar. Values supported by the settings UI are 0.01–1.00. `FarOpacityPercent` can be 0–40; use 0 for complete transparency. The fade curve is shaped to make the bar perceptible earlier in the approach, and the animation follows a moving cursor target without restarting its timer.

## Install and remove

This is a portable tray utility; there is no installer. Keep the generated `.exe`, `.dll`, `.deps.json`, and `.runtimeconfig.json` together, then run it. Optional startup is managed from the settings window. To remove it, exit the app, turn off startup if enabled, then delete the app files and `%LOCALAPPDATA%\TaskbarProximityOpacity`. On normal exit the original taskbar window styles and opacity are restored. If the app is force-terminated, the next launch reads a recovery record and restores them. If you force-terminate it and will not launch it again, restart Windows Explorer before deleting the recovery data.

## Performance notes

The utility samples the cursor every 30 ms, but skips work when the cursor has not moved and no fade is running. It writes a new taskbar opacity only when the value changes. It creates no screen-sized overlay and performs no per-frame rendering. A borderless game may still cause opacity writes while the cursor moves through the fade range; the tray menu can pause updates and restore full opacity.

Windows taskbar window classes and layering are implementation details, so a future Windows update can change behavior. The app does not relocate taskbars or toggle Windows auto-hide.
