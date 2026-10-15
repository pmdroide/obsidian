
//Lightshader TheKosmonaut 2016

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VARIABLES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "../Common/helper.fx"
#include "../Common/sdf.fx"

float4x4 WorldView;
float4x4 WorldViewProj;
float4x4 InverseView;

//Is the camera inside the light sphere?
int inside = -1;

//Temporal displacement
//Inclueded in Helper
//float Time = 1;

//color of the light 
float3 lightColor;
//position of the camera, for specular light
float3 cameraPosition = float3(0,0,0);
//this is used to compute the world-position
float4x4 InvertViewProjection;
//this is the position of the light
float3 lightPosition = float3(0,0,0);
//how far does this light reach
float lightRadius = 0;
//control the brightness of the light
float lightIntensity = 1.0f;

//Density of our light volume if we render it that way
float lightVolumeDensity = 1;

//Spot light cone, view space axis. Point lights keep spotCosOuter = -2, which lights every direction.
float3 spotDirection = float3(0, 0, -1);
float spotCosOuter = -2;
float spotCosInner = -1;

//Spot light cookie (Source 2: lightcookie): projected along the cone, spotRight/spotUp = its u/v axes (view space)
bool UseCookie = false;
Texture2D CookieTexture;
float3 spotRight = float3(1, 0, 0);
float3 spotUp = float3(0, 1, 0);
float spotTanOuter = 1;
SamplerState CookieSampler
{
	Texture = (CookieTexture);
	AddressU = CLAMP;
	AddressV = CLAMP;
	MagFilter = LINEAR;
	MinFilter = LINEAR;
	Mipfilter = LINEAR;
};

//The cookie's colour for a unit vector from the light towards the shaded point
float3 CookieColor(float3 lightToSurface)
{
	[branch]
	if (!UseCookie) return 1;
	float forward = dot(lightToSurface, spotDirection);
	if (forward <= 0.001f) return 0;
	float2 uv = float2(dot(lightToSurface, spotRight), dot(lightToSurface, spotUp)) / (forward * spotTanOuter);
	uv = uv * 0.5f + 0.5f;
	return CookieTexture.SampleLevel(CookieSampler, float2(uv.x, 1 - uv.y), 0).rgb;
}

//Light source shape (2.4): sphere radius and tube half length along lightAxis (view space), 0 = point source
float lightSourceRadius = 0;
float lightSourceLength = 0;
float3 lightAxis = float3(1, 0, 0);

float ShadowMapSize = 512;
float ShadowMapRadius = 3;
float DepthBias = 0.02;

//Needed for manual texture sampling
float2 Resolution = float2(1280, 800);

//Multiply output with this to get results that can be evaluated alone
const float OUTPUTCONST = 0.1f;

// diffuse color, and specularIntensity in the alpha channel
Texture2D AlbedoMap;
// normals, and specularPower in the alpha channel
Texture2D NormalMap;
//depth
Texture2D DepthMap;

Texture2D NoiseMap;

SamplerState PointSampler
{
    Texture = (AlbedoMap);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = POINT;
    MinFilter = POINT;
    Mipfilter = POINT;
};

//The shadow atlas of every point and spot light (ShadowMapRenderModule): this light's cube faces are square tiles,
//ShadowTiles[face] = (x, y, size, 1 = in use) in atlas texels. Faces a spot light doesn't need are lit.
Texture2D ShadowMap;
float4 ShadowTiles[6];
//0..1: shadows fade out with distance from the camera (ShadowMapRenderModule)
float ShadowStrength = 1;

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  STRUCTS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct VertexShaderInput
{
    float4 Position : POSITION0;
};
struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float4 ScreenPosition : TEXCOORD0;
	float4 PositionVS : TEXCOORD1;
};

struct PixelShaderOutput
{
    float4 Diffuse : COLOR0;
    float4 Specular : COLOR1;
	float4 Volume : COLOR2;
};

