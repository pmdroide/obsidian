//Environment cube maps, TheKosmonaut 2016

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VARIABLES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "../Common/helper.fx"
#include "../Common/sdf.fx"
#include "../Common/clouds.fx"

//We want to get from VS to WS. Usually this would mean an inverted VS. To get to 3x3 it's useful to use TI on this one.
//So it's T I I = T
float3x3 TransposeView;

Texture2D AlbedoMap;
Texture2D NormalMap;
Texture2D ReflectionMap;

//SDF
bool UseSDFAO;
Texture2D DepthMap;

float2 Resolution = { 1280, 800 };

float3 SkyColor = float3(0.1385, 0.3735f, 0.9805f);
float3 CameraPositionWS;

bool FireflyReduction;
float FireflyThreshold = 0.1f;

float EnvironmentMapSpecularStrength = 1.0f;
float EnvironmentMapSpecularStrengthRcp = 1.0f;
float EnvironmentMapDiffuseStrength = 0.2f;

TextureCube ReflectionCubeMap;
SamplerState ReflectionCubeMapSampler
{
    texture = <ReflectionCubeMap>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};

TextureCube SkyCubeMap;
SamplerState SkyCubeMapSampler
{
    texture = <SkyCubeMap>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};

Texture2D SkyMap2D;
SamplerState SkyMap2DSampler
{
    texture = <SkyMap2D>;
    AddressU = WRAP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};
bool UseSkyMap2D;
bool DayNightCycle;
float3 SunDirection;
float Daylight;
//Day/night sky look (EnvironmentSettings), linear colours
float3 DaySkyZenith = float3(0.08, 0.3, 0.8);
float3 DaySkyHorizon = float3(0.65, 0.78, 0.95);
float3 SunsetColor = float3(0.9, 0.25, 0.08);
float3 NightSkyZenith = float3(0.002, 0.004, 0.015);
float3 NightSkyHorizon = float3(0.012, 0.018, 0.04);
float2 SunDisc = float2(0.9995, 0.9998);  //cosines of the disc's outer and inner edge
float2 MoonDisc = float2(0.9996, 0.9998);
float SunBrightness = 1;
float MoonBrightness = 1;
float StarBrightness = 1;

//Baked probe volume (Engine/Renderer/Lighting). Each probe stores cosine-convolved L1 SH per colour
//channel as (c0, cx, cy, cz): irradiance E(n) = c0 + dot(c.yzw, n), in deferred light units.
//Probes sit on the corners of a regular grid spanning [ProbeVolumeMin, ProbeVolumeMin + 1/ExtentRcp].
bool UseProbeVolume = false;
float ProbeVolumeIntensity = 1.0f;
float3 ProbeVolumeMin;
float3 ProbeVolumeExtentRcp;
float3 ProbeVolumeCells; //probes per axis - 1
//Probe SH at the reflection cubemap's capture point (EnvironmentSample position), same layout as a probe
float4 CaptureSHR;
float4 CaptureSHG;
float4 CaptureSHB;

Texture3D ProbeSHR;
SamplerState ProbeSHRSampler
{
    texture = <ProbeSHR>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    AddressW = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = POINT;
};
Texture3D ProbeSHG;
SamplerState ProbeSHGSampler
{
    texture = <ProbeSHG>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    AddressW = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = POINT;
};
Texture3D ProbeSHB;
SamplerState ProbeSHBSampler
{
    texture = <ProbeSHB>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    AddressW = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = POINT;
};

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
    float3 ViewDir : TEXCOORD1;
};

struct PixelShaderOutput
{
    float4 Diffuse : COLOR0;
    float4 Specular : COLOR1;
};


////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  FUNCTIONS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

	////////////////////////////////////////////////////////////////////////////////////////////////////////////
	//  VERTEX SHADER
	////////////////////////////////////////////////////////////////////////////////////////////////////////////


