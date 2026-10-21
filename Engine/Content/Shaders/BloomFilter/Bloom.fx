//Bloomfilter 2016, Thekosmonaut

Texture2D ScreenTexture;
SamplerState u_texture
{
    Texture = <ScreenTexture>; 

	MagFilter = LINEAR;
	MinFilter = LINEAR;
	Mipfilter = LINEAR;

	AddressU = CLAMP;
	AddressV = CLAMP;
};

float2 InverseResolution;
float Threshold = 0.8f;
float Radius = 1.0f;
float Strength = 1.0f;
float StreakLength = 1;

//Source 2 look (phase 1.3): bloom starts at an exposed brightness (mat_tonemap_bloom_start_value) with a soft knee.
//The exposure is the one PostProcessing.fx applies: PowExposure times the adapted EV of auto exposure (last frame's).
float BloomStart = 1.976f;
float BloomKnee = 2.3655f;
//0 = soft knee on the brightest channel, 1 = Source 2 ramp (gaussian_bloom_blur.vfx first mip, Source2Look R3)
float BloomShape = 1;
float PowExposure = 1;
float UseAutoExposure = 0;
Texture2D ExposureTexture;

float CurrentExposure()
{
	float ev = 0;
	[branch]
	if (UseAutoExposure > 0.5f)
		ev = ExposureTexture.Load(int3(0, 0, 0)).r;
	return PowExposure * exp2(ev);
}

//Share of a colour above the bloom start, with a quadratic knee below it
float BloomContribution(float3 color)
{
	float exposure = CurrentExposure();
	float knee = max(BloomKnee, 1e-4f);
	//HLA: weight = saturate((dot(c, (0.3, 0.59, 0.11)) * exposure - threshold) / width), applied to the whole colour
	float ramp = saturate((dot(color, float3(0.3f, 0.59f, 0.11f)) * exposure - BloomStart) / knee);
	float brightness = max(color.r, max(color.g, color.b)) * exposure;
	float soft = clamp(brightness - BloomStart + knee, 0, 2 * knee);
	soft = soft * soft / (4 * knee);
	float softKnee = max(soft, brightness - BloomStart) / max(brightness, 1e-4f);
	return BloomShape > 0.5f ? ramp : softKnee;
}

struct VertexShaderInput
{
	float2 Position : POSITION0;
};

struct VertexShaderOutput
{
	float4 Position : POSITION0;
	float2 TexCoord : TEXCOORD0;
}; 

VertexShaderOutput VertexShaderFunction(VertexShaderInput input, uint id:SV_VERTEXID)
{
	VertexShaderOutput output;
	output.Position = float4(input.Position, 0, 1);
	output.TexCoord.x = (float)(id / 2) * 2.0;
	output.TexCoord.y = 1.0 - (float)(id % 2) * 2.0;

	return output;
}

float4 Box4(float4 p0, float4 p1, float4 p2, float4 p3)
{
	return (p0 + p1 + p2 + p3) * 0.25f;
}

float4 ExtractPS(float4 pos : SV_POSITION,  float2 texCoord : TEXCOORD0) : SV_TARGET0
{
	float4 color = ScreenTexture.Sample(u_texture, texCoord);
	return float4(color.rgb * BloomContribution(color.rgb), 0);
}

float GetLuma(float3 rgb)
{
	return (0.299 * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b);
}

float4 ExtractLuminancePS(float4 pos : SV_POSITION,  float2 texCoord : TEXCOORD0) : SV_TARGET0
{
    float4 color = ScreenTexture.Sample(u_texture, texCoord);
	float3 bloom = max(color.rgb, 0) * BloomContribution(max(color.rgb, 0));
	return float4(bloom, GetLuma(bloom));
}

float4 DownsamplePS(float4 pos : SV_POSITION,  float2 texCoord : TEXCOORD0) : SV_TARGET0
{
    float2 offset = float2(StreakLength * InverseResolution.x, 1 * InverseResolution.y);
        
    float4 c0 = ScreenTexture.Sample(u_texture, texCoord + float2(-2, -2) * offset);
    float4 c1 = ScreenTexture.Sample(u_texture, texCoord + float2(0,-2)*offset);
    float4 c2 = ScreenTexture.Sample(u_texture, texCoord + float2(2, -2) * offset);
    float4 c3 = ScreenTexture.Sample(u_texture, texCoord + float2(-1, -1) * offset);
    float4 c4 = ScreenTexture.Sample(u_texture, texCoord + float2(1, -1) * offset);
    float4 c5 = ScreenTexture.Sample(u_texture, texCoord + float2(-2, 0) * offset);
    float4 c6 = ScreenTexture.Sample(u_texture, texCoord);
    float4 c7 = ScreenTexture.Sample(u_texture, texCoord + float2(2, 0) * offset);
    float4 c8 = ScreenTexture.Sample(u_texture, texCoord + float2(-1, 1) * offset);
    float4 c9 = ScreenTexture.Sample(u_texture, texCoord + float2(1, 1) * offset);
    float4 c10 = ScreenTexture.Sample(u_texture, texCoord + float2(-2, 2) * offset);
    float4 c11 = ScreenTexture.Sample(u_texture, texCoord + float2(0, 2) * offset);
    float4 c12 = ScreenTexture.Sample(u_texture, texCoord + float2(2, 2) * offset);

    return Box4(c0, c1, c5, c6) * 0.125f +
    Box4(c1, c2, c6, c7) * 0.125f +
    Box4(c5, c6, c10, c11) * 0.125f +
    Box4(c6, c7, c11, c12) * 0.125f +
    Box4(c3, c4, c8, c9) * 0.5f;
}

float4 UpsamplePS(float4 pos : SV_POSITION,  float2 texCoord : TEXCOORD0) : SV_TARGET0
{
    float2 offset = float2(StreakLength * InverseResolution.x, 1 * InverseResolution.y) * Radius;

    float4 c0 = ScreenTexture.Sample(u_texture, texCoord + float2(-1, -1) * offset);
    float4 c1 = ScreenTexture.Sample(u_texture, texCoord + float2(0, -1) * offset);
    float4 c2 = ScreenTexture.Sample(u_texture, texCoord + float2(1, -1) * offset);
    float4 c3 = ScreenTexture.Sample(u_texture, texCoord + float2(-1, 0) * offset);
    float4 c4 = ScreenTexture.Sample(u_texture, texCoord);
    float4 c5 = ScreenTexture.Sample(u_texture, texCoord + float2(1, 0) * offset);
    float4 c6 = ScreenTexture.Sample(u_texture, texCoord + float2(-1,1) * offset);
    float4 c7 = ScreenTexture.Sample(u_texture, texCoord + float2(0, 1) * offset);
    float4 c8 = ScreenTexture.Sample(u_texture, texCoord + float2(1, 1) * offset);

    //Tentfilter  0.0625f    
    return 0.0625f * (c0 + 2 * c1 + c2 + 2 * c3 + 4 * c4 + 2 * c5 + c6 + 2 * c7 + c8) * Strength; //+ 0.5f * ScreenTexture.Sample(c_texture, texCoord);

}

//-------------------------- TECHNIQUES ----------------------------------------

technique Extract
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_4_0 ExtractPS();
	}
}

technique ExtractLuminance
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_4_0 ExtractLuminancePS();
	}
}

technique Downsample
{
    pass Pass1
    {
		VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_4_0 DownsamplePS();
    }
}

technique Upsample
{
    pass Pass1
    {
		VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_4_0 UpsamplePS();
    }
}
