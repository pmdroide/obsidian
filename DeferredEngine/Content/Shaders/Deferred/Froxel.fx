// ===================================================================================
// Froxel.fx — Frustum-aligned voxel fog (2D atlas: width = Nx, height = Ny * Nz)
// MonoGame / HLSL SM4
// Atlas texel (fx, fy, fz): px = fx, py = fz * Ny + fy
// ===================================================================================

#include "../Common/helper.fx"

#define PI 3.14159265

float4x4 InverseProjection;
float4x4 InverseView;
float4x4 LightViewProjection;

float NearClip;
float FarClip;

float3 GridDimensions;

float2 ScreenResolution;

float FroxelDensity = 0.02f;
float FroxelScatter = 1.0f;
float FroxelAbsorption = 0.02f;

float G = 0.7f;

bool UseDirectionalLight;

float3 DirectionalLightDirectionVS;
float3 DirectionalLightColor;

#define MAX_FROXEL_POINT_LIGHTS 8

int PointLightCount = 0;
float3 PointLightPositionsVS[MAX_FROXEL_POINT_LIGHTS];
float3 PointLightColors[MAX_FROXEL_POINT_LIGHTS];
float PointLightRadii[MAX_FROXEL_POINT_LIGHTS];

Texture2D AlbedoMap;
Texture2D NormalMap;
Texture2D DepthMap;
Texture2D ShadowMap;

Texture2D FroxelInjectionTexture;
Texture2D PreviousFroxelAccumulationTexture;
Texture2D FroxelAccumulationTexture;
Texture2D NoiseMap;
// HistoryAlpha = weight of history. 0.9 means 10% new sample, 90% history (smooth but laggy).
// 0 disables temporal blending entirely.
float HistoryAlpha = 0.9f;

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
    Mipfilter = LINEAR;
};

struct VertexShaderInput
{
    float2 Position : POSITION0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
};

float HenyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 / (4.0 * PI)) * ((1.0 - g2) / pow(abs(denom), 1.5));
}

float2 FroxelIndicesToAtlasUV(int fx, int fy, int fz)
{
    float nx = GridDimensions.x;
    float ny = GridDimensions.y;
    float nz = GridDimensions.z;
    float u = (fx + 0.5) / nx;
    float v = (fz * ny + fy + 0.5) / (ny * nz);
    return float2(u, v);
}

float4 SampleFroxelAccumulationTexture(float2 screenUV, float depth)
{
    int nx = (int)GridDimensions.x;
    int ny = (int)GridDimensions.y;
    int nz = (int)GridDimensions.z;

    int fx = clamp((int)(screenUV.x * nx), 0, nx - 1);
    int fy = clamp((int)(screenUV.y * ny), 0, ny - 1);

    float normalizedDepth = max(depth, NearClip);
    float slicePos = saturate(log(normalizedDepth / NearClip) / log(FarClip / NearClip)) * nz;
    int fz0 = clamp((int)floor(slicePos), 0, nz - 1);
    int fz1 = min(fz0 + 1, nz - 1);
    float zFrac = frac(slicePos);

    float2 uv0 = FroxelIndicesToAtlasUV(fx, fy, fz0);
    float2 uv1 = FroxelIndicesToAtlasUV(fx, fy, fz1);

    float4 sample0 = FroxelAccumulationTexture.Sample(LinearSampler, uv0);
    float4 sample1 = FroxelAccumulationTexture.Sample(LinearSampler, uv1);

    return lerp(sample0, sample1, zFrac);
}

float ComputeShadow(float3 worldPos)
{
    float4 shadowPos = mul(float4(worldPos, 1.0), LightViewProjection);
    shadowPos.xyz /= shadowPos.w;

    float2 shadowUV = shadowPos.xy * 0.5 + 0.5;
    shadowUV.y = 1.0 - shadowUV.y;

    if (shadowUV.x < 0 || shadowUV.x > 1 || shadowUV.y < 0 || shadowUV.y > 1)
        return 1.0;

    float shadowDepth = ShadowMap.SampleLevel(PointSampler, shadowUV, 0).r;
    float bias = 0.005;
    return (shadowPos.z - bias <= shadowDepth) ? 1.0 : 0.0;
}

float2 GetBlueNoiseJitter(float2 atlasUV)
{
    float2 noiseUV = frac(atlasUV * float2(32.0, 32.0) + float2(Time * 0.1312, Time * 0.7134));
    float4 noise = NoiseMap.SampleLevel(PointSampler, noiseUV, 0);
    return (noise.rg * 2.0 - 1.0) * 0.5;
}

VertexShaderOutput FullscreenVS(VertexShaderInput input)
{
    VertexShaderOutput output;
    output.Position = float4(input.Position, 0, 1);
    return output;
}

