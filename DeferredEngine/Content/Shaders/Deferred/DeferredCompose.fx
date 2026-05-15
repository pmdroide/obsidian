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

float2 FroxelScreenUVToAtlasUV(int fx, int fy, int fz)
{
    float nx = GridDimensions.x;
    float ny = GridDimensions.y;
    float nz = GridDimensions.z;
    return float2((fx + 0.5) / nx, (fz * ny + fy + 0.5) / (ny * nz));
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

    float2 uv0 = FroxelScreenUVToAtlasUV(fx, fy, fz0);
    float2 uv1 = FroxelScreenUVToAtlasUV(fx, fy, fz1);

    float4 sample0 = FroxelAccumulationTexture.Sample(linearSampler, uv0);
    float4 sample1 = FroxelAccumulationTexture.Sample(linearSampler, uv1);

    return lerp(sample0, sample1, zFrac);
}

float4 SampleFroxelBilateral(float2 screenUV, float3 centerNormal, float centerDepth)
{
    const float kernel3[3] = { 0.27901, 0.44198, 0.27901 };
    float2 texel = 1.0 / ScreenResolution;

    float4 result = 0;
    float weightSum = 0;

    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            float2 sampleUV = clamp(screenUV + float2(x, y) * texel, 0.0, 1.0);
            float4 depthSample = DepthMap.SampleLevel(pointSampler, sampleUV, 0);
            float sampleDepth = saturate(depthSample.r) * FarClip;
            sampleDepth = max(sampleDepth, NearClip);

            float4 normalSample = normalMap.SampleLevel(pointSampler, sampleUV, 0);
            float3 sampleNormal = decode(normalSample.xyz);

            float depthWeight = exp(-abs(sampleDepth - centerDepth) * 0.075);
            float normalWeight = pow(saturate(dot(centerNormal, sampleNormal)), 32.0);
            float spatialWeight = kernel3[x + 1] * kernel3[y + 1];

            float4 fogSample = SampleFroxelAccumulationTexture(sampleUV, sampleDepth);

            float weight = spatialWeight * depthWeight * normalWeight + 1e-4;
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

    float3 centerNormal =
        decode(normalInfo.xyz);

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

        float tDepth =
            saturate(
                log(dist / NearClip) /
                log(FarClip / NearClip));

        float4 fog = SampleFroxelBilateral(input.TexCoord, centerNormal, dist);

        volumetrics =
            fog.rgb;

        transmittance =
            saturate(fog.a);
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