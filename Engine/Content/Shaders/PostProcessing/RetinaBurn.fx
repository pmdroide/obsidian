// Retina burn (Half-Life: Alyx RetinaBurnFx, Docs/Research/Source2Look phase 5.3): bright lights leave an afterimage.
// A quarter resolution target keeps what was very bright on screen and lets it fade slowly; PostProcessing.fx adds it
// to the exposed image. Ping-ponged by Renderer.UpdateRetinaBurn.

Texture2D FrameTexture;     // HDR frame (before exposure)
Texture2D PreviousBurn;     // last frame's burn (exposed units)
Texture2D AutoExposureTexture;
float AutoExposureEnabled = 0;
float PowExposure = 1;
float Threshold = 2.0f;     // exposed brightness that starts burning in
float Gain = 0.6f;          // how fast it burns in, per second
float FadeSeconds = 1.5f;   // how long an afterimage takes to fade to a third
float DeltaTime = 0.016f;

SamplerState LinearSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = POINT;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VertexShaderOutput VertexShaderFunction(float2 position : POSITION0, uint id : SV_VERTEXID)
{
    VertexShaderOutput output;
    output.Position = float4(position, 0, 1);
    output.TexCoord.x = (float)(id / 2) * 2.0;
    output.TexCoord.y = 1.0 - (float)(id % 2) * 2.0;
    return output;
}

float4 BurnPS(VertexShaderOutput input) : SV_TARGET0
{
    float exposure = PowExposure * exp2(AutoExposureEnabled > 0.5f ? AutoExposureTexture.Load(int3(0, 0, 0)).r : 0);
    float3 frame = max(FrameTexture.SampleLevel(LinearSampler, input.TexCoord, 0).rgb, 0) * exposure;
    float brightness = max(frame.r, max(frame.g, frame.b));
    // The light's colour, washed out a little, above the threshold
    float3 burnIn = brightness > Threshold ? lerp(frame / max(brightness, 1e-4f), 1, 0.35f) * (brightness - Threshold) : 0;

    float3 previous = PreviousBurn.SampleLevel(LinearSampler, input.TexCoord, 0).rgb;
    float fade = exp(-DeltaTime / max(FadeSeconds, 0.01f));
    float3 burn = previous * fade + burnIn * Gain * DeltaTime;
    return float4(min(burn, 4), 1);
}

technique Burn
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_4_0 BurnPS();
    }
}
