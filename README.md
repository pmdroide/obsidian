# Custom Monogame Engine

## Description
Original intent of the project was not to build a 3D game engine but to show how graphics work. Current Objective is to build a game engine on top of this project.

## Changes
- Main.cs is now Engine.cs
- .obj files are now imported with fbximporter to avoid content load error
- .NET 10
- Monogame version 3.8.4.1

Original Code https://github.com/Kosmonaut3d/DeferredEngine

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

## Controls:
- " ^ " / the key above TAB : debug console with suggestions (tab to autocomplete)
- Space: Go into editor mode 
  - R / T: Change transformation gizmos between translation and rotation
  - Del : Delete object
  - Insert : Copy object
- WASD : move the camera
- right mouse drag : rotate the camera
- F1 : Cycle through render targets (albedo, normals, depth etc.)

## How to modify the scene
How to manipulate the scene
- See the Main / MainLogic.cs for details. Manipulate and add scene objects in Initialize() and Update();

## IMPORTANT NOTES
Current target for WindowDX

For cross-platform you need to switch to desktopGL and you must change all shaders to vs_3_0 and ps_3_0, and also change SV_VERTEXID to a standard vertex input, because OpenGL's MojoShader translator doesn't support the ID semantic in that specific way for version 3.0.