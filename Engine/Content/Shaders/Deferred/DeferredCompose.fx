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

// FINAL accumulated froxel fog texture
Texture2D FroxelAccumulationTexture;

////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CAMERA / FROXELS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

float NearClip;
float FarClip;

float3 GridDimensions;
float2 ScreenResolution;

bool useSSAO = true;
bool UseFroxelFog = true;

// 0..1 — how much fog impacts sky pixels. Defaults are tuned so the skybox shows through; raise
// for moodier atmospheric distance haze. Independent from the per-light scatter values.
float SkyFogStrength = 0.25f;

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

// Continuous-coord atlas UV. Within slice fz the V coord stays in [fz/nz, (fz+1)/nz] as long
// as fyF is in [0, ny-1], so the hardware LINEAR sampler does bilinear XY interpolation
// without bleeding across Z slices.
float2 FroxelContinuousToAtlasUV(float fxF, float fyF, int fz)
{
    float nx = GridDimensions.x;
    float ny = GridDimensions.y;
    float nz = GridDimensions.z;
    return float2((fxF + 0.5) / nx, ((float)fz * ny + fyF + 0.5) / (ny * nz));
}

float4 SampleFroxelAccumulationTexture(float2 screenUV, float depth)
{
    float nx = GridDimensions.x;
    float ny = GridDimensions.y;
    int nzI = (int)GridDimensions.z;

    // Snap to texel centers (-0.5) then clamp - this is what feeds the LINEAR sampler so the
    // ~8 screen-px per froxel get bilinearly smoothed instead of point-sampled (pixelated).
    float fxF = clamp(screenUV.x * nx - 0.5, 0.0, nx - 1.0);
    float fyF = clamp(screenUV.y * ny - 0.5, 0.0, ny - 1.0);

    float normalizedDepth = max(depth, NearClip);
    float slicePos = saturate(log(normalizedDepth / NearClip) / log(FarClip / NearClip)) * GridDimensions.z;
    int fz0 = clamp((int)floor(slicePos), 0, nzI - 1);
    int fz1 = min(fz0 + 1, nzI - 1);
    float zFrac = frac(slicePos);

    float2 uv0 = FroxelContinuousToAtlasUV(fxF, fyF, fz0);
    float2 uv1 = FroxelContinuousToAtlasUV(fxF, fyF, fz1);

    float4 sample0 = FroxelAccumulationTexture.Sample(linearSampler, uv0);
    float4 sample1 = FroxelAccumulationTexture.Sample(linearSampler, uv1);

    return lerp(sample0, sample1, zFrac);
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
            float sampleDepth = saturate(depthSample.r) * FarClip;
            sampleDepth = max(sampleDepth, NearClip);

            float4 normalSample = normalMap.SampleLevel(pointSampler, sampleUV, 0);
            float3 sampleNormal = decode(normalSample.xyz);

            // Slightly relax weights so wider taps still contribute - we'd rather have soft fog
            // than sharp 8x8 froxel blocks bleeding through.
            float depthWeight = exp(-abs(sampleDepth - centerDepth) * 0.04);
            float normalWeight = pow(saturate(dot(centerNormal, sampleNormal)), 8.0);
            float spatialWeight = kernel5[x + 2] * kernel5[y + 2];

            float4 fogSample = SampleFroxelAccumulationTexture(sampleUV, sampleDepth);

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

        // G-buffer stores view-space Z / (-FarClip); recover scene distance along forward axis.
        float dist =
            saturate(depthSample) *
            FarClip;

        dist =
            max(dist, NearClip);

        // Skip bilateral filtering for sky pixels - their cleared normal data breaks the normal weighting.
        float4 fog = isSky
            ? SampleFroxelAccumulationTexture(input.TexCoord, dist)
            : SampleFroxelBilateral(input.TexCoord, centerNormal, dist);

        volumetrics =
            fog.rgb;

        transmittance =
            saturate(fog.a);

        // For sky pixels, dial fog impact down so the skybox doesn't get washed out by accumulated
        // scatter from the entire view-frustum depth. SkyFogStrength = 0 -> sky untouched.
        if (isSky)
        {
            volumetrics *= SkyFogStrength;
            transmittance = lerp(1.0, transmittance, SkyFogStrength);
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