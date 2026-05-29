# TODO

## Context:

See the section "Runtime asset import pipeline (drop FBX → drag into scene)" in ```CHANGELOG.md```

It registers correctly when dragging new fbx models into the meshes folder inside the editor, but it doesnt immediately update and show the model in the meshes folder in realtime. Also I cant drag models from the meshes folder into the scene hierarchy.

anvil-bridge.log:

[23:47:01.703] --- EditorBridge static init pid=24560 ---
[23:47:01.709] MonoGameHost: constructing Engine.Engine
[23:47:02.069] MonoGameHost: Engine.Engine constructed
[23:47:02.070] EditorBridge.IsHostedByEditor = True
[23:47:05.155] Bridge bound — scene=True, editor=True, assets=True, models=8
[23:50:07.274] EnqueueImportModel path='C:\Users\mano3\Downloads\horizontal-fuel-tank\source\Tank.fbx'
[23:50:07.280] AssetImporter: src='C:/Dev/GitHub/obsidian/Engine/Content', built='C:/Dev/GitHub/obsidian/Engine/Content/bin/Windows', exe='C:/Dev/GitHub/obsidian/Editor/Anvil/bin/Debug/net10.0-windows/Content', mgcb='dotnet exec C:\Users\mano3\.nuget\packages\dotnet-mgcb\3.8.4.1\tools\net8.0\any\mgcb.dll '
[23:50:07.284] AssetImporter: copied model -> 'C:/Dev/GitHub/obsidian/Engine/Content\Art\Models\Tank\Tank.fbx'
[23:50:07.286] AssetImporter: invoking mgcb -> 'dotnet exec C:\Users\mano3\.nuget\packages\dotnet-mgcb\3.8.4.1\tools\net8.0\any\mgcb.dll /@:Content.mgcb /workingDir:C:/Dev/GitHub/obsidian/Engine/Content'
[23:50:11.628] mgcb stdout: Build started 28/05/2026 23:50:07

Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Default/daft_helmets.obj
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Default/dragon_lowpoly.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Default/dragon_normal.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Default/dragon_uv_smooth.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Default/isosphere.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Default/skull.obj
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Default/sphere.x
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Editor/arrow.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Editor/arrowRound.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Editor/icon_decal.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Editor/icon_envmap.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Editor/icon_light.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Editor/texStrip.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Human/human.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/plane.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Test/cube.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Test/squarebricks-ambientocclusion.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Test/squarebricks-depth.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Test/squarebricks-diffuse.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Test/squarebricks-normal.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Test/tubes.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Tiger/Tiger.fbx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Truck/truck_skeleton.FBX
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Truck/truck_skeleton_albedo.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Truck/truck_skeleton_Metalness.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Truck/truck_skeleton_normal.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/Truck/truck_skeleton_roughness.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Fonts/defaultfont.spritefont
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Fonts/monospace.spritefont
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Graphical User Interface/colorpickerbig.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Graphical User Interface/colorpickersmall.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/BloomFilter/Bloom.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Deferred/DeferredClear.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Deferred/DeferredCompose.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Deferred/DeferredDecal.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Deferred/DeferredDirectionalLight.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Deferred/DeferredEnvironmentMap.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Deferred/DeferredPointLight.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Deferred/Froxel.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Editor/BillboardEffect.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Editor/IdRender.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Editor/LineEffect.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Emissive/EmissiveDraw.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Forward/Forward.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/GBufferSetup/ClearGBuffer.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/GBufferSetup/Gbuffer.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Hologram/HologramEffect.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/noise.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/noise_blur.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/PostProcessing/ColorGrading.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/PostProcessing/lut.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/PostProcessing/PostProcessing.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/ScreenSpace/GaussianBlur.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/ScreenSpace/ReconstructDepth.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/ScreenSpace/ScreenSpaceAO.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/ScreenSpace/ScreenSpaceReflections.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Shadow/ShadowMap.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Shadow/testShadow.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/SignedDistanceFields/sampleTexture.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/SignedDistanceFields/volumeProjection.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/SubsurfaceScattering/SubsurfaceScattering.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/TemporalAntiAliasing/TemporalAntiAliasing.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Shaders/Test/TexFilter.fx
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/Sponza.obj
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_thorn_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_plant.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_round.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/background.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_blue_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/spnza_bricks_a_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_arch_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_ceiling_a_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_a_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_floor_a_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_c_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_details_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_b_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_flagpole_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_fabric_green_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_fabric_blue_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_fabric_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_green_diff.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/chain_texture.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_hanging.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_dif.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/lion.jpg
	Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_roof_diff.jpg
