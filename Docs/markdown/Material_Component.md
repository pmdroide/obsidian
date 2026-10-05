# Material component and water example

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
color and normal maps. Textures use the MonoGame content pipeline, so newly copied
images need a content build before they can load. Missing maps are logged and
leave the original map in place. Texture choices save in scenes and copy with objects.

**Shader** is a separate section with the existing shader choices, including
Water. The water preset and wave controls live there. These are registered engine
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
The existing lighting baker still estimates diffuse bounce colors from the
source materials; component overrides are applied at render time.

Each mesh part gets an owned material instance. Editing one object cannot change
another object's material or its shared model asset. Disable or remove the
component to restore the original materials; Stop preserves the authored material.
The original Material inspector's color picker, sliders, transparency checkbox,
and type selector are now the addable component's controls. Adding Material
starts with the object's existing settings. The selector retains Basic, Hologram,
ProjectHologram, Emissive, SubsurfaceScattering, and ForwardShaded, and adds Water.
There is one Material section, shown only while the component is attached.
Components save in `.obsc` files and copy independently
when duplicating objects. Older scenes continue loading without them.

The shader is `Engine/Content/Shaders/Forward/Water.fx`, built by `Content.mgcb`.
`WaterRenderModule` binds frame and surface parameters; `MaterialComponent`
supplies the settings. New shader options require registration in the component
material type enum and editor dropdown, material application, and a renderer pass.

Run `dotnet run --project Tests/Components/Components.csproj -- --graphics` to
check persistence, inspector edits, actual water pixels/animation/reflections,
opaque depth occlusion, Standard texture tint/roughness, restoration, and disposal.
The graphics checks use a hidden native window and require WindowsDX graphics.
