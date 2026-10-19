# Current Issues

- Turning off CPU culling stops moved objects from updating.
- Material batching is off by default.
The final image is drawn twice per frame.
- Shader hot reload points at an old, hard-coded mgcb.exe path, so in practice it doesn't work.
- `Tests/Components --graphics` stops at the pause menu check "the document is filled in: settings rows, scene line, and the session card" (`PauseChecks.cs`): it expects 9 settings rows in the PersistenceTest pause menu. Not caused by the weather changes; the pause menu/MenuSettings files are unmodified.
- Raindrops on surfaces and the ground mist are not occluded: drops bead on surfaces under a roof and the mist fills rooms below its top (untick the material's Rain Drops for sheltered surfaces).
