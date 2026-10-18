// Debug views of the Source 2 look buffers (Renderer.RenderModes IndirectDiffuse / AmbientOcclusion / ShadowCascades),
// drawn full screen by Renderer.DrawDebugChannel. See Docs/markdown/Source2_Look.md.

Texture2D InputTexture;
Texture2D DepthMap; //linear G-buffer depth, view Z / FarClip

float FarClip = 1000;
//View Z where each sun shadow cascade ends (Renderer / ShadowMapRenderModule), unused cascades = FarClip
float4 CascadeEnds = float4(10, 30, 80, 200);
float CascadeCount = 4;
//Brightness of the colour views (indirect light is stored at the light buffers' 1/10 scale)
float Scale = 1.5f;

struct VertexShaderInput
{
    float2 Position : POSITION0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VertexShaderOutput VertexShaderFunction(VertexShaderInput input, uint id : SV_VERTEXID)
{
    VertexShaderOutput output;
    output.Position = float4(input.Position, 0, 1);
    output.TexCoord.x = (float)(id / 2) * 2.0;
    output.TexCoord.y = 1.0 - (float)(id % 2) * 2.0;
    return output;
}

float3 Tonemap(float3 hdr)
{
    hdr *= Scale;
    return pow(saturate(hdr / (1 + hdr)), 1 / 2.2f);
}

float4 ColorPS(VertexShaderOutput input) : SV_TARGET0
{
    return float4(Tonemap(InputTexture.Load(int3(input.Position.xy, 0)).rgb), 1);
}

float4 AlphaPS(VertexShaderOutput input) : SV_TARGET0
{
    float a = saturate(InputTexture.Load(int3(input.Position.xy, 0)).a);
    return float4(a, a, a, 1);
}

//The lit image tinted by the sun shadow cascade each pixel reads: red, green, blue, yellow; beyond the last one grey
float4 CascadePS(VertexShaderOutput input) : SV_TARGET0
{
    int3 pixel = int3(input.Position.xy, 0);
    float3 color = Tonemap(InputTexture.Load(pixel).rgb * 0.1f);
    float viewZ = DepthMap.Load(pixel).r * FarClip;

    float3 tint = float3(0.5f, 0.5f, 0.5f);
    if (viewZ < CascadeEnds.x && CascadeCount > 0) tint = float3(1, 0.25f, 0.25f);
    else if (viewZ < CascadeEnds.y && CascadeCount > 1) tint = float3(0.25f, 1, 0.25f);
    else if (viewZ < CascadeEnds.z && CascadeCount > 2) tint = float3(0.3f, 0.45f, 1);
    else if (viewZ < CascadeEnds.w && CascadeCount > 3) tint = float3(1, 1, 0.25f);

    float luma = dot(color, float3(0.3f, 0.59f, 0.11f));
    return float4(lerp(luma.xxx, tint, 0.55f) * (0.35f + luma), 1);
}

technique Color
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_4_0 ColorPS();
    }
}

technique Alpha
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_4_0 AlphaPS();
    }
}

technique Cascades
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_4_0 CascadePS();
    }
}
