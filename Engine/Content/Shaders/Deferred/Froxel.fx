// ===================================================================================
// Froxel.fx — Frustum-aligned voxel fog (2D atlas: width = Nx, height = Ny * Nz)
// MonoGame / HLSL SM4
// Atlas texel (fx, fy, fz): px = fx, py = fz * Ny + fy
//
// Slices are logarithmic in positive view-space Z (the same depth the G-buffer stores), so a
// froxel's slice matches the pixels behind it anywhere on screen, not only at the centre.
// Slice k spans view Z [SliceDepth(k), SliceDepth(k + 1)].
//
// Passes (FroxelRenderModule.RenderFroxelFog):
//   BuildFroxels    medium per froxel: rgb = scattering source per metre, a = extinction per metre
//   ResolveFroxels  temporal filter: reprojects last frame's medium and clamps it to this frame's neighbours
//   SeedTransport   per froxel segment: rgb = light scattered towards the camera, a = optical depth
//   ScanTransport   log2(Nz) passes of a prefix scan along Z: texel k = everything from the camera to SliceDepth(k + 1)
// Prefixes keep optical depth rather than transmittance: a thin slice's transmittance (0.9992...) rounds
// away in a half float, while optical depth adds up with full relative precision. Transmittance = exp(-a).
// DeferredCompose.fx samples the prefix before a pixel's slice and integrates the partial slice up to it.
// ===================================================================================

#include "../Common/helper.fx"

#define PI 3.14159265

float4x4 InverseProjection;
float4x4 InverseView;
//Sun shadow cascades (world space), see DeferredDirectionalLight.fx / ShadowMapRenderModule.DrawCascades
#define MAX_CASCADES 4
float4x4 CascadeViewProjection[MAX_CASCADES];
float4x4 CascadeView[MAX_CASCADES];
float4 CascadeFarClip = float4(1, 1, 1, 1);
float4 CascadeTexelSize = float4(1, 1, 1, 1);
float CascadeCount = 0;
float CascadeResolution = 1024;

// Last frame's camera, for the temporal resolve (unjittered).
float4x4 PreviousView;
float4x4 PreviousViewProjection;
bool HistoryValid = false;

float NearClip;
// Far end of the froxel grid (the camera's far plane).
float FarClip;

float3 GridDimensions;

float2 ScreenResolution;

float FroxelDensity = 0.02f;
// Extinction per unit of density: sigmaT = density * FroxelAbsorption (per metre).
float FroxelAbsorption = 0.02f;

// Per-light-type scatter so directional sun and point lights don't fight over the same knob
// (a value strong enough for point-light glow blows out the sun's HG forward peak).
float DirectionalScatter = 0.15f;
float PointScatter = 1.5f;
// Baked bounce light (probe volume) scattered by the fog; 0 without a baked probe volume.
float IndirectScatter = 0.1f;

// Distance-based density ramp. Density linearly goes 0 -> FroxelDensity between
// FogDistanceStart and FogDistanceFull (so near geometry stays crisp, distant fades).
float FogDistanceStart = 30.0f;
float FogDistanceFull = 400.0f;
// Fraction of FroxelDensity kept before FogDistanceStart (0 = clear near the camera, 1 = no ramp).
float FogNearDensity = 0.0f;

// Fog Volume components: density added inside boxes, without the distance ramp (dusty rooms show shafts up close).
// WorldToBox maps world space to the box's unit cube (-1..1); Settings = (density, edge fade, -, -).
#define MAX_FOG_VOLUMES 8
int FogVolumeCount = 0;
float4x4 FogVolumeWorldToBox[MAX_FOG_VOLUMES];
float4 FogVolumeSettings[MAX_FOG_VOLUMES];

float G = 0.45f;

// The deferred light shaders scale their output by 0.1 (OUTPUTCONST in DeferredPointLight.fx,
// the * 0.1f in DeferredDirectionalLight.fx). Apply the same scale to in-scattered light so the
// fog is in the same units as lit surfaces; without it the fog outshines everything behind it.
// The baked probes are already in those units, so the indirect term skips it.
static const float LIGHT_OUTPUT_SCALE = 0.1f;

bool UseDirectionalLight;

float3 DirectionalLightDirectionVS;
float3 DirectionalLightColor;

#define MAX_FROXEL_POINT_LIGHTS 8

