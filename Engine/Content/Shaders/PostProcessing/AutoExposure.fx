// Eye adaptation / auto exposure
// 1. MeterLuminance: HDR frame -> 256x256 center-weighted log2 luminance (R = w * log2(L), G = w)
// 2. Downsample:     4x4 box reduction, 256 -> 64 -> 16 -> 4 -> 1, so the last texel is the weighted average
// 3. Histogram:      (bright-pixel metering) for 64 log2 luminance levels, the weighted share of the 256x256 meter above it
// 4. Adapt:          moves last frame's exposure (EV, 1x1) toward the metered target over time
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

// Metering: 0 = bright pixels (Source's tonemap controller, Docs/Research/Source2Look phase 1.4), 1 = average,
// 2 = Source 2 (Half-Life: Alyx, Source2Look R2): the median of the frame's luminance histogram exposed to KeyValue.
float Metering = 0;
// Share of the frame (0..1) that counts as its highlights, and the linear brightness they are exposed to.
float BrightShare = 0.02f;
float BrightTarget = 0.18f;
// The exposed average never drops below this (0 = no floor).
float MinAverage = 0.03f;

// Histogram of the meter: texel i = share of the weight with log2 luminance >= HistogramLevel(i)
#define HISTOGRAM_BINS 64
static const float HistogramMin = -16;
static const float HistogramMax = 6;
Texture2D HistogramTexture;

float HistogramLevel(float i)
{
	return HistogramMin + i * (HistogramMax - HistogramMin) / (HISTOGRAM_BINS - 1);
}

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

// Input = the 256x256 meter (R = w * log2 L, G = w). Every second texel in x and y is plenty for the share.
float HistogramPixelShader(float4 pos : SV_POSITION) : SV_TARGET0
{
	float level = HistogramLevel(floor(pos.x));
	float above = 0;
	float total = 0;

	[loop]
	for (int y = 0; y < 256; y += 2)
	{
		[loop]
		for (int x = 0; x < 256; x += 2)
		{
			float2 m = InputTexture.Load(int3(x, y, 0)).rg;
			float w = max(m.g, 1e-6f);
			float logL = m.r / w;
			above += logL >= level ? w : 0;
			total += w;
		}
	}
	return above / max(total, 1e-6f);
}

// log2 luminance that the brightest `shareAbove` of the frame is above, read off the histogram
float LogLuminanceAbove(float shareAbove)
{
	float result = HistogramMin;
	float previous = HistogramTexture.Load(int3(0, 0, 0)).r;
	[loop]
	for (int i = 1; i < HISTOGRAM_BINS; i++)
	{
		float share = HistogramTexture.Load(int3(i, 0, 0)).r;
		if (previous >= shareAbove && share < shareAbove)
			result = lerp(HistogramLevel(i - 1), HistogramLevel(i), saturate((previous - shareAbove) / max(previous - share, 1e-6f)));
		previous = share;
	}
	if (previous >= shareAbove) result = HistogramMax;
	return result;
}

float BrightLogLuminance()
{
	return LogLuminanceAbove(BrightShare);
}

float AdaptPixelShader(float4 pos : SV_POSITION) : SV_TARGET0
{
	float2 metered = InputTexture.Load(int3(0, 0, 0)).rg;
	float averageLogLuminance = metered.r / max(metered.g, 0.000001f);

	float target = log2(KeyValue) - averageLogLuminance;
	[branch]
	if (Metering > 1.5f)
	{
		// CTonemapSystem log-luminance mode (scenesystem.dll, HLA's gameinfo.gi Tonemapping_UseLogLuminance): the
		// histogram holds ln(L), the median bucket is exponentiated with powf(2, x), exposure = KeyValue / 2^ln(L).
		// So the exposure follows L^-ln2 (~0.69): dark rooms stay darker and bright areas brighter than a full match.
		target = log2(KeyValue) - LogLuminanceAbove(0.5f) * 0.693147f;
	}
	else if (Metering < 0.5f)
	{
		// Highlights to the target brightness, but never leave the average darker than MinAverage
		target = log2(BrightTarget) - BrightLogLuminance();
		if (MinAverage > 0)
			target = max(target, log2(MinAverage) - averageLogLuminance);
	}
	target = clamp(target, MinExposure, MaxExposure);
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

technique Histogram
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_5_0 HistogramPixelShader();
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
