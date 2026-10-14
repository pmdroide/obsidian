---
name: run-obsidian
description: Build, run, and drive the Obsidian engine and its Anvil editor on Windows. Use when asked to start or launch Anvil or the standalone game, click through the editor UI (Pipeline, Environment, Inspector panels), take a screenshot of the editor or game, build the solution, or run the component checks.
---

Obsidian is a Windows-only MonoGame engine (`Engine.exe`) with an Avalonia editor (`Anvil.exe`) that hosts it in-process. Drive either one with **`.claude/skills/run-obsidian/driver.ps1`** (Windows PowerShell 5.1). It launches the exe, finds and clicks UI Automation elements, clicks window pixels, scrolls, presses keys, takes window screenshots and quits. All paths are relative to the repo root.

## Prerequisites

Windows 10/11 with a DirectX 11 GPU and the .NET 10 SDK. The build restores `dotnet-mgcb` 3.8.4.1 into the NuGet cache, and the content build and render feature compiles use it from there. There is nothing to install by hand.

## Build

```powershell
dotnet build Engine.slnx /property:GenerateFullPaths=true "/consoleloggerparameters:NoSummary;ForceNoAlign"
```

To rebuild only the editor after an Anvil/XAML change:

```powershell
dotnet build Editor\Anvil\Anvil.csproj -v q "/consoleloggerparameters:NoSummary"
```

Quit Anvil/Engine before building (`driver.ps1 quit`). The driver runs the Debug builds in `Editor\Anvil\bin\Debug\net10.0-windows\` and `Engine\bin\Debug\net10.0-windows\`.

## Run: Anvil editor (agent path)

```powershell
$d = ".\.claude\skills\run-obsidian\driver.ps1"
& $d launch                                   # ~6 s; returns once the viewport toolbar exists
& $d click -Target Pipeline                   # toolbar button by UIA name (Environment, World, Pipeline...)
& $d shot -Out .claude\skills\run-obsidian\shots\pipeline.png
& $d clickxy -Target "1262,467"               # window-relative pixel = pixel in the screenshot
& $d wheel -Target "1320,600,15"              # scroll 15 notches down over the Inspector
& $d find -Target "^(Pipeline|Environment)$"  # list named elements + screen rects (slow: ~13 s)
& $d quit
```

- Read the PNG after each `shot`. To click something in it, pass its pixel coordinates to `clickxy`. They are window-relative, the same frame `shot` captures.
- `shots/` is git-ignored.
- In the default 1456 x 909 window, the Inspector panel is about x 1190-1440, y 385-720. The Render Pipeline's **+ New** button was at (1405,431) and its list started at y 467.

## Run: standalone game (agent path)

```powershell
& $d launch -App Engine                  # 3 s
& $d key -App Engine -Target Enter       # skip intro 1 (Obsidian)
Start-Sleep -Milliseconds 1500
& $d key -App Engine -Target Enter       # skip intro 2 (FMOD)
Start-Sleep 5
& $d shot -App Engine -Out .claude\skills\run-obsidian\shots\engine-menu.png   # MainMenu "PRESS ANY BUTTON"
& $d quit -App Engine
```

`key` takes any `System.Windows.Forms.Keys` name (`Enter`, `Escape`, `Space`, `W`, `F1`...). The game boots scene 0 of `Engine\Content\System\SceneList.json` (MainMenu) after the videos of `IntroVideos.json`.

## Run (human path)

`dotnet run --project Editor\Anvil\Anvil.csproj` or `dotnet run --project Engine\Engine.csproj` opens a window and blocks until it is closed. The driver launches the built exes instead, so the shell stays free.

## Direct invocation (no window)

Most engine and editor logic (bridge, components, view models, render features) is covered by the framework-free check app. It drives `EditorBridge` and Anvil view models directly:

```powershell
dotnet run --project Tests\Components\Components.csproj                       # ends "All component checks passed."
dotnet run --project Tests\Components\Components.csproj --no-build -- --graphics   # + GPU checks on a hidden window
```

The `--graphics` run currently stops at `PauseChecks` ("the document is filled in…"). That failure is known and unrelated (see CHANGELOG.md). Checks registered after it in `MaterialGraphicsChecks.Run` don't run, so to exercise a new graphics check, call it temporarily before `EnvironmentChecks.RunGraphics`.

## Gotchas

- **Inspector content is invisible to UI Automation.** Toolbar buttons, menus and hierarchy items have UIA names, but nothing inside the right-hand Inspector `ScrollViewer` does, not even with `AutomationProperties.Name` set (checked with the Render Pipeline panel). Click it by screenshot pixel with `clickxy`.
- **A button whose content is icon + text has no UIA name** unless it sets `AutomationProperties.Name`. The inner `Text` element is still found, and `click` uses the first exact name match. The Pipeline button now has a name.
- **Walking the UIA tree is slow, and every element shows up many times** (Avalonia exposes duplicates). `find` de-duplicates but takes about 13 s. `click` and `launch` use a name-filtered query, which takes about 1 s. Never poll `find` in a loop.
- **The Anvil window appears before the editor is usable.** `launch` waits for the `Pipeline` toolbar element, then 3 s more. The console reads "Anvil ready — waiting for engine bridge" while the engine boots.
- **`shot` copies the screen, not the window.** Toasts and overlapping windows end up in the image: a Discord notification appeared in one. The standalone Engine window's rect also includes about 8 px of Windows' invisible resize border, so the edges show what is behind it. Keep the window unobscured, and don't commit shots.
- **Timing the intro skips matters.** An extra Enter after the menu is up goes through "PRESS ANY BUTTON", and the next one activates **Play**, which loads GodRayTest. Two Enters about 1.5 s apart right after `launch` stop at the menu.
- **`CloseMainWindow` may not close Anvil.** One Anvil kept running after a close request. `quit` waits 5 s, then kills the process and says so. Check with `Get-Process Anvil` before `launch`: `launch` reuses a running instance.
- **Creating content from the UI writes to the source tree.** For example, Render Pipeline > + New writes `Engine\Content\RenderFeatures\*.fx`/`.xnb` and `Engine\Content\System\RenderFeatures.json`. Delete them from the UI (Delete Feature sends files to the Recycle Bin) and remove the leftover JSON and folder after a test.
- **Engine logs go to `%USERPROFILE%\Desktop\anvil-bridge.log`**, with `%LOCALAPPDATA%\Anvil\anvil-bridge.log` as the fallback, not to the console. Anvil's Console panel only shows messages the editor posts itself.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `launch` prints "Anvil is already running" with stale UI | An older instance survived. Run `& $d quit`, then `launch` again. |
| `click` throws "No element named '...'" for an Inspector button | The element isn't exposed (see Gotchas). Take a `shot` and use `clickxy`. |
| `launch` throws `Anvil toolbar not ready within 90 s` while Anvil looks fine | The process's main window handle was the engine's MonoGame window before it was reparented. The driver re-reads the handle on every poll; if it still happens, `quit` and `launch` again. |
| `launch` takes minutes | Something polls the full tree (`find`) in a loop. Readiness checks must use `Find-Exact`, a name-filtered query. |
| The `--graphics` run ends with `FAIL: the document is filled in: settings rows, scene line, and the session card` | A known `PauseChecks` failure, not yours. See Direct invocation. |
