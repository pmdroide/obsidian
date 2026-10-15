////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Deferred Compose
// HDR Deferred Composition + True Froxel Volumetric Fog
////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "../Common/helper.fx"

////////////////////////////////////////////////////////////////////////////////////////////////////////////
// TEXTURES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

Texture2D colorMap;
Texture2D normalMap;
Texture2D diffuseLightMap;
Texture2D specularLightMap;
Texture2D SSAOMap;
Texture2D DepthMap;
Texture2D volumeLightMap;

// Froxel fog (FroxelRenderModule): integrated prefixes (rgb = in-scattered light, a = optical depth from the
// camera to the far end of each slice) and the resolved medium (rgb = source per metre, a = extinction per metre).
Texture2D FroxelAccumulationTexture;
Texture2D FroxelInjectionTexture;

////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CAMERA / FROXELS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

float NearClip;
float FarClip;
float4x4 InverseProjection;

float3 GridDimensions;
float2 ScreenResolution;

bool useSSAO = true;
bool UseFroxelFog = true;

// 0..1 — how much fog impacts sky pixels. Defaults are tuned so the skybox shows through; raise
// for moodier atmospheric distance haze. Independent from the per-light scatter values.
float SkyFogStrength = 0.25f;

// Sky pixels keep the fog in front of them up to this distance at full strength; only the haze
// beyond it is scaled by SkyFogStrength. Light shafts seen towards the sun sit in front of the sky
// (the gap they shine through), so scaling the whole column erased them when facing the light.
float SkyShaftDistance = 200.0f;

////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SAMPLERS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

sampler pointSampler = sampler_state
{
    Texture = (colorMap);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = POINT;
    MinFilter = POINT;
    Mipfilter = POINT;
};

SamplerState linearSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};

////////////////////////////////////////////////////////////////////////////////////////////////////////////
// STRUCT DEFINITIONS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct VertexShaderInput
{
    float2 Position : POSITION0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
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
        before = FroxelAccumulationTexture.SampleLevel(linearSampler, FroxelAtlasUV(screenUV, slice - 1), 0);
    float4 medium = FroxelInjectionTexture.SampleLevel(linearSampler, FroxelAtlasUV(screenUV, slice), 0);

    float4 ray = mul(float4(screenUV.x * 2 - 1, 1 - screenUV.y * 2, 1, 1), InverseProjection);
    float3 rayVS = normalize(ray.xyz / ray.w);
    float ds = max(z - FroxelSliceDepth(slice), 0) / max(-rayVS.z, 1e-4);

    float sigmaT = max(medium.a, 0);
    float tau = sigmaT * ds;
    float weight = tau < 0.001 ? ds * (1 - 0.5 * tau + tau * tau / 6) : (1 - exp(-tau)) / sigmaT;
    return float4(before.rgb + exp(-before.a) * max(medium.rgb, 0) * weight, exp(-(before.a + tau)));
}

