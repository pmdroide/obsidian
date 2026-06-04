# TODO

## Context

See the section "Fix Anvil viewport resize — no black areas, no resize/startup crash, engine fills only its cell" in ```CHANGELOG.md```.

I can resize the window without any issues appearing but the engine window shows a white screen.
The anvil-bridge.log file shows a lot of issues that could be possibly related to the reason why the engine shows all white.

## Issues

- [FIXED] Engine window shows all white — `BloomFilter.Dispose()` disposed the *shared*
  `GraphicsDevice` (and the content-owned bloom `Effect`). `Dispose()` runs on every resolution
  change via `BloomFilter.UpdateResolution()`, so the first resize killed the device; every later
  `Texture2D`/`RenderTarget2D` creation and `SetRenderTargets` then NRE'd, leaving a white screen.
  Fixed by making `Dispose()` release only the bloom render targets it owns. See CHANGELOG.
- [FIXED] The anvil-bridge.log errors (`Texture2D..ctor` NRE in `BloomFilter.UpdateResolution`,
  `Monitor.Enter(null)` in `PlatformApplyRenderTargets`) were all downstream symptoms of the
  disposed device above.
- [FIXED] On startup the viewport rendered at MonoGame's stale 640×480 default (white/overexposed
  borders, overlay read `Res: 640 x 480`) and only snapped to the container size after the first
  manual resize. The embedded engine HWND is reparented/resized to the Anvil container before any
  `ClientSizeChanged` the engine observes, so nothing primed the reconcile at boot. Fixed by having
  `Engine.ApplyPendingResize` self-prime for the first ~30 Update ticks: if the actual
  `Window.ClientBounds` differ from the rendered resolution, it flags a resize so the viewport fits
  the container on startup with no manual resize. See CHANGELOG.

## Warnings on runtime
- [FIXED] `MonoGameHost.cs(232,54): warning CS8625` — the `CreateWindowEx` P/Invoke's `lpWindowName`
  parameter is now declared `string?`, so passing `null` no longer warns.