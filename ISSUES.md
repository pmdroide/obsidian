# Current Issues

- Turning off CPU culling stops moved objects from updating.
- Material batching is off by default.
The final image is drawn twice per frame.
- Shader hot reload points at an old, hard-coded mgcb.exe path, so in practice it doesn't work.
- `Tests/Components --graphics` stops at the pause menu check "the document is filled in: settings rows, scene line, and the session card" (`PauseChecks.cs`): it expects 9 settings rows in the PersistenceTest pause menu. Not caused by the weather changes; the pause menu/MenuSettings files are unmodified.
- Raindrops on surfaces and the ground mist are not occluded: drops bead on surfaces under a roof and the mist fills rooms below its top (untick the material's Rain Drops for sheltered surfaces).
- Breakable glass shards have no damaged material, edge texture, sounds or particles yet, and falling shards don't collide with each other (`Docs/markdown/Breakable_Glass.md` > Limitations). 
- The REA MCP server (`rea-agents@6.7.0 mcp`) times out on connect in Claude Code (30 s), although `doctor` reports its registration as aligned; Ghidra isn't wired for it (`GHIDRA_INSTALL_DIR` unset; Ghidra is at `C:DevToolsghidra_12.1.4_PUBLIC`). (common issue across platforms, not a priority)
- The main view's near plane is hard-coded to 1 m (`Renderer.cs`, `CreatePerspectiveFieldOfView(camera.FieldOfView, aspect, 1, g_farplane)`): anything closer than 1 m to the camera is clipped, e.g. a bottle held up to a first-person camera or a close-up of small props. Lowering it needs checking the depth reconstruction and the passes that assume it.
- Bottle liquid (Liquid component) isn't drawn into shadow maps or Planar Reflection mirror images (like the glass around it), and containers on their side always use the +X side fill table, so non-round containers lying on another side fill a little off (`Docs/markdown/Bottle_Liquid.md` > Limits).
- The image skybox sun (Environment > Skybox > Sun glare) is only drawn on the visible sky: Planar Reflection mirrors (`PlanarSky` in `Forward.fx`) and reflection probe captures show the panorama's plain LDR sun. In scenes with thick volumetric fog in front of the sky (e.g. LookDev) the sun is mostly hidden behind it.
