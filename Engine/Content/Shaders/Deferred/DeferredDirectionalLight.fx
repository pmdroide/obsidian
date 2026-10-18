//Lightshader TheKosmonaut 2016
//Cascaded sun shadows: Source 2 look, Docs/Research/Source2Look phase 2.1

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VARIABLES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "../Common/helper.fx"

float4x4 ViewProjection;

//color of the light
float3 lightColor;
//position of the camera, for specular light
float3 cameraPosition = float3(0,0,0);
//this is used to compute the world-position
float4x4 InvertViewProjection;

float3 LightVector;
//control the brightness of the light
float lightIntensity = 1.0f;

// diffuse color, and specularIntensity in the alpha channel
Texture2D AlbedoMap;
// normals, and specularPower in the alpha channel
Texture2D NormalMap;

//depth
Texture2D DepthMap;

//Cascade atlas: cascade i sits in tile (i % 2, i / 2), ShadowMapSize texels each. Stores light view Z / -far clip.
Texture2D ShadowMap;

#define MAX_CASCADES 4
//View space -> light clip / light view space of each cascade
float4x4 CascadeViewProjection[MAX_CASCADES];
float4x4 CascadeView[MAX_CASCADES];
//View distance where each cascade ends, the light view far clip of each, world size of one of its texels
float4 CascadeEnds = float4(10, 30, 80, 150);
float4 CascadeFarClip = float4(100, 100, 100, 100);
float4 CascadeTexelSize = float4(0.05, 0.1, 0.2, 0.4);
float CascadeCount = 4;
//Share of each cascade's depth range at its far end that blends into the next one
float CascadeBlend = 0.15f;
//Receivers are moved along their normal by this many shadow texels before the lookup (no acne on slopes)
float NormalBias = 1.25f;
//... and towards the camera (Source 2: csm_viewdir_shadow_bias), in texels
float ViewDirectionBias = 0.5f;

int ShadowFiltering = 0; //PCF, PCF(3), PCF(7), Poisson

float ShadowMapSize = 2048;

SamplerState pointSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = POINT;
    MinFilter = POINT;
    Mipfilter = POINT;
};

SamplerState linearSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  FOLIAGE TRANSLUCENCY

// Foliage material type (MaterialEffect.MaterialTypes.Foliage): thin leaves let sunlight through.
static const float FOLIAGE_MATERIAL_TYPE = 8;

bool IsFoliage(float encodedMetallicMattype)
{
    return abs(decodeMattype(encodedMetallicMattype) - FOLIAGE_MATERIAL_TYPE) < 0.1f;
}

// Light that reaches a leaf through its back (the side away from the camera), brightest when looking
// towards the sun through the crown. Tinted by the leaf's albedo in DeferredCompose like diffuse light.
float3 FoliageTransmission(float3 normal, float3 cameraDirection)
{
    float back = saturate(dot(normal, LightVector));
    float towardsSun = saturate(dot(cameraDirection, LightVector));
    towardsSun *= towardsSun;
    return lightColor * lightIntensity * back * (0.2f + 0.8f * towardsSun * towardsSun) * 0.45f / PBR_PI;
}

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
    float3 ViewDir : TEXCOORD1;
};

struct PixelShaderOutput
{
    float4 Diffuse : COLOR0;
    float4 Specular : COLOR1;
};

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VERTEX SHADER