int PointLightCount = 0;
float3 PointLightPositionsVS[MAX_FROXEL_POINT_LIGHTS];
float3 PointLightPositionsWS[MAX_FROXEL_POINT_LIGHTS];
float3 PointLightColors[MAX_FROXEL_POINT_LIGHTS];
float PointLightRadii[MAX_FROXEL_POINT_LIGHTS];
float PointLightCastShadows[MAX_FROXEL_POINT_LIGHTS];
float PointLightShadowMapSize[MAX_FROXEL_POINT_LIGHTS];
//Spot cone per light: view space axis, and (cos outer, cos inner) of the half angles. Point lights use (-2, -1).
float3 PointLightSpotDirectionsVS[MAX_FROXEL_POINT_LIGHTS];
float2 PointLightSpotCos[MAX_FROXEL_POINT_LIGHTS];

// Baked probe volume (LightingSystem / ProbeVolumeData), same layout as DeferredEnvironmentMap.fx.
// Probes sit on the corners of a regular grid spanning [ProbeVolumeMin, ProbeVolumeMin + 1/ExtentRcp].
bool UseProbeVolume = false;
float ProbeVolumeIntensity = 1.0f;
float3 ProbeVolumeMin;
float3 ProbeVolumeExtentRcp;
float3 ProbeVolumeCells; //probes per axis - 1
//Baked probe volume data, five grids side by side along X (LightingSystem.ProbeData): SH red, green, blue
//(c0, cx, cy, cz), then the mean free distances along +X/+Y/+Z and -X/-Y/-Z. One texture keeps the pass under
//MonoGame's 16 texture slots.
Texture3D ProbeData;

float4 ProbeTexel(int4 index, int grid)
{
    index.x += grid * ((int)ProbeVolumeCells.x + 1);
    return ProbeData.Load(index);
}

Texture2D ShadowMap;

//Point/spot light shadow atlas (ShadowMapRenderModule): light i's cube face f is tile PointLightShadowTiles[i * 6 + f]
//= (x, y, size, 1 = in use) in atlas texels, faces without a tile are lit
Texture2D PointShadowAtlas;
float4 PointLightShadowTiles[MAX_FROXEL_POINT_LIGHTS * 6];

//One spot light cookie in the fog (the highest ranked light with a cookie, -1 = none): Source 2 look phase 2.3
Texture2D FogCookie;
float FogCookieLight = -1;
float3 FogCookieRightVS;
float3 FogCookieUpVS;
float FogCookieTanOuter = 1;
SamplerState FogCookieSampler
{
    Texture = (FogCookie);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};

// Medium atlas read by ResolveFroxels (this frame's raw injection) and SeedTransport (the resolved medium).
Texture2D FroxelInjectionTexture;
// Last frame's resolved medium.
Texture2D PreviousFroxelHistoryTexture;
// Input of one ScanTransport pass.
Texture2D ScanInputTexture;
Texture2D NoiseMap;
// HistoryAlpha = weight of history. 0.9 means 10% new sample, 90% history (smooth but laggy).
// 0 disables temporal blending entirely. Driven from GameSettings so it's live-tunable.
float HistoryAlpha = 0.85f;
// Atlas rows between a texel and the one it combines with in this ScanTransport pass, in slices.
int ScanStride = 1;

SamplerState PointSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = POINT;
    MinFilter = POINT;
    Mipfilter = POINT;
};

SamplerState LinearSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = POINT;
};

struct VertexShaderInput
{
    float2 Position : POSITION0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
};

// ----------------------------------------------------------------------------------
// Grid helpers
// ----------------------------------------------------------------------------------

// View Z of slice boundary (or fractional slice position) s.
float SliceDepth(float s)
{
    return NearClip * pow(FarClip / NearClip, s / GridDimensions.z);
}

// Continuous slice position of view Z (boundary k at k).
float SliceOfDepth(float viewZ)
{
    return log(max(viewZ, NearClip) / NearClip) / log(FarClip / NearClip) * GridDimensions.z;
}

// Normalised view-space ray through a screen UV (y down). View space looks down -Z.
float3 ViewRay(float2 screenUV)
{
    float4 h = mul(float4(screenUV.x * 2 - 1, 1 - screenUV.y * 2, 1, 1), InverseProjection);
    return normalize(h.xyz / h.w);
}