C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/background.jpg
C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/background.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/background_1.jpg
C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/background_ddn.jpg
C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/background_ddn.tif
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/background_ddn_1.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/chain_texture.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/chain_texture_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/chain_texture_mask.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/lion.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/lion_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/lion2_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/spnza_bricks_a_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/spnza_bricks_a_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/spnza_bricks_a_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_arch_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_arch_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_arch_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_ceiling_a_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_ceiling_a_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_a_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_a_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_a_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_b_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_b_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_b_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_c_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_c_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_column_c_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_blue_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_blue_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_green_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_green_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_metallic.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_curtain_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_details_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_details_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_fabric_blue_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_fabric_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_fabric_green_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_fabric_metallic.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_fabric_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_flagpole_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_flagpole_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_floor_a_ddn.png
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_floor_a_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_floor_a_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_roof_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_thorn_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_thorn_diff.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_thorn_mask.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/sponza_thorn_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_dif.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_hanging.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_plant.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_plant_mask.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_plant_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_round.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_round_ddn.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/textures/vase_round_spec.jpg
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Art/sky.dds
C:/Dev/GitHub/obsidian/Engine/Content/Art/Models/Tank/Tank.fbx
	C:/Dev/GitHub/obsidian/Engine/Content/Art/Models/Tank/Gas Tank Horizontal Textures/Gas Tank Horizontal_Gas Tank _BaseColor.png
C:/Dev/GitHub/obsidian/Engine/Content/Art/Models/Tank/Tank.fbx: error: The source file 'C:/Dev/GitHub/obsidian/Engine/Content/Art/Models/Tank/Gas Tank Horizontal Textures/Gas Tank Horizontal_Gas Tank _BaseColor.png' does not exist!. 
Skipping C:/Dev/GitHub/obsidian/Engine/Content/Sponza/sponza_sdf.sdff

Build 127 succeeded, 1 failed.

Time elapsed 00:00:04.20.

[23:50:11.647] ImportModel threw: System.InvalidOperationException: mgcb exited with code 1. See log.
   at Engine.Recources.AssetImporter.RunMgcbBuild() in C:\Dev\GitHub\obsidian\Engine\Recources\AssetImporter.cs:line 295
   at Engine.Recources.AssetImporter.ImportFbx(String sourceModelPath, String requestedKey, List`1& importedTextures) in C:\Dev\GitHub\obsidian\Engine\Recources\AssetImporter.cs:line 144
   at Engine.Editor.EditorBridge.<>c__DisplayClass71_0.<EnqueueImportModel>b__0() in C:\Dev\GitHub\obsidian\Engine\Editor\EditorBridge.cs:line 351


## What to add/change/remove/debug:

- make it when adding a fbx model, it doesnt need textures and loads default error texture (Path: Content/Art/error.png)
- Add the function to drag the fbx model into the scene hierarchy, and loads the model with the default error.png
- If a model is not displaying correctly, make it not crash the game instead displays error model (Path: Content/Art/error.fbx)

## Issues

- Sometimes it loads correctly the 8 default models tha are on content.mgcb other times it doesnt, it register in the meshes folder but they are not draggable to the scene hierarchy.

- When i start the editor again after having dragged the fbx model into the scene, it gives a error for searching the textures

- Check log for issues
