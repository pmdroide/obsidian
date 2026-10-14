# Render Features: Your Own Shader Passes in the Render Pipeline

The engine's built-in passes (G-buffer, lighting, fog, water, TAA, bloom, post processing...) are fixed. A **render feature** is your own full-screen shader that the renderer draws *between* them, at an **injection point** you choose. It works like a URP Renderer Feature in Unity. Use it for outlines, tints, scanlines, fog layers, stylised lighting, pixelation, screen effects, and anything else that reads the image and writes it back.

- Features are project-wide. They are saved in `Content/System/RenderFeatures.json`, in run order, and ship with the built game.
- Each feature's shader is `Content/RenderFeatures/<Name>.fx`. The editor compiles it into `Content/RenderFeatures/<Name>.xnb` and recompiles it within a second or two of every save.
- Code: `Engine/Renderer/Features/` (`RenderFeature`, `RenderFeatures`, `RenderFeatureCompiler`, `RenderFeatureShaderTemplate`) and `Engine/Renderer/RenderModules/RenderFeatureRenderModule.cs`.

## The Render Pipeline panel

Open it with the **Pipeline** button in the viewport toolbar (next to Environment and World), or **Window > Render Pipeline**. It replaces the Inspector's content.

The list shows the whole pipeline in draw order:

- **Built-in passes** have a green dot when on, and are greyed out when switched off in the render settings (Inspector > Post FX). Select one to read what it does.
- **Injection points** are the blue `AFTER ...` headers. Render features run there.
- **Your features** sit under their injection point. The checkbox enables or disables a feature. The dot shows its compile status: green when compiled, amber while compiling, red on an error.

To add a feature, pick an injection point in the box at the top and press **+ New**. The engine writes a shader from the template, saves the list and compiles it. The template is a tint and vignette, so you see it working right away. Select the feature to edit it:

| Field | What it does |
|---|---|
| Up / Down | Moves the feature one row. It passes the neighbouring feature, or crosses the built-in passes into the previous or next injection point. |
| Name | The feature's name. Scripts find it by this name. |
| Enabled | Off: the feature is skipped. |
| Show in edit mode | Off: the feature only draws in Play and in the built game. |
| Injection point | Where it runs (see below). |
| Blend | How the output lands on the image (see below). |
| Intensity, Color, Params | Values handed to the shader as `Intensity` (float), `Color` (float4) and `Params` (float4, free for your own use). |
| Technique | Which technique of the .fx to run. Leave it empty for the first one. Every pass of the technique is drawn, in order. |
| Edit Shader | Opens the .fx in Visual Studio Code, or Notepad if VS Code isn't installed. Save the file to recompile it. |
| Recompile | Compiles the shader again now. |
| Status | Compiled, Compiling..., Error or Not compiled. Compiler errors are listed below it with file, line and column, and are also logged to the console. |
| Delete Feature | Removes the feature and sends its shader to the Recycle Bin. |

## Injection points

| Point | Runs after | Runs before | Image |
|---|---|---|---|
| **After Opaques** | Deferred compose (the lit opaque scene and fog) | Forward, water, particles, weather | linear HDR |
| **After Transparents** | Water, particles and weather | Temporal AA and bloom | linear HDR |
| **After Bloom** | TAA, bloom, auto exposure | Tonemapping and colour grading | linear HDR |
| **After Post Processing** | Tonemapping and colour grading | Editor gizmos, game UI and debug stats | final LDR (0 to 1) |

Some guidelines:

- Use **After Opaques** for effects that transparent objects should cover, such as an outline from the depth buffer.
- Use **After Transparents** for effects on the whole lit scene before anti-aliasing. TAA smooths their edges.
- Use **After Bloom** for HDR effects that should still be tonemapped, such as a colour tint in scene light units.
- Use **After Post Processing** for screen effects on the final picture: scanlines, pixelation, a vignette, a damage flash. Game menus and HUDs stay on top of it.

Features at one injection point run in list order. Each one reads the result of the previous one.

## Blend modes

| Blend | Result |
|---|---|
| Replace | The shader's output becomes the image. Read `ScreenTexture` to keep what was there. |
| Alpha Blend | The output is drawn over the image with its alpha. |
| Additive | The output (times its alpha) is added to the image. |
| Multiply | The image is multiplied by the output. |

Every blend mode changes the colour only. The image's alpha is kept.

## Writing the shader

Start from the template that **+ New** writes. It contains the vertex shader, the samplers and two helpers: `WorldPosition(uv, depth)` and `DecodeNormal(encoded)`. A feature is a MonoGame effect drawn as one full-screen triangle (`float2 Position : POSITION0`, clip space). The UV is `Position * float2(0.5, -0.5) + 0.5`.

