// FXAA (after Timothy Lottes' FXAA 3.11, quality path, simplified): the "no TAA" anti-aliasing of the Source 2 look
// (Docs/Research/Source2Look phase 1.6). Runs on the final LDR (sRGB) image when TAA is off and GameSettings.g_fxaa is on.

Texture2D ScreenTexture;
float2 InverseResolution = float2(1.0f / 1280, 1.0f / 720);

// Lottes' defaults: subpixel aliasing removal, edge threshold, darkness threshold
float SubpixelQuality = 0.75f;
float EdgeThreshold = 0.125f;
float EdgeThresholdMin = 0.0625f;

SamplerState LinearSampler
{
    Texture = (ScreenTexture);
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

float Luma(float3 rgb)
{
    return dot(rgb, float3(0.299f, 0.587f, 0.114f));
}

float LumaAt(float2 uv)
{
    return Luma(ScreenTexture.SampleLevel(LinearSampler, uv, 0).rgb);
}

#define FXAA_STEPS 10
static const float StepScale[FXAA_STEPS] = { 1, 1, 1, 1, 1.5, 2, 2, 2, 4, 8 };

float4 FxaaPS(VertexShaderOutput input) : SV_TARGET0
{
    float2 uv = input.TexCoord;
    float3 colorCenter = ScreenTexture.SampleLevel(LinearSampler, uv, 0).rgb;
    float lumaCenter = Luma(colorCenter);

    float2 px = InverseResolution;
    float lumaDown = LumaAt(uv + float2(0, px.y));
    float lumaUp = LumaAt(uv - float2(0, px.y));
    float lumaLeft = LumaAt(uv - float2(px.x, 0));
    float lumaRight = LumaAt(uv + float2(px.x, 0));

    float lumaMin = min(lumaCenter, min(min(lumaDown, lumaUp), min(lumaLeft, lumaRight)));
    float lumaMax = max(lumaCenter, max(max(lumaDown, lumaUp), max(lumaLeft, lumaRight)));
    float lumaRange = lumaMax - lumaMin;

    // Not an edge
    if (lumaRange < max(EdgeThresholdMin, lumaMax * EdgeThreshold))
        return float4(colorCenter, 1);

    float lumaDownLeft = LumaAt(uv + float2(-px.x, px.y));
    float lumaUpRight = LumaAt(uv + float2(px.x, -px.y));
    float lumaUpLeft = LumaAt(uv - px);
    float lumaDownRight = LumaAt(uv + px);

    float lumaDownUp = lumaDown + lumaUp;
    float lumaLeftRight = lumaLeft + lumaRight;
    float lumaLeftCorners = lumaDownLeft + lumaUpLeft;
    float lumaDownCorners = lumaDownLeft + lumaDownRight;
    float lumaRightCorners = lumaDownRight + lumaUpRight;
    float lumaUpCorners = lumaUpRight + lumaUpLeft;

    float edgeHorizontal = abs(-2 * lumaLeft + lumaLeftCorners) + abs(-2 * lumaCenter + lumaDownUp) * 2 + abs(-2 * lumaRight + lumaRightCorners);
    float edgeVertical = abs(-2 * lumaUp + lumaUpCorners) + abs(-2 * lumaCenter + lumaLeftRight) * 2 + abs(-2 * lumaDown + lumaDownCorners);
    bool isHorizontal = edgeHorizontal >= edgeVertical;

    float luma1 = isHorizontal ? lumaUp : lumaLeft;
    float luma2 = isHorizontal ? lumaDown : lumaRight;
    float gradient1 = luma1 - lumaCenter;
    float gradient2 = luma2 - lumaCenter;
    bool is1Steepest = abs(gradient1) >= abs(gradient2);
    float gradientScaled = 0.25f * max(abs(gradient1), abs(gradient2));

    float stepLength = isHorizontal ? px.y : px.x;
    float lumaLocalAverage;
    if (is1Steepest)
    {
        stepLength = -stepLength;
        lumaLocalAverage = 0.5f * (luma1 + lumaCenter);
    }
    else
    {
        lumaLocalAverage = 0.5f * (luma2 + lumaCenter);
    }

    float2 currentUv = uv;
    if (isHorizontal) currentUv.y += stepLength * 0.5f;
    else currentUv.x += stepLength * 0.5f;

    float2 offset = isHorizontal ? float2(px.x, 0) : float2(0, px.y);
    float2 uv1 = currentUv - offset;
    float2 uv2 = currentUv + offset;
    float lumaEnd1 = LumaAt(uv1) - lumaLocalAverage;
    float lumaEnd2 = LumaAt(uv2) - lumaLocalAverage;
    bool reached1 = abs(lumaEnd1) >= gradientScaled;
    bool reached2 = abs(lumaEnd2) >= gradientScaled;

    [loop]
    for (int i = 1; i < FXAA_STEPS && !(reached1 && reached2); i++)
    {
        if (!reached1)
        {
            uv1 -= offset * StepScale[i];
            lumaEnd1 = LumaAt(uv1) - lumaLocalAverage;
            reached1 = abs(lumaEnd1) >= gradientScaled;
        }
        if (!reached2)
        {
            uv2 += offset * StepScale[i];
            lumaEnd2 = LumaAt(uv2) - lumaLocalAverage;
            reached2 = abs(lumaEnd2) >= gradientScaled;
        }
    }

    float distance1 = isHorizontal ? uv.x - uv1.x : uv.y - uv1.y;
    float distance2 = isHorizontal ? uv2.x - uv.x : uv2.y - uv.y;
    bool isDirection1 = distance1 < distance2;
    float distanceFinal = min(distance1, distance2);
    float edgeThickness = distance1 + distance2;
    float pixelOffset = -distanceFinal / edgeThickness + 0.5f;

    bool isLumaCenterSmaller = lumaCenter < lumaLocalAverage;
    bool correctVariation = ((isDirection1 ? lumaEnd1 : lumaEnd2) < 0) != isLumaCenterSmaller;
    float finalOffset = correctVariation ? pixelOffset : 0;

    // Sub-pixel aliasing (single bright/dark pixels)
    float lumaAverage = (1.0f / 12.0f) * (2 * (lumaDownUp + lumaLeftRight) + lumaLeftCorners + lumaRightCorners);
    float subPixelOffset1 = saturate(abs(lumaAverage - lumaCenter) / lumaRange);
    float subPixelOffset2 = (-2 * subPixelOffset1 + 3) * subPixelOffset1 * subPixelOffset1;
    float subPixelOffsetFinal = subPixelOffset2 * subPixelOffset2 * SubpixelQuality;
    finalOffset = max(finalOffset, subPixelOffsetFinal);

    float2 finalUv = uv;
    if (isHorizontal) finalUv.y += finalOffset * stepLength;
    else finalUv.x += finalOffset * stepLength;
    return float4(ScreenTexture.SampleLevel(LinearSampler, finalUv, 0).rgb, 1);
}

technique Fxaa
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_5_0 FxaaPS();
    }
}
