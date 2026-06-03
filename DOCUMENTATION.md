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

## Structure

- Main file is Engine.cs

## How to modify the scene
How to manipulate the scene
- See the Main / MainLogic.cs for details. Manipulate and add scene objects in Initialize() and Update();

## Controls:
- " ^ " / the key above TAB : debug console with suggestions (tab to autocomplete)
- Space: Go into editor mode 
  - R / T: Change transformation gizmos between translation and rotation
  - Del : Delete object
  - Insert : Copy object
- WASD : move the camera
- right mouse drag : rotate the camera
- F1 : Cycle through render targets (albedo, normals, depth etc.)

## Render pipelines
- Deferred (Main)
- Forward

## Current target
- WindowsDX
- Monogame 3.8.4.1