struct PixelShaderInput
{
    float3 PositionVS : POSITION0;
    float2 TexCoord : TEXCOORD0;
	float Depth : TEXCOORD1;
};


////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  FUNCTIONS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

	////////////////////////////////////////////////////////////////////////////////////////////////////////////
	//  VERTEX SHADER
	////////////////////////////////////////////////////////////////////////////////////////////////////////////

VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
	VertexShaderOutput output;
	//processing geometry coordinates
	output.PositionVS = mul(input.Position, WorldView);
	output.Position = mul(input.Position, WorldViewProj);
	output.ScreenPosition = output.Position;
	return output;
}

//For stencil mask only
float4 VertexShaderBasic(VertexShaderInput input) : POSITION0
{
	return mul(input.Position, WorldViewProj);
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	//  PIXEL SHADER
	////////////////////////////////////////////////////////////////////////////////////////////////////////////

		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//  HELPER DECLARATIONS
		////////////////////////////////////////////////////////////////////////////////////////////////////////////

float CalcShadowTermPCF(float linearDepthLV, float ndotl, float3 vec3);
void LightingCalculation(in int3 texCoordInt, in float distanceToLight, in float3 lightVector, in float3 cameraDirection, inout float3 diffuseOutput, inout float3 specularOutput);
void LightingShadowedCalculation(in int3 texCoordInt, in float distanceToLight, in float3 lightVector, in float3 cameraDirection, inout float3 diffuseOutput, inout float3 specularOutput);
float4 LoadShadowMap(float3 vec3);
float GetVariableBias(float nDotL);
float ShadowCheck(float distance, float3 texCoord);
float integrateVolume(float d2, float d1, float radius);
float SpotConeFactor(float3 lightToSurface);

		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//  HELPER FUNCTIONS
		////////////////////////////////////////////////////////////////////////////////////////////////////////////

	//Check helper.fx for more helper functions

	//Smooth falloff between the outer and inner cone. lightToSurface is normalized.
	//Mirrors SpotLight.ConeFactor on the CPU side.
	float SpotConeFactor(float3 lightToSurface)
	{
		float t = saturate((dot(lightToSurface, spotDirection) - spotCosOuter) / max(spotCosInner - spotCosOuter, 0.0001f));
		return t * t;
	}

	//Diffuse and specular of this light at a G-buffer pixel, before shadows. lightVector = surface to light centre (view space).
	void ShadeLight(in int3 texCoordInt, in float distanceToLight, in float3 lightVector, in float3 cameraDirection,
		out float3 diffuseOutput, out float3 specularOutput, out float NdL)
	{
		float4 normalData = NormalMap.Load(texCoordInt);
		float3 normal = decode(normalData.xyz);
		float roughness = normalData.a;
		float4 color = AlbedoMap.Load(texCoordInt);
		float metalness = decodeMetalness(normalData.b);
		float3 f0 = GetF0(AlbedoToLinear(color.rgb), metalness);

		float attenuation = LightAttenuation(distanceToLight, lightRadius, lightSourceRadius);
		float3 l = lightVector / distanceToLight;
		attenuation *= SpotConeFactor(-l);
		NdL = saturate(dot(normal, l));

		//Sphere / tube lights: the highlight comes from the point of the light closest to the reflection ray
		float3 r = reflect(-cameraDirection, normal);
		float normalizationScale;
		float3 ls = normalize(AreaLightSpecularVector(lightVector, r, lightSourceRadius, lightSourceLength, lightAxis,
			roughness, normalizationScale));

		EvaluateLightSplit(normal, l, ls, cameraDirection, lightColor * CookieColor(-l) * lightIntensity * attenuation, f0, roughness,
			normalizationScale, diffuseOutput, specularOutput);
		diffuseOutput *= OUTPUTCONST;
		specularOutput *= OUTPUTCONST;
	}

	void LightingCalculation(in int3 texCoordInt, in float distanceToLight, in float3 lightVector, in float3 cameraDirection, inout float3 diffuseOutput, inout float3 specularOutput)
	{
		float NdL;
		ShadeLight(texCoordInt, distanceToLight, lightVector, cameraDirection, diffuseOutput, specularOutput, NdL);
	}

	void LightingShadowedCalculation(in int3 texCoordInt, in float distanceToLight, in float3 lightVector, in float3 cameraDirection, inout float3 diffuseOutput, inout float3 specularOutput)
	{
		float NdL;
		ShadeLight(texCoordInt, distanceToLight, lightVector, cameraDirection, diffuseOutput, specularOutput, NdL);

		float relativeDistance = distanceToLight / lightRadius;
		float3 lightVectorWS = -mul(float4(lightVector / distanceToLight, 0), InverseView).xyz;
		float shadowFactor = distanceToLight < lightRadius ? CalcShadowTermPCF(relativeDistance, NdL, lightVectorWS) : 0;
		shadowFactor = lerp(1, shadowFactor, ShadowStrength);

		diffuseOutput *= shadowFactor;
		specularOutput *= shadowFactor;
	}

	void LightingSDFCalculation(in float3 posVS, in int3 texCoordInt, in float distanceToLight, in float3 lightVector, in float3 cameraDirection, inout float3 diffuseOutput, inout float3 specularOutput)
	{
		float4 normalData = NormalMap.Load(texCoordInt);
		float4 color = AlbedoMap.Load(texCoordInt);

		float NdL;
		ShadeLight(texCoordInt, distanceToLight, lightVector, cameraDirection, diffuseOutput, specularOutput, NdL);
		float attenuation = LightAttenuation(distanceToLight, lightRadius, lightSourceRadius) * SpotConeFactor(-lightVector / distanceToLight);

		float3 positionWS = mul(float4(posVS, 1), InverseView).xyz;
		float3 lightWS = mul(float4(lightPosition, 1), InverseView).xyz;

		float shadowFactor = 0;
		float4 sss = 0;

		bool hasSubsurfaceScattering = abs(decodeMattype(normalData.b) - 4.0) < 0.01f;

		[branch]
		if (NdL <= 0.25f && hasSubsurfaceScattering)
		{
			float3 exitpoint = 0;
			sss.rgb = Subsurface(positionWS, lightWS, distance(positionWS, lightWS), exitpoint);
			sss.rgb = saturate(1 - sss.rgb / ShadowMapRadius) * 0.05f;
			sss.a = max(max(sss.r, sss.g), sss.b);
			positionWS = exitpoint;
		}

		[branch]
		if (NdL > 0 || sss.a > 0)
		{
			shadowFactor = RaymarchSoft(positionWS, lightWS, distance(positionWS, lightWS), sss.a > 0 ? (1-sss.a)*8.0f :32.0f);
		}

		if (distanceToLight >= lightRadius) shadowFactor = 0;
		diffuseOutput = diffuseOutput * shadowFactor + AlbedoToLinear(color.rgb) * shadowFactor * sss.rgb * attenuation * lightIntensity * lightColor * OUTPUTCONST;
		specularOutput *= shadowFactor;
	}

	//Loads a texel based on a coordinate in the 6-face strip layout of GetSampleCoordinate(vec3) (helper.fx), from
	//the face's tile in the shadow atlas. Faces without a tile read 0 = far (lit).
	//One can additionally add an offset, like this GetSampleOffset(sampleCoord, float2(x, y)*texelSize)
	float4 LoadShadowMapUV(float2 coord)
	{
		int face = clamp((int)floor(coord.y * 6), 0, 5);
		float4 tile = ShadowTiles[face];
		if (tile.w <= 0) return 0;
		float2 local = saturate(float2(coord.x, coord.y * 6 - face));
		int2 texel = int2(tile.xy) + min(int2(local * tile.z), (int)tile.z - 1);
		return ShadowMap.Load(int3(texel, 0)).r;
	}

	float4 LoadShadowMap(float3 vec3)
	{
		return LoadShadowMapUV(GetSampleCoordinate(vec3));
	}

//For Virtual Shadow Maps
//float chebyshevUpperBound(float distance, float3 texCoord)
//{
//	texCoord.z = -texCoord.z;
//	// We retrive the two moments previously stored (depth and depth*depth)
//	float2 moments = 1 - shadowCubeMap.SampleLevel(shadowCubeMapSampler, texCoord,0).rg;
//
//	// Surface is fully lit. as the current fragment is before the light occluder
//	// The fragment is either in shadow or penumbra. We now use chebyshev's upperBound to check
//	// How likely this pixel is to be lit (p_max)
//	float variance = moments.y - (moments.x * moments.x);
//	variance = max(variance, 0.000002);
//
//	float d = distance - moments.x;
//	float p_max = variance / (variance + d * d);
//
//	return p_max;
//}

float GetVariableBias(float nDotL)
{
	//return clamp(0.001 * tan(acos(nDotL)), 0, DepthBias);
	//tan = sqrt(1-cos(x)^2)/cos(x)
	return clamp(0.005 * sqrt(1 - nDotL * nDotL) / nDotL, 0, DepthBias);
}

//Shadow filtering with variable kernel, depending on ShadowMapRadius
float CalcShadowTermPCF(float linearDepthLV, float ndotl, float3 vec3)
{
	float fShadowTerm = 0.0;

	float Size = ShadowMapSize;

	//Could be offloaded to a static function
	float texelSize = 1.0f / Size;

	//Variable bias, depending on tan
	float variableBias = GetVariableBias(ndotl);
	
	//Apply bias
	float testDepth = linearDepthLV - variableBias;

	//Get float2 texture coordinate for our shadow map
	float2 sampleCoord = GetSampleCoordinate(vec3);

	//Calculate fractionals for edge tap smoothing
	float2 fractionals = frac(float2(Size * sampleCoord.x, Size * sampleCoord.y * 6));
	float2 complFractionals = float2(1, 1) - fractionals;

	//get the depth of our center sample
	float centerDepth = 1 - LoadShadowMapUV(sampleCoord).r;

	float fRadius = ShadowMapRadius - 1;

	//No PCF -> we are done
	if(fRadius < 1) 
		return testDepth < centerDepth;

	float closestDepth;

	//[unroll] is better, but can't be used when dynamically changing size
	[loop]
	for (float y = -fRadius; y <= fRadius; y++)
	{
		[loop]
		for (float x = -fRadius; x <= fRadius; x++)
		{
			if (x != 0 || y != 0)
			{
				closestDepth = 1 - LoadShadowMapUV(GetSampleOffset(sampleCoord, float2(x, y)*texelSize)).r;
			}
			else
			{
				closestDepth = centerDepth;
			}

			float fSample = testDepth < closestDepth;

			// Edge tap smoothing
			float xWeight = 1;
			float yWeight = 1;

			//Our edge texels get smoothed, they are weighted by area covered
			if (x == -fRadius)
				xWeight = complFractionals.x;
			else if (x == fRadius)
				xWeight = fractionals.x;

			if (y == -fRadius)
				yWeight = complFractionals.y;
			else if (y == fRadius)
				yWeight = fractionals.y;
			
			fShadowTerm += fSample * xWeight * yWeight;
		}
	}

	//Edge texels are worth half by default, because the one side is weighted as frac, while the other is 1-frac, so a+b = 1;
	/*
		Example: 
		[ , ] [ / ] [ , ]
		[ / ] [ X ] [ / ]
		[ , ] [ / ] [ , ]

		-> Radius = 1. Center sample is considered 100%, other 4 samples are 0.5. 4 edges are 0.25
		-> 1 + 2 + 1 = 4
		Radius * Radius * 4 = 4;

		Example2:
		-> Radius = 2 -> 9 full samples, 12 half samples, 4 edges are 0.25 
		9 + 6 + 1 = 16
		Radius * Radius * 4 = 16
	*/
	
	fShadowTerm /= (fRadius * fRadius * 4);

	return fShadowTerm;
}

//Plain Shadow Depth difference, no bias
float ShadowCheck(float distance, float3 texCoord)
{
	float2 sampleCoord = GetSampleCoordinate(texCoord);

	float centerDepth = 1 - LoadShadowMapUV(sampleCoord).r;

	return distance < centerDepth;
}

//Integrate our path through some volume. Depends only on distance from light.
float integrateVolume(float d2, float d1, float radius)
{
	// 1 - x/r -> x - x^2 / r*2
	return max(d2 - d2*d2 / (radius * 2) - d1 + d1*d1 / (radius * 2), 0);

	// 1 - x^2 / r^2 -> x - x^3 / r^2*3
	//return max(d2 - d2*d2*d2 / (radius*radius * 3) - d1 + d1*d1*d1 / (radius*radius * 3), 0);
}

	////////////////////////////////////////////////////////////////////////////////////////////////////////////
	//  SHADING AND VOLUME TRAVERSAL
	////////////////////////////////////////////////////////////////////////////////////////////////////////////

//Used for both shadow casting and unshadowed, shared function
PixelShaderInput BaseCalculations(VertexShaderOutput input)
{
	PixelShaderInput output;
     //obtain screen position
    input.ScreenPosition.xyz /= input.ScreenPosition.w;
    //obtain textureCoordinates corresponding to the current pixel
    //the screen coordinates are in [-1,1]*[1,-1]
    //the texture coordinates need to be in [0,1]*[0,1]
    float2 texCoord = 0.5f * (float2(input.ScreenPosition.x, -input.ScreenPosition.y) + 1);

    /////////////////////////////////////////

    //read depth, use point sample or load
	output.Depth = DepthMap.Load(int3(input.Position.xy, 0));//DepthMap.SampleLevel(PointSampler, texCoord, 0).r;

	//Basically extend the depth of this ray to the end of the far plane, this gives us the position of the sphere only
	float3 cameraDirVS = input.PositionVS.xyz * (FarClip / -input.PositionVS.z);

    //compute ViewSpace position
	float3 positionVS = output.Depth * cameraDirVS;

    //////////////////////////////////////
    output.PositionVS = positionVS;
    output.TexCoord = texCoord;

    return output;
}

//Unshadowed light without volumetric fog around
PixelShaderOutput BasePixelShaderFunction(PixelShaderInput input)
{
    PixelShaderOutput output;

	output.Diffuse = float4(0, 0, 0, 0);
	output.Specular = float4(0, 0, 0, 0);
	output.Volume = float4(0, 0, 0, 0);

    //surface-to-light vector, in VS
    float3 lightVector = lightPosition - input.PositionVS.xyz;
    float lengthLight = length(lightVector);

	//If the pixel is outside of our light, clip!
    [branch]
    if (lengthLight > lightRadius)
    {
        clip(-1);
        return output;
    }
    else
    {
		int3 texCoordInt = int3(input.TexCoord * Resolution, 0);
		float3 cameraDirection = -normalize(input.PositionVS.xyz);
		LightingCalculation(texCoordInt, lengthLight, lightVector, cameraDirection, output.Diffuse.rgb, output.Specular.rgb);
        return output;
    }
}


//Unshadowed light without volumetric fog around
PixelShaderOutput BasePixelShaderFunctionSDF(PixelShaderInput input)
{
    PixelShaderOutput output;

    output.Diffuse = float4(0, 0, 0, 0);
    output.Specular = float4(0, 0, 0, 0);
    output.Volume = float4(0, 0, 0, 0);

    //surface-to-light vector, in VS
    float3 lightVector = lightPosition - input.PositionVS.xyz;
    float lengthLight = length(lightVector);

	//If the pixel is outside of our light, clip!
    [branch]
    if (lengthLight > lightRadius)
    {
        clip(-1);
        return output;
    }
    else
    {
        int3 texCoordInt = int3(input.TexCoord * Resolution, 0);
        float3 cameraDirection = -normalize(input.PositionVS.xyz);
		LightingSDFCalculation(input.PositionVS, texCoordInt, lengthLight, lightVector, cameraDirection, output.Diffuse.rgb, output.Specular.rgb);
        return output;
    }
}

//Stencil masking
float4 PixelShaderBasic(float4 position : POSITION) : COLOR
{
	return float4(0,0,0,0);
}

//Base function
PixelShaderOutput PixelShaderFunction(VertexShaderOutput input)
{
	PixelShaderInput p_input = BaseCalculations(input);

	PixelShaderOutput Output;

	float lightDepth = input.PositionVS.z / -FarClip;

	[branch]
	if (lightDepth * inside < p_input.Depth * inside)
	{
		clip(-1);
		return Output;
	}
	else
	{
		return BasePixelShaderFunction(p_input);
	}
}


//Base function
PixelShaderOutput PixelShaderFunctionSDF(VertexShaderOutput input)
{
    PixelShaderInput p_input = BaseCalculations(input);

    PixelShaderOutput Output;

    float lightDepth = input.PositionVS.z / -FarClip;

	[branch]
    if (lightDepth * inside < p_input.Depth * inside)
    {
        clip(-1);
        return Output;
    }
    else
    {
        return BasePixelShaderFunctionSDF(p_input);
    }
}

//Unshadowed light with fog around it
PixelShaderOutput VolumetricPixelShaderFunction(VertexShaderOutput input)
{
	PixelShaderOutput output;
	input.ScreenPosition.xyz /= input.ScreenPosition.w;
	float2 texCoord = 0.5f * (float2(input.ScreenPosition.x, -input.ScreenPosition.y) + 1);
	int3 texCoordInt = int3(texCoord * Resolution, 0);

	//read linear depth
	float linDepth = DepthMap.Load(texCoordInt).r;

	//Basically extend the depth of this ray to the end of the far plane, this gives us the position of the sphere only
	//todo: needed?
	float3 cameraDirVS = input.PositionVS.xyz * (FarClip / -input.PositionVS.z);

	//compute ViewSpace position
	float3 positionFromDepthVS = linDepth * cameraDirVS;

	//The way our camera is looking
	float3 cameraDirection = normalize(input.PositionVS.xyz);

	//If inside = 0 we look from the outside, so we enter into the field
	float3 toLightCenter = normalize(lightPosition - input.PositionVS.xyz);

	//Is just the light position since we work from ViewSpace
	float3 cameraToLight = lightPosition;
	float distanceLtoC = length(cameraToLight);

	//normalize
	cameraToLight /= distanceLtoC;

	float alpha = dot(cameraDirection, cameraToLight);

	//P is the vertex position (interpolated)
	//B is the halfway in the sphere
	//R is the reconstructed position from the depthmap

	float distanceCtoP = distance(cameraPosition, input.PositionVS.xyz);
	float distanceCtoB = alpha * distanceLtoC;
	float distanceCtoR = distance(cameraPosition, positionFromDepthVS);

	//Position of B
	float3 b_vector = distanceCtoB * cameraDirection + cameraPosition;

	float totalVolumePassed = 0;

	float distanceLtoP = distance(lightPosition, input.PositionVS.xyz);
	float distanceLtoB = distance(lightPosition, b_vector);
	float distanceLtoR = distance(lightPosition, positionFromDepthVS);

	[branch]
	if (inside <= 0)
	{
		//Not inside
		[branch]
		if (distanceCtoR > distanceCtoP + (distanceCtoB - distanceCtoP) * 2)
		{
			totalVolumePassed = integrateVolume(distanceLtoP, distanceLtoB, lightRadius) - integrateVolume(distanceLtoP, lightRadius, lightRadius);
		}

		else
		{
			if (distanceCtoR > distanceCtoB)
			{
				totalVolumePassed = integrateVolume(distanceLtoP, distanceLtoB, lightRadius) + integrateVolume(distanceLtoB, distanceLtoR, lightRadius);
			}
			else if (distanceCtoR > distanceCtoP)
			{
				totalVolumePassed = integrateVolume(distanceLtoP, distanceLtoR, lightRadius);
			}
		}
	}
	else
	{
		//object behind our volume
		if (distanceCtoR > distanceCtoP)
		{
			//is camera behind mid of volume?
			if (alpha < 0)
			{
				totalVolumePassed = integrateVolume(distanceLtoP, distanceLtoC, lightRadius) / 2;
			}
			else
			{
				totalVolumePassed = 0.5f *(integrateVolume(distanceLtoP, distanceLtoB, lightRadius) + integrateVolume(distanceLtoC, distanceLtoB, lightRadius));
			}
		}
		else
		{
			if (alpha < 0)
			{
				totalVolumePassed = integrateVolume(distanceLtoR, distanceLtoC, lightRadius) / 2;
			}
			else
			{
				if (distanceCtoR < distanceCtoB)
				{
					totalVolumePassed = 0.5f*integrateVolume(distanceLtoC, distanceLtoR, lightRadius);
				}
				else
				{
					totalVolumePassed = 0.5f*(integrateVolume(distanceLtoR, distanceLtoB, lightRadius) + integrateVolume(distanceLtoC, distanceLtoB, lightRadius));
				}
			}
		}
	}

	float3 lightVector = lightPosition - positionFromDepthVS;

	output.Diffuse = 0;
	output.Specular = 0;
	//Spot lights: weight the analytic integral by the cone at B, the point of the ray closest to the light
	float3 lightToB = b_vector - lightPosition;
	float coneB = SpotConeFactor(lightToB * rsqrt(max(dot(lightToB, lightToB), 0.00000001f)));

	float volumetricStrength = totalVolumePassed * 0.0004 * lightIntensity * lightVolumeDensity * coneB;
	output.Volume = float4(volumetricStrength * lightColor, saturate(volumetricStrength * 0.5f));

	[branch]
	if (distanceLtoR < lightRadius)
	{
		LightingCalculation(texCoordInt, distanceLtoR, lightVector, -cameraDirection, output.Diffuse.rgb, output.Specular.rgb);
	}
	return output;
}

//Shadow mapped light!
PixelShaderOutput BasePixelShaderFunctionShadow(PixelShaderInput input)
{
    PixelShaderOutput output;
    
    //surface-to-light vector
    float3 lightVector = lightPosition - input.PositionVS;

    float lengthLight = length(lightVector);

    [branch]
    if (lengthLight > lightRadius)
    {
        clip(-1);
        return output;
    }
	else
	{
		int3 texCoordInt = int3(input.TexCoord * Resolution, 0);
		float3 cameraDirection = -normalize(input.PositionVS);

		LightingShadowedCalculation(texCoordInt, lengthLight, lightVector, cameraDirection, output.Diffuse.rgb, output.Specular.rgb);

		return output;
	}
}


PixelShaderOutput PixelShaderFunctionShadowed(VertexShaderOutput input)
{
	PixelShaderInput p_input = BaseCalculations(input);

	PixelShaderOutput Output;

	/*float lightDepth = input.PositionVS.z / -FarClip;

	if (lightDepth * inside < p_input.Depth * inside)
	{
		clip(-1);
		return Output;
	}
	else
	{*/
		Output = BasePixelShaderFunctionShadow(p_input);

		return Output;
	//}
}

//

//ShadowMapped light with Fog
PixelShaderOutput VolumetricPixelShaderFunctionShadowed(VertexShaderOutput input)
{
	PixelShaderOutput output;

	output.Diffuse = 0;
	output.Specular = 0;

	input.ScreenPosition.xyz /= input.ScreenPosition.w;
	float2 texCoord = 0.5f * (float2(input.ScreenPosition.x, -input.ScreenPosition.y) + 1);

	int3 texCoordInt = int3(texCoord * Resolution, 0);

	//read linear depth
	float linDepth = DepthMap.Load(texCoordInt).r;

	//Basically extend the depth of this ray to the end of the far plane, this gives us the position of the sphere only
	float3 cameraDirVS = input.PositionVS.xyz * (FarClip / -input.PositionVS.z);

	//compute ViewSpace position
	float3 positionFromDepthVS = linDepth * cameraDirVS;

	//The way our camera is looking
	float3 cameraDirection = normalize(input.PositionVS.xyz);

	//If inside = 0 we look from the outside, so we enter into the field
	float3 toLightCenter = normalize(lightPosition - input.PositionVS.xyz);

	float3 cameraToLight = lightPosition;
	float distanceLtoC = length(cameraToLight);

	cameraToLight /= distanceLtoC;

	float distanceCtoR = distance(cameraPosition, positionFromDepthVS);

	float totalVolumePassed = 0;

	float distanceLtoR = distance(lightPosition, positionFromDepthVS);

	float3 lightVector = lightPosition - positionFromDepthVS;

	//Raymarch

	float3 start_vector;
	float3 end_vector;

	if (inside<1)
	{
		start_vector =  cameraDirection*(distanceLtoC - lightRadius);
		end_vector =  cameraDirection*(distanceLtoC + lightRadius);
	}
	else
	{
		start_vector = 0;// float3(0, 0, 0);
		end_vector = input.PositionVS.xyz;
	}

	float visibility = 0;

	float previousLightDistance = lightRadius * 2;

	if (distanceCtoR > distanceLtoC - lightRadius)
	{

		int steps = 10;

		float3 ray = end_vector - start_vector;

		float noiseX =  (frac(sin(dot(texCoord* (frac(Time)+1), float2(15.8989f, 76.132f) )) * 46336.23745f));

		//float noiseX = NoiseMap.Sample(PointSampler, frac(((texCoord)* Resolution) / 64)).r; // + frac(input.TexCoord* Projection)).r;

		[loop]
		for (int i = 0; i < steps; i++)
		{
			float4 rayPosition = float4(float3(start_vector + ((i + noiseX) / (steps))*ray), 1);

			float3 lightVectorRay = lightPosition - rayPosition.xyz;
			float lightDistance = length(lightVectorRay);

			//normalize
			lightVectorRay /= lightDistance;

			float3 lightVectorWS = -mul(float4(lightVectorRay, 0), InverseView).xyz;

			//float depthInLS = getDepthInLS(rayPosition, lightVectorWS);

			if (length(rayPosition.xyz) > distanceCtoR || previousLightDistance < lightDistance) break;
			else
			{
				previousLightDistance = lightDistance;
				float dist = saturate( lightDistance / lightRadius * 1.1f) - 1;
				dist = dist*dist;
				visibility += ShadowCheck(lightDistance / lightRadius, lightVectorWS) * saturate(dist*dist) * SpotConeFactor(-lightVectorRay);//1 - dist*dist / (lightRadius*lightRadius));
			}
		}
		visibility /= steps;
	}
	//startvector > input.WorldPositionVS
	//EndVector

	output.Volume = float4((0.02 * lightIntensity * lightVolumeDensity * visibility) * lightColor, 0);

	LightingShadowedCalculation(texCoordInt, distanceLtoR, lightVector, -cameraDirection, output.Diffuse.rgb, output.Specular.rgb);

	return output;

}
////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  FUNCTIONS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

technique WriteStencilMask
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderBasic();
		PixelShader = compile ps_4_0 PixelShaderBasic();
	}
}

technique Unshadowed
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_4_0 PixelShaderFunction();
    }
}

technique ShadowedSDF
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_4_0 PixelShaderFunctionSDF();
    }
}

technique UnshadowedVolume
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_4_0 VolumetricPixelShaderFunction();
	}
}

technique Shadowed
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 VertexShaderFunction();
		PixelShader = compile ps_4_0 PixelShaderFunctionShadowed();
	}
}

technique ShadowedVolume
{
    pass Pass1
    {
        VertexShader = compile vs_5_0 VertexShaderFunction();
        PixelShader = compile ps_5_0 VolumetricPixelShaderFunctionShadowed();
    }
}