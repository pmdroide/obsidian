// Original Obsidian implementation draft. Not Valve source or recovered HLSL.
// Not registered in Content.mgcb. See README.md for the required host changes.
// Injection.rgb = scattering source per metre, Injection.a = extinction per metre.
// Prefix.rgb = integrated radiance, Prefix.a = transmittance (empty = 0,0,0,1).

float3 GridDimensions = float3(160, 90, 128);
float NearClip = 1;
float FogFarClip = 400;
float4x4 InverseProjection;
int ScanStride = 1;

Texture2D Injection;
Texture2D ScanInput;
Texture2D Integrated;
Texture2D LinearSceneDepth; // Positive VIEW-Z in world units, NOT depth/FarClip.

SamplerState PointClamp
{
    Filter = MIN_MAG_MIP_POINT;
    AddressU = Clamp;
    AddressV = Clamp;
};
SamplerState LinearClamp
{
    Filter = MIN_MAG_LINEAR_MIP_POINT;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VSInput { float2 Position : POSITION0; };
struct VSOutput { float4 Position : SV_POSITION; };
VSOutput FullscreenVS(VSInput input)
{
    VSOutput result;
    result.Position = float4(input.Position, 0, 1);
    return result;
}

float4 EmptyTransport() { return float4(0, 0, 0, 1); }

float4 CombineNearFar(float4 nearSegment, float4 farSegment)
{
    return float4(nearSegment.rgb + nearSegment.a * farSegment.rgb,
                  nearSegment.a * farSegment.a);
}

float4 IntegrateSegment(float3 sourcePerMetre, float extinctionPerMetre, float ds)
{
    float sigmaT = max(extinctionPerMetre, 0);
    ds = max(ds, 0);
    float tau = sigmaT * ds;
    float transmit = exp(-tau);
    // Series avoids cancellation near zero and has the exact vacuum limit.
    float weight;
    if (tau < 0.001)
        weight = ds * (1 - 0.5 * tau + tau * tau / 6);
    else
        weight = (1 - transmit) / sigmaT;
    return float4(max(sourcePerMetre, 0) * weight, transmit);
}

float SliceBoundary(int k)
{
    return NearClip * pow(FogFarClip / NearClip, k / GridDimensions.z);
}

float3 RayVS(float2 screenUV)
{
    float4 h = mul(float4(screenUV.x * 2 - 1, 1 - screenUV.y * 2, 1, 1), InverseProjection);
    return normalize(h.xyz / h.w);
}

float2 AtlasUV(float2 screenUV, int z)
{
    // Clamp within a slice; hardware bilinear filtering must not cross Z tiles.
    float2 xy = clamp(screenUV * GridDimensions.xy - 0.5, 0, GridDimensions.xy - 1);
    return float2((xy.x + 0.5) / GridDimensions.x,
                  (z * GridDimensions.y + xy.y + 0.5) / (GridDimensions.y * GridDimensions.z));
}

int3 AtlasCoordinate(float2 position)
{
    int2 p = int2(position);
    int z = p.y / (int)GridDimensions.y;
    return int3(p.x, p.y - z * (int)GridDimensions.y, z);
}

float4 SeedPS(VSOutput input) : SV_TARGET
{
    int3 p = AtlasCoordinate(input.Position.xy);
    float2 uv = (float2(p.xy) + 0.5) / GridDimensions.xy;
    float ds = (SliceBoundary(p.z + 1) - SliceBoundary(p.z)) / max(-RayVS(uv).z, 1e-4);
    float4 medium = Injection.Load(int3(int2(input.Position.xy), 0));
    return IntegrateSegment(medium.rgb, medium.a, ds);
}

float4 ScanPS(VSOutput input) : SV_TARGET
{
    int2 pixel = int2(input.Position.xy);
    int3 p = AtlasCoordinate(input.Position.xy);
    float4 current = ScanInput.Load(int3(pixel, 0));
    if (p.z < ScanStride) return current;
    int2 nearPixel = pixel - int2(0, ScanStride * (int)GridDimensions.y);
    return CombineNearFar(ScanInput.Load(int3(nearPixel, 0)), current);
}

float4 SampleFog(float2 screenUV, float viewDepth)
{
    if (viewDepth <= NearClip) return EmptyTransport();
    float z = min(viewDepth, FogFarClip);
    float sliceF = log(z / NearClip) / log(FogFarClip / NearClip) * GridDimensions.z;
    int slice = min((int)floor(sliceF), (int)GridDimensions.z - 1);
    // Prefix[k] ends at boundary k+1. Identity precedes slice zero.
    float4 before = EmptyTransport();
    if (slice > 0)
        before = Integrated.SampleLevel(LinearClamp, AtlasUV(screenUV, slice - 1), 0);
    float4 medium = Injection.SampleLevel(LinearClamp, AtlasUV(screenUV, slice), 0);
    float partialDs = max(z - SliceBoundary(slice), 0) / max(-RayVS(screenUV).z, 1e-4);
    return CombineNearFar(before, IntegrateSegment(medium.rgb, medium.a, partialDs));
}

// Helper for an injection-history pass; use the unjittered current froxel position.
// This does not validate light/density changes, exposure, camera cuts or history lifetime.
bool PreviousFroxelCoordinate(float3 worldPosition, float4x4 previousView,
    float4x4 previousViewProjection, float previousNear, float previousFar,
    out float2 previousUV, out float previousSliceF)
{
    float4 clip = mul(float4(worldPosition, 1), previousViewProjection);
    float z = -mul(float4(worldPosition, 1), previousView).z;
    previousUV = float2(0, 0);
    previousSliceF = 0;
    if (clip.w <= 0 || previousNear <= 0 || previousFar <= previousNear) return false;
    float2 ndc = clip.xy / clip.w;
    previousUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
    if (any(abs(ndc) > 1) || z < previousNear || z > previousFar) return false;
    // Injection samples live at slice centres, unlike integrated prefixes.
    previousSliceF = log(z / previousNear) / log(previousFar / previousNear) * GridDimensions.z - 0.5;
    return true;
}

float4 SampleDebugPS(VSOutput input) : SV_TARGET
{
    // Debug technique renders at atlas XY resolution, not full screen resolution.
    float2 uv = input.Position.xy / GridDimensions.xy;
    float depth = LinearSceneDepth.SampleLevel(PointClamp, uv, 0).r;
    return SampleFog(uv, depth);
}

technique SeedTransport
{
    pass P0 { VertexShader = compile vs_4_0 FullscreenVS(); PixelShader = compile ps_4_0 SeedPS(); }
}
technique ScanTransport
{
    pass P0 { VertexShader = compile vs_4_0 FullscreenVS(); PixelShader = compile ps_4_0 ScanPS(); }
}
technique SampleTransportDebug
{
    pass P0 { VertexShader = compile vs_4_0 FullscreenVS(); PixelShader = compile ps_4_0 SampleDebugPS(); }
}
