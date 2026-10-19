# Material component, water and wet surfaces

Select a mesh in Anvil, then choose **Add Component > Material**. Use **Basic**
for an opaque surface, or **Apply Water Example** for a teal water preset. For a
quick surface, add a Cube and flatten its Z scale; water ripples run on the XY
plane because the engine uses Z as up.

The component controls color, roughness, metallic, emission (0–8), and shadow
casting. Basic tints the imported albedo texture and overrides roughness and
metallic while retaining the mesh's normal, mask, and displacement maps. Water
controls opacity, wave scale, wave speed, wave height, and ripple strength; its
color sets the water's body tint. **Wave Height** is the crest-to-trough height of
the swell that moves the surface, in metres (0 keeps it flat). **Ripple Strength**
adds finer waves to the shading only. Set speed to zero to freeze the waves. The
example button resets tint, roughness, and water settings, and gives the object the
**Water** role so Buoyancy bodies float on it.

**Textures** provides Base Color, Normal, Roughness, Metallic, Mask, and
Displacement slots. Click a slot to choose an image inside `Engine/Content`, or
drag a texture from Assets. **Clear** removes that map; **Reset** restores the
model's original map and the component's default surface behavior. New components
inherit model maps. Assigned roughness and metallic textures take precedence over
their sliders; they work independently of base color and normal maps. Mask is an
alpha cutout map, and displacement uses the existing parallax shader with base
color and normal maps (Wet Surface uses it as puddle height instead). **Tiling**
(U, V) repeats every map across the mesh UVs, so a large flattened Cube can carry a
ground texture at its real-world scale; 1 keeps the model's UVs. Textures use the MonoGame content pipeline, so newly copied
images need a content build before they can load. Missing maps are logged and
leave the original map in place. Texture choices save in scenes and copy with objects.

## Material assets (.mat)

A material can live in the Assets folder and be shared by many gameobjects, like a Unity material. A material
asset is a `.mat` file in Content (by default `Content/Materials`) holding everything the Material component edits:
colour, roughness, metallic, emission, opacity, transparency, shadows, tiling, the shader and its water / wet
surface settings, and the six texture slots (Base Color, Normal, Roughness, Metallic, Mask, Displacement).

- **Create one:** right-click in Assets > **Create Material** (in that folder), or **GameObject > Create Material
  from Selection** / Inspector > Material > **Save as Material Asset**. Saving from a gameobject keeps the textures
  it shows: slots that follow the model are filled with the model's own texture files, and maps the model doesn't
  have become None, so the material looks the same on any other mesh. A new blank material keeps each model's own
  maps ("From model") until you pick textures.
- **Edit it:** click it in Assets. The Inspector shows the same controls as the Material component; changes save to
  the file and every gameobject using it updates at once. Editing the file outside Anvil also works (it is picked up
  within half a second).
- **Assign it:** drag it from Assets onto a gameobject in the Hierarchy, double-click it with a gameobject
  selected, use its context menu or the Inspector's **Assign to Selected GameObject**, or pick/drop it on the
  Material component's **Asset** slot. A gameobject without a Material component gets one.
- **Linked components:** the Material section shows the asset's name. Its settings edit the asset itself (every
  gameobject using it follows); **Edit Asset** opens the asset, **Unlink** keeps the values as the gameobject's own.
  The scene saves the link and a copy of the values, used when the file is missing.
- **Scripts:** `MaterialAsset.Assign(entity, "Brick")` (or `"Materials/Brick.mat"`) links a gameobject;
  `new MaterialComponent { MaterialPath = "Brick" }` added with `AddComponent` takes the asset's surface;
  `MaterialAsset.Load("Brick")`. Changing a linked component from a script changes only that gameobject until the
  asset is edited again.
