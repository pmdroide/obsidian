// Clustered Volumetric Fog (Froxels) Shader - MINIMAL VERSION
// Divides view frustum into 3D grid for efficient light clustering
// Copyright (c) 2026 Pedro Mano. Licensed under MIT.

#include "../Common/helper.fx"

// Grid dimensions
float4x4 View;
float4x4 InverseProjection;
float4x4 InverseView;
float NearClip;
float FarClip;
float3 GridDimensions; // X, Y, Z grid size
float2 FroxelSize; // Size of each froxel in view space
float2 ScreenResolution;

// Textures
Texture2D AlbedoMap;
Texture2D NormalMap;
Texture2D DepthMap;

float FroxelDensity = 1.0f;
float FroxelScatter = 1.0f;
float FroxelAbsorption = 1.0f;

float3 DirectionalLightDirectionVS; // MUST be normalized, in view space
float3 DirectionalLightColor;
bool UseDirectionalLight;
bool UseFroxelFog;

Texture2D ShadowMap;
Texture2D FroxelMeta;
Texture2D FroxelLightList;

float3 LightPositions[128];
float3 LightColors[128];

float4x4 LightViewProjection;

SamplerState PointSampler
{
    Texture = (AlbedoMap);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = POINT;
    MinFilter = POINT;
    Mipfilter = POINT;
};

struct VertexShaderInput
{
    float4 Position : POSITION0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float4 ScreenPosition : TEXCOORD0;
};

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

// Pixel shader for building froxel clusters - MINIMAL VERSION
float4 PixelShaderBuildFroxels(VertexShaderOutput input) : COLOR0
{
    return float4(1, 0, 1, 1); // bright purple
}

// Pixel shader for compositing froxel lighting - MINIMAL VERSION
float4 PixelShaderComposeFroxels(VertexShaderOutput input) : COLOR0
{
    return float4(1, 0, 1, 1); // bright purple for debugging
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
