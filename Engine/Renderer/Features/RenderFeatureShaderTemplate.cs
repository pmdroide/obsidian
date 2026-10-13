namespace Engine.Renderer.Features
{
    /// <summary>The .fx a new render feature starts from (Anvil > Render Pipeline > + New).</summary>
    internal static class RenderFeatureShaderTemplate
    {
        public static string For(string featureName) => Text.Replace("{NAME}", featureName ?? "Render Feature");

        private const string Text =
@"// {NAME} - a render feature (Anvil > Render Pipeline).
// A full-screen pass drawn at the feature's injection point. Save this file and the engine recompiles
// it within a few seconds; errors show in the Render Pipeline panel and the console, and the last good
// version keeps running meanwhile.
//
// The engine sets these parameters when the shader declares them (unused ones are compiled out):
//   ScreenTexture                    the image so far: linear HDR before Post Processing, the final
//                                    tonemapped LDR image at After Post Processing
//   DepthMap                         linear depth = view distance along the camera axis / FarClip (1 = sky)
//   NormalMap                        G-buffer normals, view space, spheremap encoded (DecodeNormal below)
//   AlbedoMap                        G-buffer base colour
//   Resolution, InverseResolution    target size in pixels, and 1 / size
//   Time, DeltaTime                  game time in seconds
//   Intensity, Color, Params         the feature's Inspector values (Params is free for your own use)
//   CameraPosition, FarClip, View, Projection, ViewProjection, InverseViewProjection   (Z is up)
// The feature's Blend decides how the output lands: Replace, Alpha Blend, Additive or Multiply.
// Every pass of the technique (Technique field, empty = the first one) is drawn in order.

Texture2D ScreenTexture;
Texture2D DepthMap;
Texture2D NormalMap;

float2 Resolution;
float2 InverseResolution;
float Time;
float Intensity = 1;
float4 Color = float4(1, 1, 1, 1);
float4 Params;

float3 CameraPosition;
float FarClip;
float4x4 InverseViewProjection;

SamplerState LinearSampler
{
    Texture = <ScreenTexture>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    MipFilter = POINT;
};

SamplerState PointSampler
{
    Texture = <DepthMap>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = POINT;
    MinFilter = POINT;
    MipFilter = POINT;
};

struct VertexInput
{
    float2 Position : POSITION0;
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VertexOutput MainVS(VertexInput input)
{
    VertexOutput output;
    output.Position = float4(input.Position, 0, 1);
    output.TexCoord = input.Position * float2(0.5f, -0.5f) + 0.5f;
    return output;
}

// World position of the pixel at uv from its DepthMap value
float3 WorldPosition(float2 uv, float depth)
{
    float2 clip = float2(uv.x * 2 - 1, 1 - uv.y * 2);
    float4 farPoint = mul(float4(clip, 1, 1), InverseViewProjection);
    return CameraPosition + (farPoint.xyz / farPoint.w - CameraPosition) * depth;
}

// View-space normal from a NormalMap sample (.rg)
float3 DecodeNormal(float2 encoded)
{
    float2 fenc = encoded * 4 - 2;
    float f = dot(fenc, fenc);
    float g = sqrt(1 - f / 4);
    return float3(fenc * g, 1 - f / 2);
}

float4 MainPS(VertexOutput input) : SV_TARGET
{
    float4 color = ScreenTexture.Sample(LinearSampler, input.TexCoord);

    // Example: tint the image by Color and darken the corners. Replace it with your effect.
    float2 offset = input.TexCoord - 0.5f;
    float vignette = saturate(1 - dot(offset, offset) * 2.2f);
    float3 result = color.rgb * Color.rgb * lerp(1, vignette, 0.8f);

    return float4(lerp(color.rgb, result, saturate(Intensity)), 1);
}

technique Main
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVS();
        PixelShader = compile ps_5_0 MainPS();
    }
}
";
    }
}