- Builds ship every `.mat` file. Edits made during Play are saved to the file (Stop doesn't undo them).

**Shader** is a separate section with the existing shader choices, including
Water, WetSurface and Foliage. The water preset, wave controls and wet surface controls live there. These are registered engine
shaders; arbitrary `.fx` files need renderer integration before appearing as choices.

Water moves its mesh with four Gerstner swell waves: the surface rises and falls,
and crests sharpen and lean as points move sideways. Every water mesh part is
subdivided once (up to about 131k triangles; a two-triangle plane becomes a
256x256 grid), so even a flattened Cube deforms. Swell waves shorter than three
grid cells fade out on very large meshes instead of aliasing. Eight shorter ripple
waves only bend the normal. Squeezed crests and high crests gather foam, and
sunlight shining through thin crests tints them turquoise.

`Engine/Physics/WaterWaves.cs` repeats the swell formula on the CPU so buoyancy
follows the visible surface. Change both files together.

Water uses dielectric Fresnel, the scene's existing
environment reflection cubemap, and a highlight from the first enabled
directional light. It draws after opaque/forward geometry, before TAA and bloom,
with alpha blending, depth testing, and no depth writes. Water instances draw
from back to front by object origin. Intersecting transparent meshes and multiple
transparent surfaces inside one mesh can still show ordering artifacts.

Water does not implement refraction or water shadows. Selection outlines and
picking use the undeformed mesh. It works without extra water textures and keeps
animating in Edit and Play modes.
Bodies falling into Water-role water splash, and rain rings the surface: see
[Water.md](Water.md).
The existing lighting baker still estimates diffuse bounce colors from the
source materials; component overrides are applied at render time.

Each mesh part gets an owned material instance. Editing one object cannot change
another object's material or its shared model asset. Disable or remove the
component to restore the original materials; Stop preserves the authored material.
The original Material inspector's color picker, sliders, transparency checkbox,
and type selector are now the addable component's controls. Adding Material
starts with the object's existing settings. The selector retains Basic, Hologram,
ProjectHologram, Emissive, SubsurfaceScattering, and ForwardShaded, and adds Water
and WetSurface.
There is one Material section, shown only while the component is attached.
Components save in `.obsc` files and copy independently
when duplicating objects. Older scenes continue loading without them.

The shader is `Engine/Content/Shaders/Forward/Water.fx`, built by `Content.mgcb`.
`WaterRenderModule` binds frame and surface parameters; `MaterialComponent`
supplies the settings. New shader options require registration in the component
material type enum and editor dropdown, material application, and a renderer pass.

## Foliage (leaves)

**Foliage** (Shader list) is for leaf cards. It is alpha-cut through the **Mask** texture, like any masked
material, and it casts cut-out shadows from the sun too. It also writes material type 8 to the G-buffer, so
the sun shines through the back of the leaves (`FoliageTransmission` in
`Shaders/Deferred/DeferredDirectionalLight.fx`). With a **Foliage Wind** component on the gameobject, Foliage
parts flutter while the rest of the model only bends and sways. The built-in Tree model's leaves use it. See
[Foliage_Wind.md](Foliage_Wind.md).

## Rain drops

**Rain Drops** (under Cast Shadows, on by default, hidden for WetSurface) lets the scene's rain bead on
the material and run down it, with a wet sheen. Untick it for surfaces under a roof, since the rain is
not occluded. See [Weather.md](Weather.md#raindrops-on-surfaces). It is saved with the component and
in material assets (`RainDrops`).

## Wet surfaces

**WetSurface** (Shader list) turns a ground material such as bricks, cobblestone or
stone/dirt into a rain-soaked one, like a road after a shower. It works with any
Base Color / Normal / Roughness maps; **Apply Wet Floor Example** sets it up fully
soaked. The look is written into the G-buffer, so it lights like a Basic material:

- **Damp:** the surface darkens like wet stone and turns glossier; cavities (dark
  parts of the Base Color, or the low parts of the Displacement map) soak first.
- **Puddles:** on faces that look up, water fills the low parts of a world-space
  noise field and the texture's cavities. Puddles are flat, nearly mirror-smooth
  water with a damp rim. Placement is in world space and does not repeat with Tiling.
- **Reflections:** puddles reflect through screen-space reflections and the
  environment cubemap. Wet pixels use a view-dependent Fresnel: seen from above they
  show the dark ground under the water, at grazing angles they mirror the sky and
  surroundings.
- **Rain:** with **Wet in Rain** on, the scene's weather soaks the surface beyond its
  own **Wetness**, fills its puddles and drops ripples into them. Rain wets surfaces in
  about 8 s and they dry in about 40 s after it stops; snow only dampens. A newly
  loaded scene starts as wet as its weather.

| Field | Meaning |
| --- | --- |
| Wetness (dry weather) | How wet it is without rain, 0..1. Puddles appear above 0.5. |
| Puddle Amount | How much of the upward faces puddles cover when soaked, 0..1. |
| Puddle Size (m) | Typical puddle size in metres. |
| Wet in Rain | Follow the scene weather (see above). |

For puddles that follow the texture's grooves, set the Displacement slot to a height
map (white = high). It is then used only as puddle height, without parallax. The
WeatherTest sample uses Poly Haven (CC0) ground sets in
`Engine/Content/GameObjects/Textures/Ground/` (`*_BaseColor`, `*_Normal`,
`*_Roughness`, `*_Height`): RockyDirt on the ground, Bricks on the street and
Cobblestone on the plaza.

The G-buffer shader (`Shaders/GBufferSetup/Gbuffer.fx`, `ApplyWetSurface`) does the
damp, puddle and ripple work; `GBufferRenderModule` passes the material settings
and the weather (`SurfaceWetness` in `Engine/Logic/Weather.cs`, updated by the
renderer). Wet pixels write material type 7 into the G-buffer (the normal target's
blue channel), which `Shaders/Deferred/DeferredEnvironmentMap.fx` reads to switch to the
view-dependent Fresnel. (The albedo alpha now holds the material's **Ambient Occlusion**
map, a seventh texture slot that darkens only indirect light; see
[Source2_Look.md](Source2_Look.md).) Limits: puddles do not raise the surface or affect physics, and very
close puddles can look dark when the scene's environment probe sees dark
surroundings in that direction and screen-space reflections miss.

Run `dotnet run --project Tests/Components/Components.csproj -- --graphics` to
check persistence, inspector edits, actual water pixels/animation/reflections,
opaque depth occlusion, Standard texture tint/roughness, wet surface G-buffer output
and tiling (`Tests/Components/WetSurfaceChecks.cs`), restoration, and disposal.
The graphics checks use a hidden native window and require WindowsDX graphics.