// PASS 1 — one atlas texel = froxel (fx, fy, fz); exponential depth per slice
float4 PixelShaderBuildFroxels(VertexShaderOutput input) : COLOR0
{
    int nx = (int)GridDimensions.x;
    int ny = (int)GridDimensions.y;
    int nz = (int)GridDimensions.z;

    int fx = clamp((int)input.Position.x, 0, nx - 1);
    int py = clamp((int)input.Position.y, 0, ny * nz - 1);
    int fz = py / ny;
    int fy = py - fz * ny;

    float t = (fz + 0.5) / (float)nz;
    float dist = NearClip * pow(FarClip / NearClip, t);

    float2 screenUV = float2((fx + 0.5) / (float)nx, (fy + 0.5) / (float)ny);
    float2 jitter = GetBlueNoiseJitter(screenUV);
    float ndcX = screenUV.x * 2.0 - 1.0 + jitter.x / (float)nx;
    float ndcY = 1.0 - screenUV.y * 2.0 + jitter.y / (float)ny;

    float4 viewH = mul(float4(ndcX, ndcY, 1.0, 1.0), InverseProjection);
    float3 dirVS = normalize(viewH.xyz / viewH.w);
    float3 posVS = dirVS * dist;

    float4 worldH = mul(float4(posVS, 1.0), InverseView);
    float3 worldPos = worldH.xyz / worldH.w;

    float density = FroxelDensity;
    float3 scatter = 0;

    float3 viewDir = normalize(-dirVS);

    if (UseDirectionalLight)
    {
        float3 lightDir = normalize(-DirectionalLightDirectionVS);
        float cosTheta = dot(lightDir, viewDir);
        float phase = HenyeyGreenstein(cosTheta, G);
        float shadow = ComputeShadow(worldPos);
        scatter += DirectionalLightColor * phase * density * FroxelScatter * shadow;
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

        float cosTheta = dot(lightDirPt, viewDir);
        float phase = HenyeyGreenstein(cosTheta, G);

        scatter += PointLightColors[li] * phase * density * FroxelScatter * attenuation;
    }

    return float4(scatter, density);
}

// PASS 2 — for texel (fx,fy,fz), accumulate slices k = 0 .. fz (Beer–Lambert)
float4 PixelShaderAccumulateFroxels(VertexShaderOutput input) : COLOR0
{
    int nx = (int)GridDimensions.x;
    int ny = (int)GridDimensions.y;
    int nz = (int)GridDimensions.z;

    int fx = clamp((int)input.Position.x, 0, nx - 1);
    int py = clamp((int)input.Position.y, 0, ny * nz - 1);
    int fz = py / ny;
    int fy = py - fz * ny;

    float3 accumScatter = 0;
    float accumTransmit = 1.0;

    float logFarOverNear = log(FarClip / NearClip);

    [loop]
    for (int k = 0; k <= fz; k++)
    {
        float2 uv = FroxelIndicesToAtlasUV(fx, fy, k);
        float4 slice = FroxelInjectionTexture.SampleLevel(PointSampler, uv, 0);

        float3 sc = slice.rgb;
        float dens = slice.a;

        // Exponential slice boundaries: d(t) = NearClip * (FarClip/NearClip)^t, t = k/nz.
        float tNear = (float)k / (float)nz;
        float tFar = (float)(k + 1) / (float)nz;
        float distNear = NearClip * exp(tNear * logFarOverNear);
        float distFar = NearClip * exp(tFar * logFarOverNear);
        float sliceThickness = distFar - distNear;

        float sliceTransmit = exp(-dens * FroxelAbsorption * sliceThickness);

        accumScatter += sc * accumTransmit * sliceThickness;
        accumTransmit *= sliceTransmit;
    }

    float2 currentAtlasUV = FroxelIndicesToAtlasUV(fx, fy, fz);
    float4 prevAccum = PreviousFroxelAccumulationTexture.SampleLevel(LinearSampler, currentAtlasUV, 0);
    accumScatter = lerp(accumScatter, prevAccum.rgb, HistoryAlpha);
    accumTransmit = lerp(accumTransmit, prevAccum.a, HistoryAlpha);

    return float4(accumScatter, accumTransmit);
}

// PASS 3 — optional debug / legacy fullscreen compose (not used when DeferredCompose samples atlas)
float4 PixelShaderComposeFroxels(VertexShaderOutput input) : COLOR0
{
    int3 texCoordInt = int3(input.Position.xy, 0);

    float4 albedo = AlbedoMap.Load(texCoordInt);
    float depth = DepthMap.Load(texCoordInt).r;

    float dist = saturate(depth) * FarClip;
    dist = max(dist, NearClip);

    float tDepth = saturate(log(dist / NearClip) / log(FarClip / NearClip));

    int nx = (int)GridDimensions.x;
    int ny = (int)GridDimensions.y;
    int nz = (int)GridDimensions.z;

    float2 screenUV = float2(
        (input.Position.x + 0.5) / ScreenResolution.x,
        (input.Position.y + 0.5) / ScreenResolution.y);

    int fx = clamp((int)(screenUV.x * (float)nx), 0, nx - 1);
    int fy = clamp((int)(screenUV.y * (float)ny), 0, ny - 1);

    float4 fog = SampleFroxelAccumulationTexture(screenUV, dist);

    float3 finalColor = albedo.rgb * fog.a + fog.rgb;
    return float4(finalColor, 1.0);
}

technique BuildFroxels
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 FullscreenVS();
        PixelShader = compile ps_4_0 PixelShaderBuildFroxels();
    }
}

technique AccumulateFroxels
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 FullscreenVS();
        PixelShader = compile ps_4_0 PixelShaderAccumulateFroxels();
    }
}

technique ComposeFroxels
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 FullscreenVS();
        PixelShader = compile ps_4_0 PixelShaderComposeFroxels();
    }
}
