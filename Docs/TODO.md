# TODO

## Context:

See what was added and changed in the `CHANGELOG.md`

## What to add/change/remove/debug

- Fix engine inside the app dimensions and add engine auto resize with the editor
- Refactor GameObjects logic to facilitate the creation of new GameObjects in the scene

## Issues

- The entire engine occupies the entire screen of the app editor so it selects some objects even when i am clicking inside for example in the inspector.

## Additional notes

- The in-engine "Editor Mode" toggle that used to live in HelperSuite's
  right-side panel is now driven by `IEditorBridge.IsHostedByEditor`: enabled
  by default under Anvil, off in standalone `Engine.exe` (Space toggles).