// The point on a view ray at positive view Z.
float3 ViewPositionAt(float3 ray, float viewZ)
{
    return ray * (viewZ / max(-ray.z, 1e-4));
}

// (fx, fy, fz) of an atlas pixel.
int3 AtlasCoordinate(float2 position)
{
    int ny = (int)GridDimensions.y;
    int nz = (int)GridDimensions.z;
    int fx = clamp((int)position.x, 0, (int)GridDimensions.x - 1);
    int py = clamp((int)position.y, 0, ny * nz - 1);
    int fz = py / ny;
    return int3(fx, py - fz * ny, fz);
}

// Atlas UV of a screen UV in slice fz. XY is clamped to texel centres within the slice so the
// linear sampler interpolates in XY without bleeding into the neighbouring slices.
float2 AtlasUV(float2 screenUV, int fz)
{
    float2 xy = clamp(screenUV * GridDimensions.xy - 0.5, 0, GridDimensions.xy - 1);
    return float2((xy.x + 0.5) / GridDimensions.x,
                  (fz * GridDimensions.y + xy.y + 0.5) / (GridDimensions.y * GridDimensions.z));
}

int2 AtlasPixel(int3 froxel)
{
    return int2(froxel.x, froxel.z * (int)GridDimensions.y + froxel.y);
}

// ----------------------------------------------------------------------------------
// Transport: a segment is (light scattered towards the camera, optical depth); empty = (0,0,0,0).
// ----------------------------------------------------------------------------------

float4 CombineNearFar(float4 nearSegment, float4 farSegment)
{
    return float4(nearSegment.rgb + exp(-nearSegment.a) * farSegment.rgb, nearSegment.a + farSegment.a);
}

// Exact integral over ds metres of a constant medium (source per metre, extinction per metre).
float4 IntegrateSegment(float3 sourcePerMetre, float extinctionPerMetre, float ds)
{
    float sigmaT = max(extinctionPerMetre, 0);
    ds = max(ds, 0);
    float tau = sigmaT * ds;
    // Series near zero avoids cancellation in (1 - T) / sigmaT and has the exact vacuum limit.
    float weight = tau < 0.001 ? ds * (1 - 0.5 * tau + tau * tau / 6) : (1 - exp(-tau)) / sigmaT;
    return float4(max(sourcePerMetre, 0) * weight, tau);
}

// ----------------------------------------------------------------------------------
// Lighting
// ----------------------------------------------------------------------------------

float HenyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    // Floor the denominator to soften the forward peak and keep it from going singular when
    // cosTheta -> 1, which otherwise produces a blown-out spike at the sun position that the
    // froxel grid resolves as visible 8x8 blocks (one froxel = ~8 screen px).
    denom = max(denom, 0.05);
    float phase = (1.0 / (4.0 * PI)) * ((1.0 - g2) / pow(denom, 1.5));
    return min(phase, 4.0);
}

// Matches FogVolumeComponent.DensityAt.
float FogVolumeDensity(float3 worldPos)
{
    float density = 0;
    [loop]
    for (int i = 0; i < FogVolumeCount; ++i)
    {
        float3 p = mul(float4(worldPos, 1.0), FogVolumeWorldToBox[i]).xyz;
        float inside = 1.0 - max(abs(p.x), max(abs(p.y), abs(p.z)));
        if (inside <= 0)
            continue;
        float fade = FogVolumeSettings[i].y;
        density += FogVolumeSettings[i].x * (fade > 0 ? saturate(inside / fade) : 1.0);
    }
    return density;
}

// Matches ShadowMap.fx: each cascade stores light-view-space Z / -its far clip. Compare against the
// same linearised value, NOT the NDC z that ortho projection yields. The first cascade that contains
// the point answers (the cascades are nested spheres around the camera); beyond the last one it is lit.
float ComputeShadow(float3 worldPos)
{
    int count = (int)CascadeCount;
    [loop]
    for (int i = 0; i < count; i++)
    {
        float4 posLVP = mul(float4(worldPos, 1.0), CascadeViewProjection[i]);
        float2 shadowUV = posLVP.xy / posLVP.w * 0.5 + 0.5;
        shadowUV.y = 1.0 - shadowUV.y;
        if (shadowUV.x < 0 || shadowUV.x > 1 || shadowUV.y < 0 || shadowUV.y > 1)
            continue;

        float4 posLV = mul(float4(worldPos, 1.0), CascadeView[i]);
        float linearDepthLV = posLV.z / -CascadeFarClip[i];
        int2 texel = int2(i % 2, i / 2) * (int)CascadeResolution + min(int2(shadowUV * CascadeResolution), (int)CascadeResolution - 1);
        float shadowDepth = ShadowMap.Load(int3(texel, 0)).r;
        float bias = 2 * CascadeTexelSize[i] / CascadeFarClip[i];
        return (linearDepthLV - bias < shadowDepth) ? 1.0 : 0.0;
    }
    return 1.0;
}

