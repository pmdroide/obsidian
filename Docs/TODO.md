# TODO

## Context

See the ```CHANGELOG.md``` for more info.

## Issues

(none open)

## Resolved

- ~~Game input detection outside the viewport/engine window~~ — RMB-drag over the editor panels
  no longer moves the camera. `MonoGameHost` now forwards pointer-over-viewport state to the engine
  bridge so the engine ignores the global mouse state while the cursor is outside the viewport. See
  `CHANGELOG.md`.