VertexShaderOutput VertexShaderFunction(VertexShaderInput input, uint id:SV_VERTEXID)
{
	VertexShaderOutput output;
	output.Position = float4(input.Position, 0, 1);
	output.TexCoord.x = (float)(id / 2) * 2.0;
	output.TexCoord.y = 1.0 - (float)(id % 2) * 2.0;

	output.ViewDir = GetFrustumRay(id);
	return output;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  SHADOWS

float random(float4 seed4)
{
	float dot_product = dot(seed4, float4(12.9898, 78.233, 45.164, 94.673));
	return frac(sin(dot_product) * 43758.5453);
}

//Depth test of one atlas texel, clamped into the cascade's tile so filters never read a neighbouring cascade
float ShadowTest(int2 texel, int2 tileMin, float depth)
{
	texel = clamp(texel, tileMin, tileMin + (int)ShadowMapSize - 1);
	return depth <= ShadowMap.Load(int3(texel, 0)).r;
}

//Bilinear-weighted box PCF over (2 * radius + 1)^2 texels around the sample point
float FilterPCF(float2 texelPos, int2 tileMin, float depth, int radius)
{
	float2 fractionals = frac(texelPos - 0.5f);
	float2 complement = 1 - fractionals;
	int2 base = int2(floor(texelPos - 0.5f));
	float sum = 0;

	[loop]
	for (int y = -radius; y <= radius + 1; y++)
	{
		[loop]
		for (int x = -radius; x <= radius + 1; x++)
		{
			float wx = x == -radius ? complement.x : x == radius + 1 ? fractionals.x : 1;
			float wy = y == -radius ? complement.y : y == radius + 1 ? fractionals.y : 1;
			sum += ShadowTest(base + int2(x, y), tileMin, depth) * wx * wy;
		}
	}
	float size = 2 * radius + 1;
	return sum / (size * size);
}

float FilterPoisson(float2 texelPos, int2 tileMin, float depth, float2 screenSeed)
{
	const float2 poissonDisk[16] =
	{
		float2(0.1908291f, 0.1823764f), float2(0.4236465f, 0.76107f), float2(-0.3056469f, 0.5557697f),
		float2(-0.4979181f, 0.1770361f), float2(0.4962559f, -0.2154941f), float2(0.6897131f, 0.4324413f),
		float2(-0.3782056f, -0.3405231f), float2(0.04382932f, -0.2403435f), float2(0.886423f, -0.05176726f),
		float2(0.4599024f, -0.6679791f), float2(-0.8389286f, -0.4176486f), float2(-0.9797052f, -0.0152119f),
		float2(-0.2747172f, -0.7914276f), float2(-0.7316247f, 0.6114004f), float2(-0.220655f, 0.9378002f),
		float2(0.1389218f, -0.8920172f)
	};
	float sum = 0;
	[unroll]
	for (uint i = 0; i < 8; i++)
	{
		int index = int(16.0 * random(float4(screenSeed.xyy, i))) % 16;
		sum += ShadowTest(int2(texelPos + poissonDisk[index] * 1.5f), tileMin, depth);
	}
	return sum / 8.0f;
}

//1 = lit. positionVS / normalVS: the receiver in view space.
float SampleCascade(int cascade, float3 positionVS, float3 normalVS, float ndotl, float2 screenSeed)
{
	float texel = CascadeTexelSize[cascade];
	float3 toCamera = normalize(-positionVS);
	float3 p = positionVS + normalVS * texel * NormalBias + toCamera * texel * ViewDirectionBias;

	float4 positionLVP = mul(float4(p, 1), CascadeViewProjection[cascade]);
	float4 positionLV = mul(float4(p, 1), CascadeView[cascade]);
	float depth = positionLV.z / -CascadeFarClip[cascade];

	float2 uv = mad(positionLVP.xy / positionLVP.w, 0.5f, float2(0.5f, 0.5f));
	uv.y = 1 - uv.y;
	if (any(uv < 0) || any(uv > 1)) return 1;

	//Slope bias: a tilted receiver changes depth by tan(angle) per texel across the filter
	int kernel = ShadowFiltering == 0 ? 0 : ShadowFiltering == 2 ? 2 : 1;
	float tanAngle = min(sqrt(saturate(1 - ndotl * ndotl)) / max(ndotl, 1e-3), 4);
	depth -= texel / CascadeFarClip[cascade] * (0.5f + (kernel + 1) * tanAngle * 0.5f);

	int2 tileMin = int2(cascade % 2, cascade / 2) * (int)ShadowMapSize;
	float2 texelPos = tileMin + uv * ShadowMapSize;

	[branch]
	if (ShadowFiltering == 3)
		return FilterPoisson(texelPos, tileMin, depth, screenSeed);
	return FilterPCF(texelPos, tileMin, depth, kernel);
}

//Sun shadow at a view space point: the cascade covering its distance, blended into the next near its far end
//(after the last one: lit, so the shadows fade out at the Shadow Distance)
float SunShadow(float3 positionVS, float3 normalVS, float ndotl, float2 screenSeed)
{
	float viewZ = -positionVS.z;
	int count = (int)CascadeCount;
	int cascade = count;
	[unroll]
	for (int i = MAX_CASCADES - 1; i >= 0; i--)
		if (i < count && viewZ < CascadeEnds[i]) cascade = i;
	if (cascade >= count) return 1;

	float shadow = SampleCascade(cascade, positionVS, normalVS, ndotl, screenSeed);

	float start = cascade == 0 ? 0 : CascadeEnds[cascade - 1];
	float end = CascadeEnds[cascade];
	float blendStart = end - (end - start) * CascadeBlend;
	[branch]
	if (viewZ > blendStart)
	{
		float t = saturate((viewZ - blendStart) / max(end - blendStart, 1e-3f));
		float next = cascade + 1 < count ? SampleCascade(cascade + 1, positionVS, normalVS, ndotl, screenSeed) : 1;
		shadow = lerp(shadow, next, t);
	}
	return shadow;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  PIXEL SHADERS

PixelShaderOutput PixelShaderUnshadowedFunction(VertexShaderOutput input)
{
    PixelShaderOutput output;
    float2 texCoord = float2(input.TexCoord);

    float4 normalData = NormalMap.Sample(pointSampler, texCoord);
    float3 normal = decode(normalData.xyz);

    [branch]
    if (normalData.x + normalData.y <= 0.001f) //Out of range
    {
        output.Diffuse = float4(0, 0, 0, 0);
        output.Specular = float4(0, 0, 0, 0);
        return output;
    }
    else
    {
        float roughness = normalData.a;
        float4 color = AlbedoMap.Sample(pointSampler, texCoord);
        float metalness = decodeMetalness(normalData.b);
        float3 f0 = GetF0(AlbedoToLinear(color.rgb), metalness);

        float3 cameraDirection = -normalize(input.ViewDir);

        float3 diffuse, specular;
        EvaluateLight(normal, -LightVector, cameraDirection, lightColor * lightIntensity, f0, roughness, 1, diffuse, specular);

        float3 transmission = IsFoliage(normalData.b) ? FoliageTransmission(normal, cameraDirection) : float3(0, 0, 0);

        output.Diffuse = float4(diffuse + transmission, 0) * 0.1f;
        output.Specular = float4(specular, 0) * 0.1f;

        return output;
    }
}

PixelShaderOutput PixelShaderShadowedFunction(VertexShaderOutput input)
{
    PixelShaderOutput output;
    float2 texCoord = float2(input.TexCoord);

    float4 normalData = NormalMap.Sample(pointSampler, texCoord);
    float3 normal = decode(normalData.xyz);

    if (normalData.x + normalData.y <= 0.001f) //Out of range
    {
        output.Diffuse = float4(0, 0, 0, 0);
        output.Specular = float4(0, 0, 0, 0);
        return output;
    }

    float NdL = saturate(dot(normal, -LightVector));
    bool foliage = IsFoliage(normalData.b);
    // Foliage lets light through its back, so it needs the shadow there too
    float shadowNdL = foliage ? max(NdL, saturate(dot(normal, LightVector))) : NdL;

	float linearDepth = DepthMap.Sample(pointSampler, texCoord).r;
	float3 positionVS = input.ViewDir * linearDepth;

	float shadowContribution = 0;
    [branch]
    if (shadowNdL > 0)
        shadowContribution = SunShadow(positionVS, normal, shadowNdL, texCoord);

    float roughness = normalData.a;
    float4 color = AlbedoMap.Sample(pointSampler, texCoord);
	float metalness = decodeMetalness(normalData.b);
    float3 f0 = GetF0(AlbedoToLinear(color.rgb), metalness);

    float3 cameraDirection = -normalize(input.ViewDir);

    float3 diffuse = float3(0,0,0);
    float3 specular = float3(0, 0, 0);

    [branch]
    if(shadowContribution > 0)
    {
        EvaluateLight(normal, -LightVector, cameraDirection, lightColor * lightIntensity, f0, roughness, 1, diffuse, specular);
    }

    float3 transmission = foliage ? FoliageTransmission(normal, cameraDirection) : float3(0, 0, 0);

    output.Diffuse = float4(diffuse + transmission, 0) * 0.1f * shadowContribution;
    output.Specular = float4(specular, 0) * 0.1f * shadowContribution;

    return output;
}

technique Unshadowed
{
    pass Pass1
    {
        VertexShader = compile vs_5_0 VertexShaderFunction();
        PixelShader = compile ps_5_0 PixelShaderUnshadowedFunction();
    }
}

technique Shadowed
{
    pass Pass1
    {
        VertexShader = compile vs_5_0 VertexShaderFunction();
        PixelShader = compile ps_5_0 PixelShaderShadowedFunction();
    }
}