// Pulled from helper.fx GetSampleCoordinate but inlined so we don't drag the whole file in.
// Maps a world-space direction (from light to froxel) to the (u,v) in the 6-face vertical strip.
float2 PointShadowSampleCoord(float3 dir)
{
    float2 coord;
    float slice;
    dir.z = -dir.z;

    if (abs(dir.x) >= abs(dir.y) && abs(dir.x) >= abs(dir.z))
    {
        dir.y = -dir.y;
        if (dir.x > 0) { slice = 0; dir /= dir.x; coord = dir.yz; }
        else { dir.z = -dir.z; slice = 1; dir /= dir.x; coord = dir.yz; }
    }
    else if (abs(dir.y) >= abs(dir.x) && abs(dir.y) >= abs(dir.z))
    {
        if (dir.y > 0) { slice = 2; dir /= dir.y; coord = dir.xz; }
        else { dir.z = -dir.z; slice = 3; dir /= dir.y; coord = dir.xz; }
    }
    else
    {
        dir.y = -dir.y;
        if (dir.z < 0) { slice = 4; dir /= dir.z; coord = dir.yx; }
        else { dir.x = -dir.x; slice = 5; dir /= dir.z; coord = dir.yx; }
    }

    const float sixth = 1.0f / 6.0f;
    coord = (coord + float2(1, 1)) * 0.5f;
    coord.y = coord.y * sixth + slice * sixth;
    return coord;
}

float SamplePointLightShadow(int lightIndex, float2 uv)
{
    int face = clamp((int)floor(uv.y * 6), 0, 5);
    float4 tile = PointLightShadowTiles[lightIndex * 6 + face];
    if (tile.w <= 0) return 0;
    float2 local = saturate(float2(uv.x, uv.y * 6 - face));
    int2 texel = int2(tile.xy) + min(int2(local * tile.z), (int)tile.z - 1);
    return PointShadowAtlas.Load(int3(texel, 0)).r;
}

// Sample point-light cube shadow map. Shadow map stores `1 - distFromLightToOccluder/radius`,
// so the in-light test mirrors DeferredPointLight.fx CalcShadowTermPCF.
float ComputePointLightShadow(int lightIndex, float3 worldPos, float3 lightPosWS, float radius)
{
    float3 dirLightToPoint = worldPos - lightPosWS;
    float distance = length(dirLightToPoint);
    if (distance >= radius)
        return 0.0;

    float2 uv = PointShadowSampleCoord(dirLightToPoint);
    float shadowSample = SamplePointLightShadow(lightIndex, uv);
    float occluderDist = 1.0 - shadowSample;
    float testDepth = distance / radius;
    float bias = 0.002;
    return (testDepth - bias < occluderDist) ? 1.0 : 0.0;
}

// Same as ProbeVisibility in DeferredEnvironmentMap.fx / ProbeVolumeData.Visibility: 1 within the
// probe's free distance towards the point, falling off steeply beyond it (a wall in the way).
float ProbeVisibility(float3 toPoint, float3 distPos, float3 distNeg)
{
    float d = length(toPoint);
    if (d < 0.0001f) return 1;
    float3 dir = toPoint / d;
    float3 axisDistance = max(dir >= 0 ? distPos : distNeg, 0.0001f);
    float free = 1.0f / max(dot(dir * dir, 1.0f / axisDistance), 0.000001f);
    if (d <= free) return 1;
    float ratio = free / d;
    ratio *= ratio;
    return ratio * ratio;
}

