
float4x4 CurrentToPrevious;


Texture2D DepthMap;
Texture2D AccumulationMap;
Texture2D UpdateMap;

float2 Resolution = { 1280, 800 };

float3 FrustumCorners[4]; //In Viewspace!

bool UseTonemap = true;

//float Threshold = 1;

SamplerState texSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = POINT;
    MinFilter = POINT;
};

SamplerState linearSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = POINT;
};


////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  STRUCT DEFINITIONS

struct VertexShaderInput
{
    float2 Position : POSITION0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
	float3 ViewRay : TEXCOORD1;
};

struct PixelShaderOutput
{
	float4 Combine : COLOR0;
	//float4 Coherence : COLOR1;
};

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  FUNCTION DEFINITIONS


////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	//  VERTEX SHADER
	////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 GetFrustumRay(uint id)
{
	//Bottom left
	if (id < 1)
	{
		return FrustumCorners[2];
	}
	else if (id < 2) //Top left
	{
		return FrustumCorners[2] + (FrustumCorners[0] - FrustumCorners[2]) * 2;
	}
	else
	{
		return FrustumCorners[2] + (FrustumCorners[3] - FrustumCorners[2]) * 2;
	}

}
VertexShaderOutput VertexShaderFunction(VertexShaderInput input, uint id:SV_VERTEXID)
{
	VertexShaderOutput output;
	output.Position = float4(input.Position, 0, 1);
	output.TexCoord.x = (float)(id / 2) * 2.0;
	output.TexCoord.y = 1.0 - (float)(id % 2) * 2.0;

	output.ViewRay = GetFrustumRay(id);
	return output;
}


////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	//  PIXEL SHADER
	////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 ToYUV(float3 rgb)
{
	return rgb;
    /*float y = 0.299f * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b;

    return float3(y, (rgb.b - y) * 0.493, (rgb.r - y) * 0.877);*/
}

float overlapFunction(float3 x, float3 y)
{
	//return dot(x, y) / (length(x)*length(y));
	return 1 - dot(abs(x-y), float3(1, 1, 1)) / 3;
}

float3 GetFrustumRay2(float2 texCoord)
{
	float3 x1 = lerp(FrustumCorners[0], FrustumCorners[1], texCoord.x);
	float3 x2 = lerp(FrustumCorners[2], FrustumCorners[3], texCoord.x);
	float3 outV = lerp(x1, x2, texCoord.y);
	return outV;
}

float GetLuma(float3 rgb)
{
	return (0.299 * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b);
}

//http://www.cs.utah.edu/~reinhard/cdrom/tonemap.pdf

float3 ReinhardTonemap(float3 hdr)
{
	float x = GetLuma(hdr);
	return hdr * (1 / (x + 1));
}

float3 InverseReinhardTonemap(float3 ldr)
{
	float x = GetLuma(ldr);
	return ldr * ((x + 1) / 1);
}

float4 InverseToneMapPixelShader(VertexShaderOutput input) : SV_Target
{
	int3 TexCoordInt = int3(input.TexCoord * Resolution, 0);

	float4 updatedColorSample = AccumulationMap.Load(TexCoordInt);

	return float4(InverseReinhardTonemap(updatedColorSample.rgb), updatedColorSample.a);
}


float3 RGBToYCoCg(float3 rgb)
{
	return float3(dot(rgb, float3(0.25, 0.5, 0.25)), dot(rgb, float3(0.5, 0, -0.5)), dot(rgb, float3(-0.25, 0.5, -0.25)));
}

float3 YCoCgToRGB(float3 ycocg)
{
	return float3(ycocg.x + ycocg.y - ycocg.z, ycocg.x + ycocg.z, ycocg.x - ycocg.y - ycocg.z);
}

//Pull the history colour towards the box centre until it lies inside the box
float3 ClipToBox(float3 history, float3 boxMin, float3 boxMax)
{
	float3 center = 0.5 * (boxMax + boxMin);
	float3 extents = 0.5 * (boxMax - boxMin) + 0.0001;
	float3 offset = history - center;
	float3 units = abs(offset / extents);
	float maxUnit = max(units.x, max(units.y, units.z));
	return maxUnit > 1 ? center + offset / maxUnit : history;
}

PixelShaderOutput PixelShaderFunction(VertexShaderOutput input) : SV_Target
{
	PixelShaderOutput output;
	int2 pixel = int2(input.Position.xy);
	int2 maxPixel = int2(Resolution) - 1;

	//Gather the 3x3 neighbourhood of the current frame: colour statistics for history rejection
	//and the closest depth, so edges reproject with the foreground object instead of leaving a trail
	float4 updatedColorSample = 0;
	float3 m1 = 0;
	float3 m2 = 0;
	float3 boxMin = 100000;
	float3 boxMax = -100000;
	float closestDepth = 1;

	[unroll]
	for (int y = -1; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++)
		{
			int3 samplePixel = int3(clamp(pixel + int2(x, y), 0, maxPixel), 0);
			float4 neighbour = UpdateMap.Load(samplePixel);

			//HDR -> LDR!
			[branch]
			if (UseTonemap)
				neighbour.rgb = ReinhardTonemap(neighbour.rgb);

			if (x == 0 && y == 0)
				updatedColorSample = neighbour;

			float3 ycocg = RGBToYCoCg(neighbour.rgb);
			m1 += ycocg;
			m2 += ycocg * ycocg;
			boxMin = min(boxMin, ycocg);
			boxMax = max(boxMax, ycocg);

			closestDepth = min(closestDepth, DepthMap.Load(samplePixel).r);
		}
	}

	//Reproject into the previous frame
	float3 positionVS = input.ViewRay * closestDepth;

	float4 previousPositionVS = mul(float4(positionVS, 1), CurrentToPrevious);
	previousPositionVS /= previousPositionVS.w;

	float2 sampleTexCoord = 0.5f * (float2(previousPositionVS.x, -previousPositionVS.y) + 1);

	int3 sampleTexCoordInt = int3(sampleTexCoord * Resolution, 0);

	float4 accumulationColorSample = AccumulationMap.Load(sampleTexCoordInt);

	//Variance clipping: history that does not match anything around this pixel now (disocclusion,
	//moving objects, lighting changes) is pulled into the current colour range instead of ghosting
	float3 mean = m1 / 9;
	float3 sigma = sqrt(max(m2 / 9 - mean * mean, 0));
	float3 varianceMin = max(boxMin, mean - sigma);
	float3 varianceMax = min(boxMax, mean + sigma);

	accumulationColorSample.rgb = YCoCgToRGB(ClipToBox(RGBToYCoCg(accumulationColorSample.rgb), varianceMin, varianceMax));

	float alpha = accumulationColorSample.a;
	alpha = min(1 - 1 / (1 / (1 - alpha) + 1), 0.9375);

	//Out of bounds, no info
	if (sampleTexCoord.x > 1 || sampleTexCoord.x < 0 || sampleTexCoord.y > 1 || sampleTexCoord.y < 0)
		alpha = 0;

	float3 rgbout = lerp(updatedColorSample.rgb, accumulationColorSample.rgb, alpha);

	output.Combine = float4(rgbout, 1);

	return output;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  TECHNIQUES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

technique TemporalAntialiasing
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_5_0 PixelShaderFunction();
    }
}

technique InverseTonemap
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_5_0 InverseToneMapPixelShader();
	}
}
