// Froxel fog lookup (FroxelRenderModule): the fog between the camera and a point, shared by DeferredCompose and the
// forward pass (Source 2 look, Docs/Research/Source2Look phase 3.4: transparents are fogged too).

// Froxel fog (FroxelRenderModule): integrated prefixes (rgb = in-scattered light, a = optical depth from the
// camera to the far end of each slice) and the resolved medium (rgb = source per metre, a = extinction per metre).
Texture2D FroxelAccumulationTexture;
Texture2D FroxelInjectionTexture;

float NearClip;
float FarClip;
float4x4 InverseProjection;

float3 GridDimensions;
float2 ScreenResolution;

SamplerState FroxelLinearSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};

// Atlas UV of a screen UV in slice fz. XY is clamped to texel centres within the slice so the
// hardware LINEAR sampler interpolates in XY (the ~8 screen px per froxel stay smooth) without
// bleeding into the neighbouring slices. Same as AtlasUV in Froxel.fx.
float2 FroxelAtlasUV(float2 screenUV, int fz)
{
    float2 xy = clamp(screenUV * GridDimensions.xy - 0.5, 0, GridDimensions.xy - 1);
    return float2((xy.x + 0.5) / GridDimensions.x,
                  (fz * GridDimensions.y + xy.y + 0.5) / (GridDimensions.y * GridDimensions.z));
}

// View Z of froxel slice boundary k (Froxel.fx SliceDepth).
float FroxelSliceDepth(float k)
{
    return NearClip * pow(FarClip / NearClip, k / GridDimensions.z);
}

// Fog between the camera and positive view Z viewDepth along the pixel's ray: rgb = light scattered
// towards the camera, a = transmittance. The prefix ending at the pixel's slice start is combined with
// the exact integral over the part of its slice in front of the pixel, so a surface inside a deep
// slice isn't fogged as if it sat at the slice's far end.
float4 SampleFroxelFog(float2 screenUV, float viewDepth)
{
    if (viewDepth <= NearClip) return float4(0, 0, 0, 1);
    float z = min(viewDepth, FarClip);
    int nz = (int)GridDimensions.z;
    int slice = min((int)floor(log(z / NearClip) / log(FarClip / NearClip) * GridDimensions.z), nz - 1);

    // Prefix k ends at boundary k + 1; nothing precedes slice 0. Its alpha is optical depth.
    float4 before = float4(0, 0, 0, 0);
    if (slice > 0)
        before = FroxelAccumulationTexture.SampleLevel(FroxelLinearSampler, FroxelAtlasUV(screenUV, slice - 1), 0);
    float4 medium = FroxelInjectionTexture.SampleLevel(FroxelLinearSampler, FroxelAtlasUV(screenUV, slice), 0);

    float4 ray = mul(float4(screenUV.x * 2 - 1, 1 - screenUV.y * 2, 1, 1), InverseProjection);
    float3 rayVS = normalize(ray.xyz / ray.w);
    float ds = max(z - FroxelSliceDepth(slice), 0) / max(-rayVS.z, 1e-4);

    float sigmaT = max(medium.a, 0);
    float tau = sigmaT * ds;
    float weight = tau < 0.001 ? ds * (1 - 0.5 * tau + tau * tau / 6) : (1 - exp(-tau)) / sigmaT;
    return float4(before.rgb + exp(-before.a) * max(medium.rgb, 0) * weight, exp(-(before.a + tau)));
}