// Mean radiance (the SH L0 band) of the baked probes around a point in the air. Fog has no surface
// normal, so unlike SampleProbeVolume there is no normal offset or backface term. Outside the volume,
// or where no probe can see the point, it is 0 rather than unoccluded light leaking through walls.
float3 SampleProbeAmbient(float3 positionWS)
{
    float3 uvw = (positionWS - ProbeVolumeMin) * ProbeVolumeExtentRcp;
    float3 edge = min(uvw, 1 - uvw) * ProbeVolumeCells;
    float borderWeight = saturate(min(edge.x, min(edge.y, edge.z)) * 2);
    if (borderWeight <= 0) return 0;

    float3 cells = max(ProbeVolumeCells, 1);
    float3 cellSize = 1.0f / (ProbeVolumeExtentRcp * cells);
    float3 grid = saturate(uvw) * cells;
    float3 base = min(floor(grid), cells - 1);
    float3 f = grid - base;

    float3 l0 = 0;
    float total = 0;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float3 corner = float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
        float3 t = lerp(1 - f, f, corner);
        int4 index = int4(base + corner, 0);
        float3 toPoint = positionWS - (ProbeVolumeMin + (base + corner) * cellSize);
        float w = t.x * t.y * t.z * ProbeVisibility(toPoint, ProbeTexel(index, 3).xyz, ProbeTexel(index, 4).xyz);
        l0 += float3(ProbeTexel(index, 0).x, ProbeTexel(index, 1).x, ProbeTexel(index, 2).x) * w;
        total += w;
    }
    if (total <= 0.00001f) return 0;
    return max(l0 / total, 0) * borderWeight;
}

// Per-froxel jitter in [-0.5, 0.5]^3. noise.png is greyscale (r = g = b), so each axis reads it at a
// different offset; reading one texel for both x and y only ever jittered along the diagonal.
float3 GetBlueNoiseJitter(float2 atlasUV)
{
    float2 noiseUV = atlasUV * float2(32.0, 32.0) + float2(Time * 0.1312, Time * 0.7134);
    float x = NoiseMap.SampleLevel(PointSampler, frac(noiseUV), 0).r;
    float y = NoiseMap.SampleLevel(PointSampler, frac(noiseUV + float2(0.371, 0.613)), 0).r;
    float z = NoiseMap.SampleLevel(PointSampler, frac(noiseUV + float2(0.719, 0.257)), 0).r;
    return float3(x, y, z) - 0.5;
}

// Directional shadow averaged over the froxel's depth range. Distant slices are many metres deep
// (exponential slicing), so one shadow test per froxel aliases against repeating occluders such as
// roof slats and turns the shafts into blotches. Stratified samples give the lit fraction instead.
#define FROXEL_SHADOW_SAMPLES 6
float ComputeShadowAlongSlice(float3 rayVS, int fz, float jitter)
{
    float shadow = 0;
    [unroll]
    for (int i = 0; i < FROXEL_SHADOW_SAMPLES; i++)
    {
        float viewZ = SliceDepth(fz + (i + 0.5 + jitter) / FROXEL_SHADOW_SAMPLES);
        float4 worldH = mul(float4(ViewPositionAt(rayVS, viewZ), 1.0), InverseView);
        shadow += ComputeShadow(worldH.xyz / worldH.w);
    }
    return shadow / FROXEL_SHADOW_SAMPLES;
}

VertexShaderOutput FullscreenVS(VertexShaderInput input)
{
    VertexShaderOutput output;
    output.Position = float4(input.Position, 0, 1);
    return output;
}

