// Procedural water: world-space ripples, dielectric Fresnel, and environment reflections.
float4x4 World;
float4x4 WorldViewProj;
float4x4 WorldInverseTranspose;
float3 CameraPositionWS;
float Time;
float3 SurfaceColor;
float Roughness;
float Opacity;
float WaveScale;
float WaveSpeed;
float WaveStrength;
float3 LightDirection;
float3 LightColor;
bool HasEnvironment;
TextureCube EnvironmentMap;
SamplerState EnvironmentSampler
{
    Texture = <EnvironmentMap>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    MipFilter = LINEAR;
};

struct VSInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
};
struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 PositionWS : TEXCOORD0;
    float3 NormalWS : TEXCOORD1;
};

VSOutput WaterVS(VSInput input)
{
    VSOutput output;
    output.Position = mul(input.Position, WorldViewProj);
    output.PositionWS = mul(input.Position, World).xyz;
    output.NormalWS = mul(float4(input.Normal, 0), WorldInverseTranspose).xyz;
    return output;
}

float4 WaterPS(VSOutput input) : SV_TARGET
{
    float3 baseNormal = normalize(input.NormalWS);
    float3 view = normalize(CameraPositionWS - input.PositionWS);
    // Two-sided shading is useful for a thin water surface viewed from below.
    if (dot(baseNormal, view) < 0) baseNormal = -baseNormal;
    float2 p = input.PositionWS.xy * WaveScale;
    float t = Time * WaveSpeed;
    float2 slope = float2(cos(dot(p, float2(1, 0.35)) + t),
                         cos(dot(p, float2(-0.45, 1.3)) - t * 0.8));
    slope += 0.35 * float2(cos(p.x * 2.7 + p.y + t * 1.4),
                          cos(p.y * 3.1 - p.x - t * 1.1));
    float3 ripple = float3(-slope * WaveStrength, 0);
    // Project the perturbation onto the mesh's tangent plane; Z is up in this engine.
    float3 normal = normalize(baseNormal + ripple - baseNormal * dot(ripple, baseNormal));
    float fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(normal, view)), 5);
    float3 reflectedDirection = reflect(-view, normal);
    float3 reflection = lerp(float3(0.025, 0.04, 0.055), float3(0.12, 0.2, 0.3),
                             saturate(reflectedDirection.z * 0.5 + 0.5));
    if (HasEnvironment)
        reflection = EnvironmentMap.SampleLevel(EnvironmentSampler, reflectedDirection, Roughness * 5).rgb;
    float3 halfVector = normalize(view + LightDirection);
    float specular = pow(saturate(dot(normal, halfVector)), lerp(256, 8, Roughness));
    float3 body = pow(abs(SurfaceColor), 2.2) * (0.08 + LightColor * saturate(dot(normal, LightDirection)) * 0.25);
    float3 color = lerp(body, reflection, fresnel) + LightColor * specular * 0.1;
    // Opacity zero fully hides water; grazing views become more opaque otherwise.
    return float4(color, Opacity * lerp(1, 1 / max(Opacity, 0.0001), fresnel));
}

technique Water
{
    pass Pass0
    {
        VertexShader = compile vs_5_0 WaterVS();
        PixelShader = compile ps_5_0 WaterPS();
    }
}
