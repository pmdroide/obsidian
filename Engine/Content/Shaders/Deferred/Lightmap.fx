//Lightmaps (Source 2 look, phase 4.2): redraws Lightmap Static gameobjects after the G-buffer and, where they are what the
//G-buffer shows (same linear depth), writes their baked light for the lighting passes:
//  COLOR0 (HalfVector4): rgb = indirect irradiance for the G-buffer normal (directional lightmap), a = 1 + baked AO
//                        (cleared to 0 = not lightmapped; DeferredEnvironmentMap.fx uses it instead of the probes and SSAO)
//  COLOR1 (Color):       shadow mask of up to 4 static lights (cleared to 1; the light shaders multiply their channel in)
//Depth is tested in the shader like the deferred decals: the G-buffer's depth buffer isn't kept.

#include "../Common/helper.fx"

float4x4 WorldView;
float4x4 WorldViewProj;
float FarClip = 500;
//View space -> world space for the G-buffer normal
float3x3 TransposeView;

Texture2D DepthMap;
Texture2D NormalMap;

Texture2D IrradianceMap;
Texture2D DirectionMap;
Texture2D MaskMap;

SamplerState LightmapSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = POINT;
};

struct VertexShaderInput
{
    float3 Position : POSITION0;
    float2 Uv : TEXCOORD1;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float2 Uv : TEXCOORD0;
    float Depth : TEXCOORD1;
};

struct PixelShaderOutput
{
    float4 Indirect : COLOR0;
    float4 Mask : COLOR1;
};

VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output;
    float4 position = float4(input.Position, 1);
    output.Position = mul(position, WorldViewProj);
    //Same linear depth as Gbuffer.fx
    output.Depth = mul(position, WorldView).z / -FarClip;
    output.Uv = input.Uv;
    return output;
}

PixelShaderOutput PixelShaderFunction(VertexShaderOutput input)
{
    int3 texel = int3(input.Position.xy, 0);
    float depth = DepthMap.Load(texel).r;
    //Something else is in front (or this surface is hidden): leave the pixel to the probes
    clip(max(input.Depth * 0.002f, 0.00002f) - abs(depth - input.Depth));

    float3 normal = normalize(mul(decode(NormalMap.Load(texel).xyz), TransposeView));

    float4 irradiance = IrradianceMap.Sample(LightmapSampler, input.Uv);
    float4 direction = DirectionMap.Sample(LightmapSampler, input.Uv);
    float3 dir = direction.xyz * 2 - 1;
    dir /= max(length(dir), 0.0001f);
    //The baker divided by this term at the baked normal; the G-buffer normal (normal maps) brings the detail back
    float strength = saturate(direction.w);
    float term = 1 - strength + strength * (dot(normal, dir) * 0.5f + 0.5f);

    PixelShaderOutput output;
    output.Indirect = float4(max(irradiance.rgb, 0) * term, 1 + saturate(irradiance.a));
    output.Mask = MaskMap.Sample(LightmapSampler, input.Uv);
    return output;
}

technique Lightmap
{
    pass Pass1
    {
        VertexShader = compile vs_5_0 VertexShaderFunction();
        PixelShader = compile ps_5_0 PixelShaderFunction();
    }
}