// ----------------------------------------------------------------------------------
// PASS 1 — medium per froxel, evaluated at a jittered point inside the cell
// ----------------------------------------------------------------------------------
float4 PixelShaderBuildFroxels(VertexShaderOutput input) : COLOR0
{
    int3 froxel = AtlasCoordinate(input.Position.xy);
    int fz = froxel.z;

    float2 screenUV = (float2(froxel.xy) + 0.5) / GridDimensions.xy;
    float3 jitter = GetBlueNoiseJitter(screenUV);
    float3 rayVS = ViewRay(screenUV + jitter.xy / GridDimensions.xy);
    float3 posVS = ViewPositionAt(rayVS, SliceDepth(fz + 0.5 + jitter.z));
    float dist = length(posVS);

    float4 worldH = mul(float4(posVS, 1.0), InverseView);
    float3 worldPos = worldH.xyz / worldH.w;

    // Distance ramp: keep nearby fog thin so foreground objects stay readable, swell toward the distance.
    float distanceRamp = saturate((dist - FogDistanceStart) / max(FogDistanceFull - FogDistanceStart, 1e-3));
    float density = FroxelDensity * lerp(FogNearDensity, 1.0, distanceRamp) + FogVolumeDensity(worldPos);
    float3 scatter = 0;

    float3 viewDir = -rayVS;

    if (UseDirectionalLight)
    {
        // Scattering angle between the light's travel direction and the direction to the camera:
        // 1 when looking straight towards the sun, which is where the forward peak belongs.
        float cosTheta = dot(normalize(DirectionalLightDirectionVS), viewDir);
        float phase = HenyeyGreenstein(cosTheta, G);
        float shadow = ComputeShadowAlongSlice(rayVS, fz, jitter.z);
        scatter += DirectionalLightColor * LIGHT_OUTPUT_SCALE * phase * density * DirectionalScatter * shadow;
    }

    [loop]
    for (int li = 0; li < PointLightCount; ++li)
    {
        float3 toLight = PointLightPositionsVS[li] - posVS;
        float distToLight = length(toLight);
        float radius = PointLightRadii[li];

        if (distToLight >= radius)
            continue;

        float3 lightDirPt = toLight / max(distToLight, 1e-4);
        float falloff = saturate(1.0 - distToLight / radius);
        falloff *= falloff;
        float attenuation = falloff / max(distToLight * distToLight, 0.01);

        //Spot cone, mirrors SpotConeFactor in DeferredPointLight.fx
        float2 spotCos = PointLightSpotCos[li];
        float spotT = saturate((dot(-lightDirPt, PointLightSpotDirectionsVS[li]) - spotCos.x) / max(spotCos.y - spotCos.x, 0.0001f));
        attenuation *= spotT * spotT;
        if (attenuation <= 0.0)
            continue;

        float cosTheta = dot(-lightDirPt, viewDir);
        float phase = HenyeyGreenstein(cosTheta, G);

        // PointLightCastShadows = the light's shadow strength (fades out with distance from the camera)
        float ptShadow = 1.0;
        if (PointLightCastShadows[li] > 0.001)
            ptShadow = lerp(1, ComputePointLightShadow(li, worldPos, PointLightPositionsWS[li], radius), PointLightCastShadows[li]);

        // Spot light cookie (one per frame, FogCookieLight)
        float3 cookie = 1;
        [branch]
        if (abs(FogCookieLight - li) < 0.5)
        {
            float3 l = -lightDirPt;
            float forward = dot(l, PointLightSpotDirectionsVS[li]);
            float2 uv = float2(dot(l, FogCookieRightVS), dot(l, FogCookieUpVS)) / max(forward * FogCookieTanOuter, 1e-4) * 0.5 + 0.5;
            cookie = forward > 0.001 ? FogCookie.SampleLevel(FogCookieSampler, float2(uv.x, 1 - uv.y), 0).rgb : 0;
        }

        scatter += PointLightColors[li] * cookie * LIGHT_OUTPUT_SCALE * phase * density * PointScatter * attenuation * ptShadow;
    }

    // Bounce light: isotropic in-scattering of the probes' mean radiance (already in lit-surface units).
    [branch]
    if (UseProbeVolume && IndirectScatter > 0)
        scatter += SampleProbeAmbient(worldPos) * ProbeVolumeIntensity * density * IndirectScatter;

    return float4(scatter, density * FroxelAbsorption);
}

// ----------------------------------------------------------------------------------
// PASS 2 — temporal resolve of the medium. Last frame's medium is reprojected from the unjittered
// cell centre, clamped to this frame's neighbourhood (rejects disocclusion, moved lights and
// fog volumes), and blended. History outside last frame's frustum is dropped.
// ----------------------------------------------------------------------------------

static const int3 NeighbourOffsets[6] = { int3(1, 0, 0), int3(-1, 0, 0), int3(0, 1, 0), int3(0, -1, 0), int3(0, 0, 1), int3(0, 0, -1) };

// Last frame's continuous froxel position of a world point: atlas-space screen UV and slice
// position of injection samples (which sit at slice centres). False outside last frame's grid.
bool PreviousFroxelCoordinate(float3 worldPosition, out float2 previousUV, out float previousSliceF)
{
    float4 clip = mul(float4(worldPosition, 1), PreviousViewProjection);
    float z = -mul(float4(worldPosition, 1), PreviousView).z;
    previousUV = float2(0, 0);
    previousSliceF = 0;
    if (clip.w <= 0) return false;
    float2 ndc = clip.xy / clip.w;
    previousUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
    if (any(abs(ndc) > 1) || z < NearClip || z > FarClip) return false;
    previousSliceF = SliceOfDepth(z) - 0.5;
    return true;
}

