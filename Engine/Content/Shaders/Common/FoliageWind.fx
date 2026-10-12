////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  Foliage wind (Foliage Wind component)
//
//  Shared by the G-buffer, the shadow maps and the editor id/outline passes, so shadows and the selection
//  outline follow the moving leaves. Include it after the effect's `World` matrix. Engine side:
//  Renderer/Helper/FoliageWind.cs (parameters) and Components/FoliageWindComponent.cs (per-tree settings).
//
//  Three layers after Crytek's vegetation bending (GPU Gems 3, ch. 16), computed from the vertex alone so
//  any tree model works:
//  1. Trunk bend - the whole tree leans with the wind and sways with gusts that roll across the scene.
//     Bending grows with height^2 and keeps each vertex's distance to the trunk base, so the crown arcs
//     over instead of stretching.
//  2. Branch sway - limbs bob and swing, more towards their ends (distance from the trunk axis); the phase
//     comes from the position so waves run through the crown.
//  3. Leaf flutter (Foliage material only) - each leaf card flaps along its normal and tilts its normal so
//     the light shimmers. It fades to 0 at the card's stem (texture v = 1). floor(u) is a per-card id
//     (whole-number U offsets, see GameObjects/Tree/Source/generate_tree.py); both sides of a card move
//     the same way.
////////////////////////////////////////////////////////////////////////////////////////////////////////////

bool WindEnabled = false;
// x: tree height in world metres, y: trunk bend, z: branch sway, w: leaf flutter
float4 WindParams0 = float4(8, 1, 1, 1);
// xy: direction the wind blows towards (world, unit length), z: strength (0 calm, ~0.7 breeze, 3 storm),
// w: per-tree phase (cycles)
float4 WindParams1 = float4(1, 0, 0.7, 0);
// The tree's wind clock in seconds; it runs faster in stronger wind
float WindTime = 0;
// 1 when the current material flutters (Foliage material type)
float WindLeaf = 0;

// Smoothed triangle wave, period 1, range 0..1: cheaper than sin and with natural-looking pauses
float4 WindWave(float4 x)
{
    float4 t = abs(frac(x + 0.5f) * 2 - 1);
    return t * t * (3 - 2 * t);
}

// worldPos: the vertex after World. worldNormal: unit world normal. uv: mesh texture coordinates (before
// tiling); pass float2(0, 1) when the pass has none (no flutter). origin: the tree's base (World translation).
// Returns the moved world position; tilt receives a world-space vector to add to the normal (leaf shimmer).
float3 ApplyFoliageWind(float3 worldPos, float3 worldNormal, float2 uv, float3 origin, out float3 tilt)
{
    tilt = float3(0, 0, 0);

    float height = max(WindParams0.x, 0.1f);
    float2 dir = WindParams1.xy;
    float strength = WindParams1.z;
    float phase = WindParams1.w;
    float t = WindTime;

    float3 rel = worldPos - origin;
    float h = saturate(rel.z / height);

    // Gusts travel downwind, so trees further along lag behind; z and w are the tree's own sway
    float travel = dot(origin.xy, dir) * 0.08f;
    float4 waves = WindWave(float4(t * 0.11f - travel, t * 0.27f - travel + 0.3f,
                                   t * 0.53f + phase, t * 0.37f + phase * 1.7f));
    float gust = waves.x * 0.6f + waves.y * 0.4f;

    float3 offset = float3(0, 0, 0);

    // 2. Branch sway: grows towards the ends of the limbs and up the crown
    float radial = length(rel.xy);
    float branchAtten = saturate(radial / (height * 0.4f)) * smoothstep(0.15f, 0.55f, h);
    branchAtten *= sqrt(branchAtten);
    float branchPhase = dot(rel, float3(0.37f, 0.29f, 0.23f)) + phase;
    float4 bw = WindWave(float4(t * 0.71f + branchPhase, t * 1.13f + branchPhase * 1.31f,
                                t * 1.7f + branchPhase * 0.7f, t * 0.47f + branchPhase * 1.9f)) * 2 - 1;
    float branchWave = bw.x * 0.5f + bw.y * 0.35f + bw.z * 0.15f;
    float branchAmp = strength * WindParams0.z * (0.4f + 0.6f * gust) * 0.015f * height * branchAtten;
    offset += float3(dir * (0.5f + 0.6f * bw.w), 0) * branchAmp + float3(0, 0, branchWave) * branchAmp;

    // 3. Leaf flutter
    float flutter = WindParams0.w * WindLeaf;
    [branch]
    if (flutter > 0)
    {
        // The two faces of a card have opposite normals: pick one so both move together
        float3 cardNormal = dot(worldNormal, float3(0.577f, 0.577f, 0.577f)) >= 0 ? worldNormal : -worldNormal;
        float leafPhase = frac(floor(uv.x) * 0.618034f) + dot(cardNormal, float3(0.9f, 1.3f, 0.7f)) * 0.5f
                          + dot(rel, float3(0.41f, 0.53f, 0.31f));
        float edge = saturate(1 - uv.y);
        float4 lw = WindWave(float4(t * 2.3f + leafPhase, t * 3.7f + leafPhase * 1.7f,
                                    t * 5.9f + leafPhase * 2.3f, t * 1.3f + leafPhase)) * 2 - 1;
        float leafWave = lw.x * 0.5f + lw.y * 0.3f + lw.z * 0.2f;
        float leafAmp = flutter * (0.25f + 0.75f * saturate(strength)) * (0.5f + 0.5f * gust) * 0.06f * edge;
        offset += cardNormal * leafWave * leafAmp + float3(dir, 0) * (lw.w * 0.5f + 0.5f) * leafAmp * 0.6f;
        tilt = (float3(dir, 0.3f) * leafWave + cardNormal * lw.w * 0.3f) * saturate(flutter) * 0.45f * edge;
    }

    // 1. Trunk bend, applied last so branches and leaves ride on it
    rel += offset;
    float sway = waves.z * 2 - 1;
    float crossSway = waves.w * 2 - 1;
    float bend = strength * WindParams0.y * (0.55f + 0.45f * gust) * 0.035f * height;
    float2 bendOffset = dir * bend * (1 + 0.35f * sway) + float2(-dir.y, dir.x) * bend * 0.25f * crossSway;
    float restLength = length(rel);
    float3 bent = rel;
    bent.xy += bendOffset * h * h;
    if (restLength > 0.0001f)
        bent = normalize(bent) * restLength;

    return origin + bent;
}

// World position of a vertex: World, then the wind when the Foliage Wind component is on.
float3 FoliageWorldPosition(float4 position, float3 normal, float2 uv, out float3 tilt)
{
    float3 worldPos = mul(position, World).xyz;
    tilt = float3(0, 0, 0);
    [branch]
    if (WindEnabled)
        worldPos = ApplyFoliageWind(worldPos, normalize(mul(normal, (float3x3)World)), uv, World._41_42_43, tilt);
    return worldPos;
}
