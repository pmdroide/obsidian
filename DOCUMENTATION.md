# Project version: 1.0

## Features
- Easy to use viewer, with lots of GUI options
- G-buffer creation with support for physically based materials (albedo, normal, roughness, metallic, mask)
- Cook-Torrance specular shading and Oren-Nayar diffuse shading for point lights
- Light and mesh frustum culling
- Deferred point lights, directional lights and environment mapping
- Forward rendering to render transparency
- Soft shadows
- Dynamically updating point light shadows depending on scene changes
- Temporal anti-aliasing
- HDR Bloom
- Screen space ambient occlusion (HBAO)
- Screen space reflections
- Linear HDR pipeline.
- EXPERIMENTAL: screen space emissive materials (not updated to work right now)
- Froxel Volumetric Lightning/Fog
- .obj files are imported with fbximporter to avoid content load error
- Vista, a custom UI that uses XML for structure and CSS for styling
- Anvil Game Editor (AvaloniaUI)

## Current Issues

- Imported models don't save. Scene files only recognise the built-in models listed on Assets, so objects using a model imported in the editor are skipped on save and on load.
- Some hotkeys ignore Anvil. Input.WasKeyPressed doesn't see keys forwarded from Anvil, so in-engine hotkeys like Delete, Insert and Ctrl+C only work when the engine window itself has focus.
- The Delete key cleans up less than the hierarchy's delete. It skips stopping the object's components and removing its physics body.
- Debug-object picking is broken. The selection loop for debug objects reads from the wrong list (directional lights).

Other rough spots:
- Turning off CPU culling stops moved objects from updating.
- Material batching is off by default.
The final image is drawn twice per frame.
- Shader hot reload points at an old, hard-coded mgcb.exe path, so in practice it doesn't work.

## Controls:
- " ^ " / the key above TAB : debug console with suggestions (tab to autocomplete)
- Space: Go into editor mode 
  - R / T: Change transformation gizmos between translation and rotation
  - Del : Delete object
  - Insert : Copy object
- WASD : move the camera
- right mouse drag : rotate the camera
- F1 : Cycle through render targets (albedo, normals, depth etc.)
- X/M : Audio test

## Current target
- WindowsDX
- Monogame 3.8.4.1

## TROUBLESHOOT

Sometimes the game might fail due to old packages or assets that were in old build of the game, to fix this use the [clean.ps1](clean.ps1) file to cleanup the old build.

```
.\clean.ps1          # delete
.\clean.ps1 -WhatIf  # just list what would go
```