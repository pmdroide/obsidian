// Eye adaptation / auto exposure
// 1. MeterLuminance: HDR frame -> 256x256 center-weighted log2 luminance (R = w * log2(L), G = w)
// 2. Downsample:     4x4 box reduction, 256 -> 64 -> 16 -> 4 -> 1, so the last texel is the weighted average
// 3. Adapt:          moves last frame's exposure (EV, 1x1) toward the metered target over time
// PostProcessing.fx multiplies its exposure by exp2 of the adapted EV.

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VARIABLES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

Texture2D InputTexture;
Texture2D PreviousExposureTexture;

SamplerState LinearSampler
{
	Texture = (InputTexture);
	AddressU = CLAMP;
	AddressV = CLAMP;
	MagFilter = LINEAR;
	MinFilter = LINEAR;
	Mipfilter = POINT;
};

// Size of the render target being written by MeterLuminance.
float2 OutputSize = float2(256, 256);

// 0 = average the whole frame equally, 1 = strongly favour the screen center.
float CenterWeight = 0.5f;

// The average scene luminance is exposed to this value (0.18 = middle grey).
float KeyValue = 0.18f;

// Clamp of the automatic exposure in EV (stops).
float MinExposure = -4;
float MaxExposure = 4;

// Adaptation speed per second when the scene gets brighter / darker.
float SpeedDarkToLight = 3;
float SpeedLightToDark = 1;

float DeltaTime = 0.016f;

// 1 snaps to the target (first frame / after re-enabling), 0 adapts over time.
float Reset = 1;

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  STRUCTS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct VertexShaderInput
{
	float2 Position : POSITION0;
};

struct VertexShaderOutput
{
	float4 Position : POSITION0;
	float2 TexCoord : TEXCOORD0;
};

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VERTEX SHADER
////////////////////////////////////////////////////////////////////////////////////////////////////////////

VertexShaderOutput VertexShaderFunction(VertexShaderInput input, uint id:SV_VERTEXID)
{
	VertexShaderOutput output;
	output.Position = float4(input.Position, 0, 1);
	output.TexCoord.x = (float)(id / 2) * 2.0;
	output.TexCoord.y = 1.0 - (float)(id % 2) * 2.0;
	return output;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  PIXEL SHADERS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

float GetLuminance(float3 rgb)
{
	float luminance = dot(rgb, float3(0.2126f, 0.7152f, 0.0722f));
	// min/max return the non-NaN operand, so NaNs and infinities from the HDR buffer can't poison the average.
	return min(max(luminance, 0.00001f), 65000.0f);
}

float2 MeterLuminancePixelShader(float4 pos : SV_POSITION) : SV_TARGET0
{
	float2 uv = pos.xy / OutputSize;
	float2 tapOffset = 0.25f / OutputSize;

	// Four bilinear taps cover the texel's footprint better than one, which keeps small highlights from flickering.
	float logLuminance =
		log2(GetLuminance(InputTexture.SampleLevel(LinearSampler, uv + float2(-tapOffset.x, -tapOffset.y), 0).rgb)) +
		log2(GetLuminance(InputTexture.SampleLevel(LinearSampler, uv + float2( tapOffset.x, -tapOffset.y), 0).rgb)) +
		log2(GetLuminance(InputTexture.SampleLevel(LinearSampler, uv + float2(-tapOffset.x,  tapOffset.y), 0).rgb)) +
		log2(GetLuminance(InputTexture.SampleLevel(LinearSampler, uv + float2( tapOffset.x,  tapOffset.y), 0).rgb));
	logLuminance *= 0.25f;

	float2 toCenter = uv - 0.5f;
	float weight = lerp(1.0f, exp(-dot(toCenter, toCenter) * 8.0f), CenterWeight);

	return float2(logLuminance * weight, weight);
}

float2 DownsamplePixelShader(float4 pos : SV_POSITION) : SV_TARGET0
{
	int2 origin = int2(pos.xy) * 4;
	float2 sum = 0;

	[unroll]
	for (int y = 0; y < 4; y++)
	{
		[unroll]
		for (int x = 0; x < 4; x++)
		{
			sum += InputTexture.Load(int3(origin + int2(x, y), 0)).rg;
		}
	}

	return sum * (1.0f / 16.0f);
}

float AdaptPixelShader(float4 pos : SV_POSITION) : SV_TARGET0
{
	float2 metered = InputTexture.Load(int3(0, 0, 0)).rg;
	float averageLogLuminance = metered.r / max(metered.g, 0.000001f);

	float target = clamp(log2(KeyValue) - averageLogLuminance, MinExposure, MaxExposure);
	float previous = PreviousExposureTexture.Load(int3(0, 0, 0)).r;

	// Exposure going down means the scene got brighter; eyes adapt to light faster than to darkness.
	float speed = target < previous ? SpeedDarkToLight : SpeedLightToDark;
	float blend = Reset > 0.5f ? 1.0f : 1.0f - exp(-DeltaTime * speed);

	float exposure = lerp(previous, target, blend);
	// A NaN history would never recover, so fall back to the target.
	return isnan(exposure) ? target : exposure;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  TECHNIQUES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

technique MeterLuminance
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_5_0 MeterLuminancePixelShader();
	}
}

technique Downsample
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_5_0 DownsamplePixelShader();
	}
}

technique Adapt
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_5_0 AdaptPixelShader();
	}
}
