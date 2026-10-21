//Environment cube maps, TheKosmonaut 2016

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VARIABLES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "../Common/helper.fx"
#include "../Common/sdf.fx"
#include "../Common/clouds.fx"
#include "../Common/ProbeVolume.fx"
#include "../Common/Reflections.fx"
#include "../Common/AoProxies.fx"

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

//Screen space ambient occlusion (half resolution, bilateral upsampled). It darkens indirect light only.
Texture2D SSAOMap;
bool UseSSAO = true;

//Lightmaps (Source 2 look, phase 4.2): the lightmap pass's target. rgb = baked indirect irradiance (probe units) for the
//G-buffer normal, a = 1 + baked AO on Lightmap Static surfaces, 0 elsewhere.
Texture2D LightmapMap;
bool UseLightmaps = false;
float LightmapIntensity = 1;

SamplerState LinearClampSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = POINT;
};


bool FireflyReduction;
float FireflyThreshold = 0.1f;

float EnvironmentMapSpecularStrength = 1.0f;
float EnvironmentMapSpecularStrengthRcp = 1.0f;
float EnvironmentMapDiffuseStrength = 0.2f;

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
//Image skybox sun (EnvironmentSettings.SkyboxSun*): towards the brightest directional light, linear light colour
float3 SkySunDirection = float3(0, 0, 1);
float3 SkySunColor = float3(1, 1, 1);
float SkySunStrength = 0;
float2 SkySunDisc = float2(0.9995, 0.9998);
float2 SkySunCone = float2(0.9928, 0.9988);

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

