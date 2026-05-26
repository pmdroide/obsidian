# TODO

## Context:

See what was added and changed in the `CHANGELOG.md`

## What to add/change/remove/debug

- Safely remove HelperSuite (legacy UI)
- Investigate deeper and fix the Add GameObject crashing issue
- Investigate deeper and fix the Inspector focus issue

## Issues

- Phase 1, new scene breaks the game (the game stops moving), also when saving a scene and then opening it, the textures were gone
- Phase 1, part 2, Inspector focus still sometimes opens and closes immediately, when selecting a object in the hierarchy or trying to edit the values.
- Phase 3, holding RMB and pressing WASD doesnt move the camera
- Phase 4, Translate, Rotate, Scale are binded and working but the Select tool is not behaving as expected

## Additional notes

- HelperSuite shows in the GUI the option to turn on the Editor mode so for the select tool to work it should be like this, the editor mode should be on by default but only when the game is running on the editor.
- In phase 1 after creating a new scene and then the game breaking, If I were to open a scene I saved It would bring back the game