// 5x5 bilateral, sampled at 2px stride so it spans ~10 screen px - roughly one full froxel
// across (the atlas is 160x90 over a 1280x720 viewport, so ~8 px per froxel). Without this
// stride a 3x3 kernel stays inside a single froxel block and the grid stays visible when still.
float4 SampleFroxelBilateral(float2 screenUV, float3 centerNormal, float centerDepth)
{
    const float kernel5[5] = { 0.07, 0.24, 0.38, 0.24, 0.07 };
    float2 texel = 1.0 / ScreenResolution;
    const float stride = 2.0;

    float4 result = 0;
    float weightSum = 0;

    [unroll]
    for (int y = -2; y <= 2; ++y)
    {
        [unroll]
        for (int x = -2; x <= 2; ++x)
        {
            float2 sampleUV = clamp(screenUV + float2(x, y) * texel * stride, 0.0, 1.0);
            float4 depthSample = DepthMap.SampleLevel(pointSampler, sampleUV, 0);
            // G-buffer depth is view Z / FarClip (sky = 1, the far plane).
            float sampleDepth = saturate(depthSample.r) * FarClip;

            float4 normalSample = normalMap.SampleLevel(pointSampler, sampleUV, 0);
            float3 sampleNormal = decode(normalSample.xyz);

            // Slightly relax weights so wider taps still contribute - we'd rather have soft fog
            // than sharp 8x8 froxel blocks bleeding through.
            float depthWeight = exp(-abs(sampleDepth - centerDepth) * 0.04);
            float normalWeight = pow(saturate(dot(centerNormal, sampleNormal)), 8.0);
            float spatialWeight = kernel5[x + 2] * kernel5[y + 2];

            float4 fogSample = SampleFroxelFog(sampleUV, sampleDepth);

            float weight = spatialWeight * depthWeight * normalWeight + 5e-3;
            result += fogSample * weight;
            weightSum += weight;
        }
    }

    return result / max(weightSum, 1e-4);
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
// VERTEX SHADER
////////////////////////////////////////////////////////////////////////////////////////////////////////////

VertexShaderOutput VertexShaderFunction(VertexShaderInput input, uint id : SV_VERTEXID)
{
    VertexShaderOutput output;

    output.Position = float4(input.Position, 0, 1);

    output.TexCoord.x =
        (float)(id / 2) * 2.0;

    output.TexCoord.y =
        1.0 - (float)(id % 2) * 2.0;

    return output;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
// PIXEL SHADER
////////////////////////////////////////////////////////////////////////////////////////////////////////////

float4 PixelShaderFunction(VertexShaderOutput input) : COLOR0
{
    int3 texCoordInt =
        int3(input.Position.xy, 0);

    ////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GBUFFER
    ////////////////////////////////////////////////////////////////////////////////////////////////////////

    float4 diffuseColor =
        colorMap.Load(texCoordInt);

    float4 normalInfo =
        normalMap.Load(texCoordInt);

    // GBuffer normal channels are zero for empty (sky) pixels. The sphere-map decode
    // produces NaN there (sqrt of negative), which would poison the bilateral fog filter.
    bool isSky = (normalInfo.x + normalInfo.y) <= 0.001f;

    float3 centerNormal = isSky ? float3(0, 0, 1) : decode(normalInfo.xyz);

    // Gamma -> Linear
    diffuseColor.rgb =
        pow(abs(diffuseColor.rgb), 2.2f);

    ////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MATERIAL DATA
    ////////////////////////////////////////////////////////////////////////////////////////////////////////

    float materialType =
        decodeMattype(normalInfo.b);

    float metalness =
        decodeMetalness(normalInfo.b);

    ////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SSAO
    ////////////////////////////////////////////////////////////////////////////////////////////////////////

    float ssaoContribution = 1.0;

    if (useSSAO)
    {
        ssaoContribution =
            SSAOMap.SampleLevel(
                pointSampler,
                input.TexCoord,
                0).r;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LIGHTING
    ////////////////////////////////////////////////////////////////////////////////////////////////////////

    float3 diffuseLight =
        diffuseLightMap.Load(texCoordInt).rgb;

    float3 specularLight =
        specularLightMap.Load(texCoordInt).rgb;

    float3 plasticFinal =
        diffuseColor.rgb * diffuseLight +
        specularLight;

    float3 metalFinal =
        diffuseColor.rgb * specularLight;

    float3 finalValue =
        lerp(
            plasticFinal,
            metalFinal,
            metalness);

    ////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EMISSIVE
    ////////////////////////////////////////////////////////////////////////////////////////////////////////

    if (abs(materialType - 3) < 0.1f)
    {
        return float4(
            diffuseColor.rgb * metalness * 8,
            1);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////
    // FROXEL FOG
    ////////////////////////////////////////////////////////////////////////////////////////////////////////

    float3 volumetrics = 0;
    float transmittance = 1.0;

    if (UseFroxelFog)
    {
        float depthSample =
            DepthMap.Load(texCoordInt).r;

        // G-buffer stores view-space Z / (-FarClip): the positive view Z the froxel slices use.
        float dist =
            saturate(depthSample) *
            FarClip;

        // Skip bilateral filtering for sky pixels - their cleared normal data breaks the normal weighting.
        float4 fog = isSky
            ? SampleFroxelFog(input.TexCoord, dist)
            : SampleFroxelBilateral(input.TexCoord, centerNormal, dist);

        volumetrics =
            fog.rgb;

        transmittance =
            saturate(fog.a);

        // For sky pixels, dial the far haze down so the skybox doesn't get washed out by accumulated
        // scatter from the entire view-frustum depth, but keep the near part (where shafts live) like
        // any surface at that distance. SkyFogStrength = 0 -> sky only gets the near fog.
        if (isSky)
        {
            float4 nearFog = SampleFroxelFog(input.TexCoord, min(max(SkyShaftDistance, NearClip), dist));
            volumetrics = nearFog.rgb + max(fog.rgb - nearFog.rgb, 0) * SkyFogStrength;
            transmittance = lerp(saturate(nearFog.a), transmittance, SkyFogStrength);
        }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////
    // FINAL COMPOSITION
    ////////////////////////////////////////////////////////////////////////////////////////////////////////

    float3 output =
        finalValue *
        ssaoContribution *
        transmittance +
        volumetrics;

    return float4(output, 1);
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
// TECHNIQUE
////////////////////////////////////////////////////////////////////////////////////////////////////////////

technique TechniqueLinear
{
    pass Pass1
    {
        VertexShader =
            compile vs_4_0 VertexShaderFunction();

        PixelShader =
            compile ps_4_0 PixelShaderFunction();
    }
}

technique TechniqueNonLinear
{
    pass Pass1
    {
        VertexShader =
            compile vs_4_0 VertexShaderFunction();

        PixelShader =
            compile ps_4_0 PixelShaderFunction();
    }
}