VertexShaderOutput VertexShaderFunction(VertexShaderInput input, uint id:SV_VERTEXID)
{
	VertexShaderOutput output;
	output.Position = float4(input.Position, 0, 1);
	output.TexCoord.x = (float)(id / 2) * 2.0;
	output.TexCoord.y = 1.0 - (float)(id % 2) * 2.0;

	output.ViewDir = GetFrustumRay(id);
	return output;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	//  PIXEL SHADER
	////////////////////////////////////////////////////////////////////////////////////////////////////////////

		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//  HELPER FUNCTIONS
		////////////////////////////////////////////////////////////////////////////////////////////////////////////

float GetLuma(float3 rgb)
{
	return (0.299 * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b)*10;
}

float3 SampleSky(float3 viewDir)
{
	float3 viewDirNorm = normalize(viewDir);
	if (DayNightCycle)
	{
		float height = saturate(viewDirNorm.z);
		float3 night = lerp(NightSkyHorizon, NightSkyZenith, height);
		float3 day = lerp(DaySkyHorizon, DaySkyZenith, sqrt(height));
		float sunset = (1 - saturate(abs(SunDirection.z) * 5)) * (1 - height) * Daylight;
		float3 sky = lerp(night, day, Daylight);
		sky = lerp(sky, SunsetColor, sunset * 0.7);
		float sunDisc = smoothstep(SunDisc.x, SunDisc.y, dot(viewDirNorm, SunDirection));
		float sunGlow = pow(saturate(dot(viewDirNorm, SunDirection)), 64);
		sky += (sunDisc * 3 + sunGlow * 0.2) * float3(1, 0.8, 0.5) * Daylight * SunBrightness;
		//Moon opposite the sun (EnvironmentSettings.MoonDirection), cool and faint
		float moonDot = dot(viewDirNorm, normalize(float3(-SunDirection.x, SunDirection.y, -SunDirection.z)));
		float moonDisc = smoothstep(MoonDisc.x, MoonDisc.y, moonDot);
		float moonGlow = pow(saturate(moonDot), 32);
		sky += (moonDisc * 0.8 + moonGlow * 0.03) * float3(0.6, 0.7, 1.0) * (1 - Daylight) * MoonBrightness;
		float3 starCell = floor(viewDirNorm * 700);
		float star = frac(sin(dot(starCell, float3(12.9898, 78.233, 45.164))) * 43758.5453);
		sky += step(0.998, star) * (1 - Daylight) * height * 0.3 * StarBrightness;
		//Clouds go on last so they cover the sun, moon and stars
		return ApplyClouds(sky, viewDirNorm, SunDirection, Daylight);
	}
	float3 skyColor;

	if (UseSkyMap2D)
	{
		// World space is Z-up (see Camera.cs). Typical equirectangular maps sweep azimuth around the
		// vertical axis and use polar angle from that axis — i.e. treat +Z as the sky pole here.
		// Using acos(dir.y) assumes Y-up and causes pole pinch / spiral at horizon.
		float phi = atan2(viewDirNorm.y, viewDirNorm.x); // yaw around +Z from +X toward +Y
		float theta = acos(clamp(viewDirNorm.z, -1.0f, 1.0f)); // zenith measured from world +Z
		const float PI_VALUE = 3.14159265f;
		const float TWO_PI = 6.2831853f;
		float u = phi / TWO_PI + 0.5f;
		float v = theta / PI_VALUE;
		skyColor = SkyMap2D.SampleLevel(SkyMap2DSampler, float2(u, v), 0).rgb;
	}
	else
	{
		skyColor = SkyCubeMap.SampleLevel(SkyCubeMapSampler, viewDirNorm, 0).rgb;
	}

	if (dot(skyColor, skyColor) < 0.001f)
	{
		skyColor = ReflectionCubeMap.SampleLevel(ReflectionCubeMapSampler, viewDirNorm, 0).rgb;
	}

	return skyColor;
}

// Resolves the noisy SSR target: rgb = reflected colour, a = coverage (0 where the ray missed).
// Each pixel's jittered ray either hits or misses, so neighbours on the same surface are averaged
// (5x5, depth-aware): hit colours weighted by coverage, coverage itself averaged. This turns the
// per-pixel hit/miss pattern into a smooth partial blend instead of speckle.
float4 GetSSR(float2 TexCoord)
{
	int3 texCoord = int3(TexCoord * Resolution, 0);

	float centerDepth = DepthMap.Load(texCoord).r;

	float3 colorAcc = 0;
	float colorWeight = 0;
	float coverageAcc = 0;
	float weightAcc = 0;

	[loop]
	for (int x = -2; x <= 2; x++)
	{
		[loop]
		for (int y = -2; y <= 2; y++)
		{
			int3 sampleCoord = int3(texCoord.x + x, texCoord.y + y, 0);

			float weight = (abs(x) > 1 || abs(y) > 1) ? 0.5f : 1;

			//Stay on the same surface
			float sampleDepth = DepthMap.Load(sampleCoord).r;
			weight *= saturate(1 - abs(sampleDepth - centerDepth) / (centerDepth * 0.02f + 0.0001f));

			float4 reflection = ReflectionMap.Load(sampleCoord);

			coverageAcc += reflection.a * weight;
			weightAcc += weight;

			//Fireflies: bright hits get less say, so a single sky or sun pixel doesn't dominate
			float colorW = reflection.a * weight;
			if (FireflyReduction)
				colorW /= 1 + GetLuma(reflection.rgb) / max(FireflyThreshold, 0.001f);

			colorAcc += reflection.rgb * colorW;
			colorWeight += colorW;
		}
	}

	if (colorWeight <= 0.0001f) return float4(0, 0, 0, 0);

	return float4(colorAcc / colorWeight, coverageAcc / weightAcc);
}

//float GetNormalVariance(float2 texCoord, float3 baseNormal, float offset)
//{
//    float variance = 0;
//
//    float3 normalTest;
//    for (int i = 0; i < SAMPLE_COUNT; i++)
//    {
//        normalTest = NormalMap.Sample(normalSampler, texCoord.xy + offset*
//                     SampleOffsets[i] * InverseResolution).rgb;
//        normalTest = decode(normalTest.xyz);
//
//        variance += 1-dot(baseNormal, normalTest);
//    }
//
//    return variance/SAMPLE_COUNT;
//}

//Mean free distance from each probe along +X/+Y/+Z and -X/-Y/-Z (ProbeVolumeData.DistPos/DistNeg)
Texture3D ProbeDistPos;
Texture3D ProbeDistNeg;

//1 while the shaded point is within the probe's free distance in that direction, falling off
//steeply beyond it, i.e. once a wall is in the way. Same as ProbeVolumeData.Visibility.
float ProbeVisibility(float3 toPoint, float3 distPos, float3 distNeg)
{
	float d = length(toPoint);
	if (d < 0.0001f) return 1;
	float3 dir = toPoint / d;
	//Harmonic blend of the three axis distances facing dir, so the nearest blocker dominates
	float3 axisDistance = max(dir >= 0 ? distPos : distNeg, 0.0001f);
	float free = 1.0f / max(dot(dir * dir, 1.0f / axisDistance), 0.000001f);
	if (d <= free) return 1;
	float ratio = free / d;
	ratio *= ratio;
	return ratio * ratio;
}

//Down-weights probes behind the surface (DDGI's smooth backface term). Same as ProbeVolumeData.BackfaceWeight.
float ProbeBackfaceWeight(float3 toPoint, float3 normalWS)
{
	float d = length(toPoint);
	if (d < 0.0001f) return 1;
	float facing = (dot(-toPoint / d, normalWS) + 1) * 0.5f;
	return facing * facing + 0.2f;
}

//rgb = baked irradiance for a world-space normal, a = blend weight (fades to 0 at the volume's border).
//Blends the 8 surrounding probes by hand so probes that can't see the point (behind a wall, ceiling
//or floor thinner than the probe spacing) are left out instead of leaking their light through it.
float4 SampleProbeVolume(float3 positionWS, float3 normalWS)
{
	float3 uvw = (positionWS - ProbeVolumeMin) * ProbeVolumeExtentRcp;

	//Distance to the nearest face in probe cells; full weight half a cell inside
	float3 edge = min(uvw, 1 - uvw) * ProbeVolumeCells;
	float borderWeight = saturate(min(edge.x, min(edge.y, edge.z)) * 2);
	if (borderWeight <= 0) return float4(0, 0, 0, 0);

	float3 cells = max(ProbeVolumeCells, 1);
	float3 cellSize = 1.0f / (ProbeVolumeExtentRcp * cells);
	float3 grid = saturate(uvw) * cells;
	float3 base = min(floor(grid), cells - 1);
	float3 f = grid - base;

	float4 r = 0, g = 0, b = 0;
	float4 plainR = 0, plainG = 0, plainB = 0;
	float total = 0;

	[unroll]
	for (int i = 0; i < 8; i++)
	{
		float3 corner = float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
		float3 t = lerp(1 - f, f, corner);
		float trilinear = t.x * t.y * t.z;
		int4 index = int4(base + corner, 0);

		float3 toPoint = positionWS - (ProbeVolumeMin + (base + corner) * cellSize);
		float w = trilinear
			* ProbeVisibility(toPoint, ProbeDistPos.Load(index).xyz, ProbeDistNeg.Load(index).xyz)
			* ProbeBackfaceWeight(toPoint, normalWS);

		float4 sr = ProbeSHR.Load(index);
		float4 sg = ProbeSHG.Load(index);
		float4 sb = ProbeSHB.Load(index);
		r += sr * w; g += sg * w; b += sb * w;
		total += w;
		plainR += sr * trilinear; plainG += sg * trilinear; plainB += sb * trilinear;
	}

	//Every neighbour blocked (e.g. a point inside geometry): fall back to plain trilinear
	if (total > 0.00001f) { r /= total; g /= total; b /= total; }
	else { r = plainR; g = plainG; b = plainB; }

	float3 irradiance = float3(r.x + dot(r.yzw, normalWS),
	                           g.x + dot(g.yzw, normalWS),
	                           b.x + dot(b.yzw, normalWS));
	return float4(max(irradiance, 0), borderWeight);
}

		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//  BASE FUNCTIONS
		////////////////////////////////////////////////////////////////////////////////////////////////////////////

PixelShaderOutput PixelShaderFunctionBasic(VertexShaderOutput input)
{
    PixelShaderOutput output;
	int3 texCoordInt = int3(input.Position.xy, 0);
    
    //get normal data from the NormalMap
    float4 normalData = NormalMap.Load(texCoordInt);
    //tranform normal back into [-1,1] range
    float3 normal = decode(normalData.xyz); //2.0f * normalData.xyz - 1.0f;    //could do mad

	//We use this to fake a sky color in the specular component
    if (normalData.x + normalData.y <= 0.001f)
    {
			float3 skyColor = SampleSky(input.ViewDir);
            output.Diffuse = float4(0, 0, 0, 0);
			output.Specular = float4(skyColor, 0) * 0.5f;
            return output;
    }

    //get metalness
    float roughness = normalData.a;
    //get specular intensity from the AlbedoMap
    float4 color = AlbedoMap.Load(texCoordInt);

    float metalness = decodeMetalness(normalData.b);

    float f0 = lerp(0.04f, color.g * 0.25 + 0.75, metalness);

    //float materialType = decodeMattype(color.a);

	//The incoming vector from the camera //EDIT: In world space now
    float3 incident = normalize(input.ViewDir - CameraPositionWS);

	//Transform the reflectionVector from VS to WS
	normal = mul(normal, TransposeView);

	//The reflected vector which points to our cube map
    float3 reflectionVector = reflect(incident, normal);

	//Fresnel
    float VdotH = saturate(dot(normal, incident));
    float fresnel = pow(1.0 - VdotH, 5.0);
    fresnel *= (1.0 - f0);
    fresnel += f0;

    reflectionVector.z = -reflectionVector.z;

    //roughness from 0.05 to 0.5, coarsest of approximations
    float mip = roughness / 0.04f;

	float4 specularReflection = ReflectionCubeMap.SampleLevel(ReflectionCubeMapSampler, reflectionVector, mip);

	specularReflection *= (1 - roughness) * fresnel; //* NdotC * NdotC * NdotC;

	float4 diffuseReflection = ReflectionCubeMap.SampleLevel(ReflectionCubeMapSampler, reflectionVector, 9);

	diffuseReflection *= (roughness) * fresnel; //* NdotC * NdotC * NdotC;

	float ao = 1;

	[branch]
	if (UseSDFAO)
	{
		//Compute WS position 
		float linearDepth = DepthMap.Load(texCoordInt).r;
		float3 PositionWS = CameraPositionWS + linearDepth * input.ViewDir;

		float3 aoDirection = normal;

		float3 random = randomNormal2(input.Position.xy / 2000.0f);

		if (dot(random, normal) < 0) random = -random;

		aoDirection = random;

		ao = RaymarchAO(PositionWS, PositionWS + normalize(aoDirection) * 10, 10);

		ao = smoothstep(0, 1, ao);
	}
	
	float3 diffuseAmbient = diffuseReflection.xyz * EnvironmentMapDiffuseStrength;

	//Baked GI: inside the probe volume the probes' irradiance replaces the cubemap's diffuse term
	[branch]
	if (UseProbeVolume)
	{
		float probeDepth = DepthMap.Load(texCoordInt).r;
		float3 probePositionWS = CameraPositionWS + probeDepth * input.ViewDir;
		float3 probeNormal = normalize(normal);
		//Sample half a cell out along the normal, so a wall's inner face reads the probes in the
		//room it faces rather than blending in the ones on the far side of the wall.
		float3 probeCell = 1.0f / (ProbeVolumeExtentRcp * max(ProbeVolumeCells, 1));
		probePositionWS += probeNormal * probeCell * 0.5f;
		float4 probe = SampleProbeVolume(probePositionWS, probeNormal);
		diffuseAmbient = lerp(diffuseAmbient, probe.rgb * ProbeVolumeIntensity, probe.a);

		//The cubemap shows the surroundings of its capture point. Scale its reflections by how much
		//light the probes see here compared with there, so a closed room doesn't reflect the sunny outdoors.
		float4 n = float4(1, probeNormal);
		float3 captureIrradiance = max(float3(dot(CaptureSHR, n), dot(CaptureSHG, n), dot(CaptureSHB, n)), 0);
		const float3 lumaWeights = float3(0.2126f, 0.7152f, 0.0722f);
		float specularOcclusion = saturate(dot(probe.rgb, lumaWeights) / max(dot(captureIrradiance, lumaWeights), 0.0001f));
		specularReflection.rgb *= lerp(1, specularOcclusion, probe.a);
	}

	//Sample our screen space reflection map and use the environment map only as fallback
	//Blend in by coverage, weighted like the cubemap reflection so a hit and a miss differ only in what is reflected
	float4 ssreflectionMap = GetSSR(input.TexCoord);
	float specularWeight = (1 - roughness) * fresnel;
	specularReflection.rgb = lerp(specularReflection.rgb, ssreflectionMap.rgb * specularWeight * EnvironmentMapSpecularStrengthRcp, ssreflectionMap.a);

    output.Diffuse = float4(diffuseAmbient, 0) * ao;
    output.Specular = float4(specularReflection.xyz, 0) *EnvironmentMapSpecularStrength * (ao * 0.5f + 0.5f);

    return output;
}

PixelShaderOutput PixelShaderFunctionSky(VertexShaderOutput input)
{
	PixelShaderOutput output;
	int3 texCoordInt = int3(input.Position.xy, 0);

	//get normal data from the NormalMap
	float4 normalData = NormalMap.Load(texCoordInt);

	//tranform normal back into [-1,1] range
	float3 normal = decode(normalData.xyz);

	//We use this to render the skybox when there are no geometry pixels
	if (normalData.x + normalData.y <= 0.001f)
	{
		float3 skyColor = SampleSky(input.ViewDir);
		
		//Capture the procedural sky at the brightness the main view shows it (Basic pass: specular * 0.5),
		//so reflections of the day/night sky are never brighter than the sky itself
		if (DayNightCycle)
		{
			output.Diffuse = 0;
			output.Specular = float4(skyColor * 0.5f, 0);
			return output;
		}
		output.Diffuse = float4(skyColor * EnvironmentMapDiffuseStrength, 0);
		output.Specular = float4(skyColor * EnvironmentMapSpecularStrength, 0);
		return output;
	}

	output.Diffuse = 0;
	output.Specular = 0;

	return output;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  TECHNIQUES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

technique Basic
{
    pass Pass1
    {
        VertexShader = compile vs_5_0 VertexShaderFunction();
        PixelShader = compile ps_5_0 PixelShaderFunctionBasic();
    }
}

technique Sky
{
	pass Pass1
	{
		VertexShader = compile vs_5_0 VertexShaderFunction();
		PixelShader = compile ps_5_0 PixelShaderFunctionSky();
	}
}