//The main view's environment pass also keeps its indirect diffuse (rgb) and ambient occlusion (a) for the debug views
struct PixelShaderOutputIndirect
{
    float4 Diffuse : COLOR0;
    float4 Specular : COLOR1;
    float4 Indirect : COLOR2;
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

//An image sky is LDR, so its sun is no brighter than a white cloud. Around the directional light, push the image's
//near-white pixels into HDR and add a disc + glare, so looking at the light blooms like the real sun. Only the visible
//sky gets it: in captures the directional light already draws the sun's highlight, and the disc would add fill light.
float3 AddSkySun(float3 skyColor, float3 viewDir)
{
	if (DayNightCycle || SkySunStrength <= 0) return skyColor;
	float cosSun = dot(normalize(viewDir), SkySunDirection);
	float peak = max(skyColor.r, max(skyColor.g, skyColor.b));
	float bright = smoothstep(0.7f, 1.0f, peak);
	skyColor *= 1 + smoothstep(SkySunCone.x, SkySunCone.y, cosSun) * bright * 150 * SkySunStrength;
	//The disc shows dimmer where the image has cloud over the sun. Bloom spreads it into the glare you squint at.
	float disc = smoothstep(SkySunDisc.x, SkySunDisc.y, cosSun) * lerp(0.25f, 1, smoothstep(0.4f, 0.9f, peak));
	float glare = pow(saturate(cosSun), 2048) * 25 + pow(saturate(cosSun), 256) * 2 + pow(saturate(cosSun), 32) * 0.15f;
	return skyColor + (disc * 400 + glare) * SkySunColor * SkySunStrength;
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




		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//  BASE FUNCTIONS
		////////////////////////////////////////////////////////////////////////////////////////////////////////////

PixelShaderOutputIndirect PixelShaderFunctionBasic(VertexShaderOutput input)
{
    PixelShaderOutputIndirect output;
	int3 texCoordInt = int3(input.Position.xy, 0);
    output.Indirect = float4(0, 0, 0, 1);

    float4 normalData = NormalMap.Load(texCoordInt);
    float3 normal = decode(normalData.xyz);

	//Sky: show it in the specular buffer (DeferredCompose adds it unlit). Captures drew it already.
    if (normalData.x + normalData.y <= 0.001f)
    {
            output.Diffuse = float4(0, 0, 0, 0);
			output.Specular = CaptureMode ? float4(0, 0, 0, 0) : float4(AddSkySun(SampleSky(input.ViewDir), input.ViewDir), 0) * 0.5f;
            return output;
    }

    float roughness = normalData.a;
    float4 color = AlbedoMap.Load(texCoordInt);
    float metalness = decodeMetalness(normalData.b);
    float3 f0 = GetF0(AlbedoToLinear(color.rgb), metalness);

    //Wet Surface pixels (material type 7): puddle reflections are kept above the probe's unlit ground below
    bool wetSurface = abs(decodeMattype(normalData.b) - 7) < 0.1f;

	//View ray from the camera, world space (ViewDir is relative to the camera)
    float3 incident = normalize(input.ViewDir);
	float linearDepth = DepthMap.Load(texCoordInt).r;
	float3 positionWS = CameraPositionWS + linearDepth * input.ViewDir;

	//View space -> world space
	normal = normalize(mul(normal, TransposeView));

    float3 reflectionVector = reflect(incident, normal);
    float NdotV = saturate(dot(normal, -incident));

    [flatten]
    if (wetSurface)
        reflectionVector = normalize(float3(reflectionVector.xy, max(reflectionVector.z, 0.2f)));

    //Split sum: F0 scale and bias of the GGX BRDF integrated over the hemisphere
    float2 envBRDF = UseBrdfLut ? BrdfLut.SampleLevel(BrdfSampler, float2(NdotV, roughness), 0).rg : EnvBRDFApprox(NdotV, roughness);
    float3 specularWeight = f0 * envBRDF.x + envBRDF.y;

    //Diffuse: the global probe's roughest mip around the normal stands in for the irradiance.
    //Diffuse Strength 0.2 (EnvironmentSample's default) is the surroundings' full irradiance
    float3 diffuseAmbient = ReflectionCubeMap.SampleLevel(ReflectionCubeMapSampler, float3(normal.xy, -normal.z), GlobalMaxMip).rgb * (EnvironmentMapDiffuseStrength * 5);
    //Light reflected at the surface does not enter it
    float3 diffuseWeight = 1 - FresnelSchlickRoughness(f0, NdotV, roughness);
    diffuseAmbient *= diffuseWeight;

	//Lightmapped pixel: its baked light already contains the occlusion around it, so no screen space AO on top
	float4 lightmap = 0;
	[branch]
	if (UseLightmaps && !CaptureMode)
		lightmap = LightmapMap.Load(texCoordInt);
	bool lightmapped = lightmap.a > 0.5f;

	float ao = 1;

	[branch]
	if (UseSDFAO && !CaptureMode && !lightmapped)
	{
		float3 random = randomNormal2(input.Position.xy / 2000.0f);
		if (dot(random, normal) < 0) random = -random;

		ao = RaymarchAO(positionWS, positionWS + normalize(random) * 10, 10);
		ao = smoothstep(0, 1, ao);
	}

	[branch]
	if (UseSSAO && !CaptureMode && !lightmapped)
		ao *= SSAOMap.SampleLevel(LinearClampSampler, input.TexCoord, 0).r;

	//The material's own AO map (albedo alpha) and the AO proxies of dynamic objects: indirect light only
	ao *= saturate(color.a);
	[branch]
	if (!CaptureMode)
		ao *= AoProxyOcclusion(positionWS, normal);

	//Baked GI: inside the probe volume the probes' irradiance replaces the cubemap's diffuse term
	float3 pixelIrradiance = 0;
	float irradianceWeight = 0;
	//Baked AO of the lightmap: only for reflections (its irradiance is already occluded)
	float specularAo = ao;
	[branch]
	if (lightmapped)
	{
		//Lightmap Static surface: the lightmap's irradiance replaces the probes (they are for dynamic objects)
		diffuseAmbient = lightmap.rgb * LightmapIntensity * diffuseWeight;
		pixelIrradiance = lightmap.rgb;
		irradianceWeight = 1;
		specularAo = ao * saturate(lightmap.a - 1);
	}
	else if (UseProbeVolume)
	{
		//Sample half a cell out along the normal, so a wall's inner face reads the probes in the
		//room it faces rather than blending in the ones on the far side of the wall.
		float3 probeCell = 1.0f / (ProbeVolumeExtentRcp * max(ProbeVolumeCells, 1));
		float4 probe = SampleProbeVolume(positionWS + normal * probeCell * 0.5f, normal);
		diffuseAmbient = lerp(diffuseAmbient, probe.rgb * ProbeVolumeIntensity * diffuseWeight, probe.a);
		pixelIrradiance = probe.rgb;
		irradianceWeight = probe.a;
	}

	output.Diffuse = float4(diffuseAmbient * ao, 0);
    output.Indirect = float4(diffuseAmbient * ao, specularAo);

	[branch]
	if (CaptureMode)
	{
		output.Specular = 0;
		return output;
	}

	float3 specularReflection = SampleReflections(positionWS, reflectionVector, normal, roughness, UseProbeVolume,
		pixelIrradiance, irradianceWeight) * specularWeight;

	//Screen space reflections where the ray hit, the cubemaps elsewhere; both weighted by the same BRDF
	float4 ssreflectionMap = GetSSR(input.TexCoord);
	specularReflection = lerp(specularReflection, ssreflectionMap.rgb * specularWeight * EnvironmentMapSpecularStrengthRcp, ssreflectionMap.a);

    float specularOcclusion = SpecularOcclusion(NdotV, specularAo, roughness);
    output.Specular = float4(specularReflection * EnvironmentMapSpecularStrength * specularOcclusion, 0);

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

