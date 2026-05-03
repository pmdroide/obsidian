// Clustered Volumetric Fog (Froxels) Shader
// Divides view frustum into 3D grid for efficient light clustering
// Copyright (c) 2026 Pedro Mano. Licensed under MIT.

#include "../Common/helper.fx"

// Grid dimensions
float4x4 View;
float4x4 InverseProjection;
float NearClip;
float FarClip;
float3 GridDimensions; // X, Y, Z grid size
float3 FroxelSize; // Size of each froxel in view space
float2 ScreenResolution;

// Textures
Texture2D AlbedoMap;
Texture2D NormalMap;
Texture2D DepthMap;

float3 LightDirection;
float3 LightColor;
float G = 0.75f; // anisotropy

int NumSteps = 64;        // e.g. 32–64
float StepSize;      // in view space (or derive from near/far)

float3 LightPositionVS;

SamplerState PointSampler
{
    Texture = (AlbedoMap);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = POINT;
    MinFilter = POINT;
    Mipfilter = POINT;
};

// Input/Output structures
struct VertexShaderInput
{
    float4 Position : POSITION0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float4 ScreenPosition : TEXCOORD0;
};

struct PixelShaderInput
{
    float2 TexCoord : TEXCOORD0;
    float3 PositionVS : TEXCOORD1;
};

// Helper function to reconstruct view space position from depth
float3 ReconstructViewSpacePosition(float2 texCoord, float depth)
{
    // Convert to NDC space [-1, 1]
    float3 posNDC = float3(
        2.0f * texCoord.x - 1.0f,
        1.0f - 2.0f * texCoord.y,
        depth
    );
    
    // Transform from NDC to view space
    float4 posVS = mul(float4(posNDC, 1.0f), InverseProjection);
    return posVS.xyz / posVS.w;
}

// Convert view space position to froxel grid coordinates
int3 GetFroxelCoordinates(float3 posVS)
{
    float z = -posVS.z;

    float zSlice = log(z / NearClip) / log(FarClip / NearClip);
    zSlice = saturate(zSlice);
    
    // Calculate grid indices
    int3 froxelCoord = int3(
        (int)((posVS.x / posVS.z + 1.0f) * 0.5f * GridDimensions.x),
        (int)(((-posVS.y / posVS.z + 1.0f) * 0.5f) * GridDimensions.y),
        (int)(zSlice * GridDimensions.z)
    );
    
    // Clamp to grid bounds
    froxelCoord = clamp(froxelCoord, int3(0, 0, 0), int3(GridDimensions) - int3(1, 1, 1));
    
    return froxelCoord;
}

// Vertex shader for full screen pass
VertexShaderOutput VertexShaderBuildFroxels(VertexShaderInput input)
{
    VertexShaderOutput output;
    output.Position = input.Position;
    output.ScreenPosition = input.Position;
    return output;
}

VertexShaderOutput VertexShaderComposeFroxels(VertexShaderInput input)
{
    VertexShaderOutput output;
    output.Position = input.Position;
    output.ScreenPosition = input.Position;
    return output;
}

float HenyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 / (4.0 * 3.14159)) * ((1.0 - g2) / pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
}

// Pixel shader for building froxel clusters
float4 PixelShaderBuildFroxels(VertexShaderOutput input) : COLOR0
{
    float2 texCoord = 0.5f * (float2(input.ScreenPosition.x, -input.ScreenPosition.y) + 1);
    int3 pixelCoord = int3(input.ScreenPosition.xy, 0);
    
    // Sample depth
    float depth = DepthMap.Load(pixelCoord).r;
    
    // Reconstruct view space position
    float3 posVS = ReconstructViewSpacePosition(texCoord, depth);
    
    // Get froxel coordinates
    int3 froxelCoord = GetFroxelCoordinates(posVS);
    
    // Output froxel coordinates as color for now (for debugging)
    // In a full implementation, this would output light indices
    float3 normalized = float3(
        froxelCoord.x / GridDimensions.x,
        froxelCoord.y / GridDimensions.y,
        froxelCoord.z / GridDimensions.z
    );
    
    return float4(normalized, 1.0f);
}

// Pixel shader for compositing froxel lighting
float4 PixelShaderComposeFroxels(VertexShaderOutput input) : COLOR0
{
    float2 texCoord = 0.5f * (float2(input.ScreenPosition.x, -input.ScreenPosition.y) + 1);
    int3 pixelCoord = int3(input.ScreenPosition.xy, 0);

    float depth = DepthMap.Load(pixelCoord).r;
    float4 albedo = AlbedoMap.Load(pixelCoord);

    // Reconstruct view space position
    float3 endPosVS = ReconstructViewSpacePosition(texCoord, depth);

    // Ray setup
    float3 rayOrigin = float3(0, 0, 0); // camera in view space
    float3 rayDir = normalize(endPosVS);

    float rayLength = length(endPosVS);
    float stepSize = rayLength / NumSteps;

    float3 accumulatedLight = float3(0, 0, 0);
    float transmittance = 1.0;

    // Raymarch
    for (int i = 0; i < NumSteps; i++)
    {
        float t = (i + 0.5f) * stepSize;
        float3 samplePos = rayOrigin + rayDir * t;

        float z = -samplePos.z;
        if (z < NearClip || z > FarClip)
            continue;

        // Density (simple exponential fog)
        float density = 0.0008f;

        float3 toLight = LightPositionVS - samplePos;
        float distanceToLight = max(length(toLight), 0.001f);
        float3 lightDir = toLight / distanceToLight;

        float attenuation = 1.0 / (1.0 + distanceToLight * distanceToLight * 0.01);

        // Phase function
        float cosTheta = dot(rayDir, lightDir);
        float phase = HenyeyGreenstein(cosTheta, G);

        // Light contribution
        float scatteringStrength = 3.0f;

        float3 scattering = LightColor * phase * attenuation * density * scatteringStrength;

        // Accumulate using Beer-Lambert
        accumulatedLight += transmittance * scattering * stepSize;

        // Attenuate transmittance
        transmittance *= exp(-density * stepSize);

        // Early exit (performance)
        if (transmittance < 0.01f)
            break;
    }

    // Combine with scene
    return float4(accumulatedLight, transmittance);
}

// Techniques
technique BuildFroxels
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderBuildFroxels();
        PixelShader = compile ps_4_0 PixelShaderBuildFroxels();
    }
};

technique ComposeFroxels
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderComposeFroxels();
        PixelShader = compile ps_4_0 PixelShaderComposeFroxels();
    }
};