float4 PixelShaderResolveFroxels(VertexShaderOutput input) : COLOR0
{
    int3 froxel = AtlasCoordinate(input.Position.xy);
    int2 pixel = AtlasPixel(froxel);
    float4 current = FroxelInjectionTexture.Load(int3(pixel, 0));

    [branch]
    if (!HistoryValid || HistoryAlpha <= 0)
        return current;

    float2 screenUV = (float2(froxel.xy) + 0.5) / GridDimensions.xy;
    float3 posVS = ViewPositionAt(ViewRay(screenUV), SliceDepth(froxel.z + 0.5));
    float4 worldH = mul(float4(posVS, 1.0), InverseView);

    float2 previousUV;
    float previousSliceF;
    if (!PreviousFroxelCoordinate(worldH.xyz / worldH.w, previousUV, previousSliceF))
        return current;

    // A 2D atlas has no trilinear filtering: blend the two nearest slices by hand.
    int nz = (int)GridDimensions.z;
    float sliceF = clamp(previousSliceF, 0, nz - 1);
    int s0 = (int)floor(sliceF);
    int s1 = min(s0 + 1, nz - 1);
    float4 history = lerp(PreviousFroxelHistoryTexture.SampleLevel(LinearSampler, AtlasUV(previousUV, s0), 0),
                          PreviousFroxelHistoryTexture.SampleLevel(LinearSampler, AtlasUV(previousUV, s1), 0),
                          sliceF - s0);

    // Neighbourhood clamp: the 6 face neighbours (within the grid) and the cell itself.
    int3 maxFroxel = int3(GridDimensions) - 1;
    float4 low = current;
    float4 high = current;
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        float4 neighbour = FroxelInjectionTexture.Load(int3(AtlasPixel(clamp(froxel + NeighbourOffsets[i], 0, maxFroxel)), 0));
        low = min(low, neighbour);
        high = max(high, neighbour);
    }
    history = clamp(history, low, high);

    return lerp(current, history, HistoryAlpha);
}

// ----------------------------------------------------------------------------------
// PASS 3 — one segment per froxel: its slice's view-Z range along this column's ray, in metres.
// ----------------------------------------------------------------------------------
float4 PixelShaderSeedTransport(VertexShaderOutput input) : COLOR0
{
    int3 froxel = AtlasCoordinate(input.Position.xy);
    float2 screenUV = (float2(froxel.xy) + 0.5) / GridDimensions.xy;
    float ds = (SliceDepth(froxel.z + 1) - SliceDepth(froxel.z)) / max(-ViewRay(screenUV).z, 1e-4);
    float4 medium = FroxelInjectionTexture.Load(int3(AtlasPixel(froxel), 0));
    return IntegrateSegment(medium.rgb, medium.a, ds);
}

// ----------------------------------------------------------------------------------
// PASS 4 — prefix scan step (Hillis-Steele): combine with the prefix ScanStride slices nearer.
// After strides 1, 2, 4 .. Nz/2, texel k holds the transport from the camera to SliceDepth(k + 1).
// ----------------------------------------------------------------------------------
float4 PixelShaderScanTransport(VertexShaderOutput input) : COLOR0
{
    int3 froxel = AtlasCoordinate(input.Position.xy);
    int2 pixel = AtlasPixel(froxel);
    float4 current = ScanInputTexture.Load(int3(pixel, 0));
    if (froxel.z < ScanStride)
        return current;
    int2 nearPixel = pixel - int2(0, ScanStride * (int)GridDimensions.y);
    return CombineNearFar(ScanInputTexture.Load(int3(nearPixel, 0)), current);
}

technique BuildFroxels
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 FullscreenVS();
        PixelShader = compile ps_4_0 PixelShaderBuildFroxels();
    }
}

technique ResolveFroxels
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 FullscreenVS();
        PixelShader = compile ps_4_0 PixelShaderResolveFroxels();
    }
}

technique SeedTransport
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 FullscreenVS();
        PixelShader = compile ps_4_0 PixelShaderSeedTransport();
    }
}

technique ScanTransport
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 FullscreenVS();
        PixelShader = compile ps_4_0 PixelShaderScanTransport();
    }
}