The engine sets these parameters if the shader declares them. Names are case sensitive. Parameters the shader doesn't use are compiled out and skipped.

| Parameter | Type | Value |
|---|---|---|
| `ScreenTexture` | `Texture2D` | The image so far (HDR or LDR, see the table above) |
| `DepthMap` | `Texture2D` | Linear depth: view distance along the camera axis / `FarClip`. The sky is 1. |
| `NormalMap` | `Texture2D` | G-buffer normals: `.rg` is the sphere-map encoded view-space normal (`DecodeNormal`), `.a` is roughness |
| `AlbedoMap` | `Texture2D` | G-buffer base colour |
| `Resolution`, `InverseResolution` | `float2` | Target size in pixels, and 1 / size |
| `Time`, `DeltaTime` | `float` | Game time in seconds (wraps every hour). `DeltaTime` is 0 while the game is paused. |
| `Intensity` | `float` | The feature's Intensity |
| `Color` | `float4` | The feature's Color (RGBA, 0 to 1) |
| `Params` | `float4` | The feature's Params |
| `CameraPosition` | `float3` | World position of the camera (Z is up) |
| `FarClip` | `float` | Camera far plane |
| `View`, `Projection`, `ViewProjection`, `InverseViewProjection` | `float4x4` | Camera matrices of this frame. `ViewProjection` and `InverseViewProjection` include the TAA jitter, like the G-buffer. |

Declare each parameter with the type in this table. A parameter of the wrong kind, such as a texture named `Time`, stops the feature with "Draw failed: ...". It draws again after its next compile.

You can `#include` the engine's shader helpers with a relative path, for example `#include "../Shaders/Common/helper.fx"`.

### Example: an outline from depth

```hlsl
float4 MainPS(VertexOutput input) : SV_TARGET
{
    float4 color = ScreenTexture.Sample(LinearSampler, input.TexCoord);
    float2 px = InverseResolution * max(Params.x, 1);
    float d = DepthMap.Sample(PointSampler, input.TexCoord).r;
    float edge = 0;
    edge += abs(d - DepthMap.Sample(PointSampler, input.TexCoord + float2(px.x, 0)).r);
    edge += abs(d - DepthMap.Sample(PointSampler, input.TexCoord + float2(0, px.y)).r);
    float outline = saturate(edge * FarClip * 0.5f) * Intensity;
    return float4(lerp(color.rgb, Color.rgb, outline), 1);
}
```

Put it at After Opaques with Blend Replace, Color black and Params.x = 1 (the line width in pixels).

## Compiling and hot reload

- The editor compiles a shader with the same `mgcb` the content build uses (`RenderFeatureCompiler`). It builds that one file into a temp folder and copies the `.xnb` next to the `.fx`. A failed compile never replaces the last good `.xnb`, so the previous version keeps running while you fix the error.
- The shaders are **not** entries in `Content.mgcb`. A feature shader with an error can't break the engine's content build.
- Every 30 frames the engine checks the `.fx` files' write times and recompiles the ones that changed. When the engine starts, it loads each compiled `.xnb`. If the `.fx` is newer, it loads the old `.xnb` first and compiles the new one in the background.
- A built game has no `.fx` and no mgcb. It loads the `.xnb` that `Engine.csproj` copies next to the executable (`Content\RenderFeatures\**\*.xnb` and `Content\System\RenderFeatures.json`). Before you build, check that every feature shows Compiled.
- Edit > Project Settings > Delete Project removes `RenderFeatures.json` and the `RenderFeatures` folder.

## From a script

Scripts can change a feature during Play. For example, fade a damage flash:

```csharp
using Engine.Renderer.Features;

RenderFeature flash = RenderFeatures.Find("Damage Flash");
flash.Enabled = true;
Tween(1f, 0f, 0.4f, v => flash.Intensity = v);
```

Script changes are not saved. Stop puts every feature back to its saved values (`RenderFeatures.RestoreSaved`). `Intensity`, `Color`, `Params`, `Enabled`, `Blend`, `ShowInEditMode` and `Event` take effect on the next frame. Change `Shader` and `Technique` from the editor instead.

## Checks

`Tests/Components/RenderFeatureChecks.cs`:

- `Run` covers the pipeline listing, save/load, moving features between injection points, Stop restoring script changes, the bridge and Anvil's panel. It runs without a GPU and doesn't compile anything.
- `RunGraphics` (with `--graphics`) compiles the template with mgcb and draws it. It then checks every blend mode against a known image, chaining, Show in edit mode, a compile error keeping the last good shader, hot reload after a save, a draw failure, and loading at startup.
