# Material component and water example

Select a mesh in Anvil, then choose **Add Component > Material**. Use **Standard**
for an opaque surface, or **Apply Water Example** for a teal water preset. For a
quick surface, add a Cube and flatten its Z scale; water ripples run on the XY
plane because the engine uses Z as up.

The component controls color, roughness, metallic, emission (0–8), and shadow
casting. Standard tints the imported albedo texture and overrides roughness and
metallic while retaining the mesh's normal, mask, and displacement maps. Water
controls opacity, wave scale, wave speed, and wave strength; its color sets the
water's body tint. Set speed to zero to freeze the ripples, or strength to zero
for a smooth surface. The example button resets tint, roughness, and water settings.

Water uses procedural animated normals, dielectric Fresnel, the scene's existing
environment reflection cubemap, and a highlight from the first enabled
directional light. It draws after opaque/forward geometry, before TAA and bloom,
with alpha blending, depth testing, and no depth writes. Water instances draw
from back to front by object origin. Intersecting transparent meshes and multiple
transparent surfaces inside one mesh can still show ordering artifacts.

This example animates shading without displacing vertices. It does not implement
refraction, shoreline foam, water physics, or water shadows. It works without
extra water textures and continues animating in Edit and Play modes.
The existing lighting baker still estimates diffuse bounce colors from the
source materials; component overrides are applied at render time.

Each mesh part gets an owned material instance. Editing one object cannot change
another object's material or its shared model asset. Disable or remove the
component to restore the original materials; Stop preserves the authored material.
The base-material inspector is disabled while the component is attached, so edit
the component's controls. Components save in `.obsc` files and copy independently
when duplicating objects. Older scenes continue loading without them.

The shader is `Engine/Content/Shaders/Forward/Water.fx`, built by `Content.mgcb`.
`WaterRenderModule` binds frame and surface parameters; `MaterialComponent`
supplies the settings. New shader options require registration in the component
enum and editor dropdown, material application, and a renderer pass.

Run `dotnet run --project Tests/Components/Components.csproj -- --graphics` to
check persistence, inspector edits, actual water pixels/animation/reflections,
opaque depth occlusion, Standard texture tint/roughness, restoration, and disposal.
The graphics checks use a hidden native window and require WindowsDX graphics.
