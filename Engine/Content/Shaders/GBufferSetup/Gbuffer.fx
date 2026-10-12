////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  GBuffer creation

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VARIABLES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

//#define TESTNORMALENCODING

#include "../Common/helper.fx"

float4x4  World;
float4x4  View;
float4x4  WorldView;
float4x4  WorldViewProj;
float3x3  WorldViewIT; //Inverse Transposed
// Only used by gameobjects with the Foliage Wind component, which move their vertices in world space
float4x4  ViewProjection;

float3 Camera;
float2 Resolution;
float FarClip = 200;

float Roughness = 0.3f;
float Metallic = 0;
int MaterialType = 0;

const float CLIP_VALUE = 0.49;

float4 DiffuseColor = float4(0.8f, 0.8f, 0.8f, 1);
// Component instances tint imported albedo and override scalar surface values.
bool OverrideSurface = false;
float3 SurfaceTint = float3(1, 1, 1);
float2 ComponentSurface;
bool ComponentRoughnessMap = false;
bool ComponentMetallicMap = false;
// Texture repeats across the mesh UVs.
float2 TextureTiling = float2(1, 1);

// Wet Surface material type (see WriteBuffers)
bool WetSurface = false;
// x: wetness (material + weather), y: puddle amount, z: 1 / puddle size in metres,
// w: 1 = puddle height from DisplacementMap, 0 = from the albedo brightness
float4 WetParams = float4(0, 0.5, 0.33, 0);
// Raindrop ripples in the puddles (0 = still water)
float Ripples = 0;
// (Time, the ripple clock, is declared in helper.fx)

#include "../Common/FoliageWind.fx"

Texture2D<float4> Texture;

Texture2D<float4> NormalMap;

Texture2D<float4> MetallicMap;

Texture2D<float4> RoughnessMap;

Texture2D<float4> DisplacementMap;

Texture2D<float4> Mask;

sampler TextureSampler
{
	Texture = (Texture);
	Filter = Anisotropic;
	MaxAnisotropy = 8;
	AddressU = Wrap;
	AddressV = Wrap;
};

sampler TextureSamplerTrilinear
{
	Texture = (NormalMap);
	MagFilter = LINEAR;
	MinFilter = LINEAR;
	Mipfilter = LINEAR;
	AddressU = Wrap;
	AddressV = Wrap;
};

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  STRUCT DEFINITIONS

struct DrawBasic_VSIn
{
	float4 Position : POSITION0;
	float3 Normal   : NORMAL0;
	float2 TexCoord : TEXCOORD0;
};

struct DrawBasic_VSOut
{
    float4 Position : SV_POSITION;
    float3 Normal : NORMAL0;
    float2 TexCoord : TEXCOORD1;
    float Depth : TEXCOORD2;
    float3 WorldPos : TEXCOORD6;
};

struct DrawNormals_VSIn
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float3 Binormal : BINORMAL0;
    float3 Tangent : TANGENT0;
    float2 TexCoord : TEXCOORD0;
};

struct DrawNormals_VSOut
{
    float4 Position : SV_POSITION;
    float3x3 WorldToTangentSpace : TEXCOORD3;
    float2 TexCoord : TEXCOORD1;
    float Depth : TEXCOORD0;
    float3 WorldPos : TEXCOORD6;
};

struct Render_IN
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float3 Normal : TEXCOORD0;
    float2 Depth : DEPTH;
    float Metallic : TEXCOORD1;
    float roughness : TEXCOORD2;
    float3 WorldPos : TEXCOORD3;
    // Interpolated mesh normal (view space) before normal mapping
    float3 GeoNormal : TEXCOORD4;
};

struct PixelShaderOutput
{
    float4 Color : COLOR0;
    float4 Normal : COLOR1;
    float4 Depth : COLOR2;
};


////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  FUNCTION DEFINITIONS

DrawBasic_VSOut DrawBasic_VertexShader(DrawBasic_VSIn input)
{
    DrawBasic_VSOut Output;
    Output.Position = mul(input.Position, WorldViewProj);
	Output.Normal = mul(input.Normal, WorldViewIT);//mul(float4(input.Normal, 0), World).xyz;
    Output.TexCoord = input.TexCoord * TextureTiling;
    Output.WorldPos = mul(input.Position, World).xyz;

	//Linear Depth buffer instead of Z / W
	Output.Depth = mul(input.Position, WorldView).z / -FarClip;//float2(Output.Position.z, Output.Position.w);

    [branch]
    if (WindEnabled)
    {
        float3 tilt;
        float3 worldPos = FoliageWorldPosition(input.Position, input.Normal, input.TexCoord, tilt);
        Output.Position = mul(float4(worldPos, 1), ViewProjection);
        Output.WorldPos = worldPos;
        Output.Depth = mul(float4(worldPos, 1), View).z / -FarClip;
        Output.Normal = normalize(Output.Normal) + mul(tilt, (float3x3)View);
    }
    return Output;
}

DrawNormals_VSOut DrawNormals_VertexShader(DrawNormals_VSIn input)
{
    DrawNormals_VSOut Output;
    Output.Position = mul(input.Position, WorldViewProj);
	Output.WorldToTangentSpace[0] = mul(input.Tangent, WorldViewIT);//mul(normalize(float4(input.Tangent, 0)), World).xyz;
    Output.WorldToTangentSpace[1] = mul(input.Binormal, WorldViewIT);//mul(normalize(float4(input.Binormal, 0)), World).xyz;
    Output.WorldToTangentSpace[2] = mul(input.Normal, WorldViewIT);//mul(normalize(float4(input.Normal, 0)), World).xyz;
    Output.TexCoord = input.TexCoord * TextureTiling;
    Output.WorldPos = mul(input.Position, World).xyz;

	//Linear Depth buffer instead of Z / W
	Output.Depth = mul(input.Position, WorldView).z / -FarClip;//float2(Output.Position.z, Output.Position.w);

    [branch]
    if (WindEnabled)
    {
        float3 tilt;
        float3 worldPos = FoliageWorldPosition(input.Position, input.Normal, input.TexCoord, tilt);
        Output.Position = mul(float4(worldPos, 1), ViewProjection);
        Output.WorldPos = worldPos;
        Output.Depth = mul(float4(worldPos, 1), View).z / -FarClip;
        Output.WorldToTangentSpace[2] = normalize(Output.WorldToTangentSpace[2]) + mul(tilt, (float3x3)View);
    }
    return Output;
}

float3 GetNormalMap(float2 TexCoord)
{
	//This gets normalized anyways, so it doesn't matter that it's technically only half the length
	return NormalMap.Sample(TextureSamplerTrilinear, TexCoord).rgb - float3(0.5f, 0.5f, 0.5f);
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  WET SURFACE

float WetHash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031f);
    p3 += dot(p3, p3.yzx + 33.33f);
    return frac((p3.x + p3.y) * p3.z);
}

float WetNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(WetHash(i), WetHash(i + float2(1, 0)), u.x),
                lerp(WetHash(i + float2(0, 1)), WetHash(i + float2(1, 1)), u.x), u.y);
}

// 0..1, centred on 0.5
float WetFbm(float2 p)
{
    float value = 0;
    float amplitude = 0.5f;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        value += amplitude * WetNoise(p);
        // Rotate each octave so the value-noise grid does not show
        p = mul(p, float2x2(1.6f, 1.2f, -1.2f, 1.6f)) + 7.31f;
        amplitude *= 0.5f;
    }
    return value / 0.9375f;
}

// One layer of raindrop rings, one drop per cell (p in cells). Returns the water slope (world XY).
float2 RippleLayer(float2 p, float time, float density)
{
    float2 cell = floor(p);
    float seed = WetHash(cell);
    float cycle = time * 1.3f + seed;
    float phase = frac(cycle);
    // Every cycle the cell's drop lands somewhere else, or not at all in light rain
    float2 rnd = float2(WetHash(cell + floor(cycle) * 7.13f), WetHash(cell + floor(cycle) * 3.71f + 19.1f));
    float alive = step(WetHash(cell + floor(cycle) * 5.31f + 41.7f), density);
    float2 delta = frac(p) - 0.25f - rnd * 0.5f;
    float d = length(delta) + 0.0001f;
    float x = d - phase * 0.3f;
    // A few rings travelling outwards that fade as they grow
    float envelope = saturate(1 - abs(x + 0.05f) * 10) * (1 - phase) * (1 - phase);
    float slope = sin(x * 70) * envelope;
    return delta / d * slope * alive;
}

void ApplyWetSurface(inout Render_IN input, float2 texCoord)
{
    float wetness = WetParams.x;
    float3 viewUp = normalize(mul(float3(0, 0, 1), (float3x3)View));
    float3 geoNormal = normalize(input.GeoNormal);
    // Water only pools on faces that look up; walls just get damp
    float upFacing = smoothstep(0.75f, 0.95f, dot(geoNormal, viewUp));

    // 0 = cavity (mortar, gaps between stones) .. 1 = top of a brick or stone
    float height = WetParams.w > 0.5f
        ? DisplacementMap.Sample(TextureSampler, texCoord).r
        : saturate(dot(input.Color.rgb, float3(0.3f, 0.59f, 0.11f)) * 2);

    // Puddles fill the low parts of a world-space noise field, cavities first
    float field = lerp(WetFbm(input.WorldPos.xy * WetParams.z), height, 0.3f);
    float coverage = WetParams.y * saturate(wetness * 2 - 1);
    float level = coverage > 0 ? lerp(0.3f, 0.65f, coverage) : -1;
    float puddle = saturate((level - field) / 0.03f) * upFacing;
    float rim = saturate((level + 0.06f - field) / 0.06f) * upFacing;

    // Porous surfaces darken and their micro surface fills with water; cavities soak first
    float damp = saturate(max(wetness * (1.4f - 0.6f * height) * lerp(0.6f, 1, upFacing), rim));

    input.Color.rgb *= lerp(1, 0.45f, damp);
    input.Color.rgb *= lerp(1, 0.6f, puddle);
    input.roughness = lerp(input.roughness, 0.25f, damp * 0.8f);
    // Not lower: the sun's highlight on a sharper mirror overflows the half-float light buffer
    input.roughness = lerp(input.roughness, 0.06f, puddle);
    input.Metallic = lerp(input.Metallic, 0, puddle);

    // A water film softens the normal map; puddles have a flat (rippling) water surface
    float3 normal = normalize(lerp(input.Normal, geoNormal, damp * 0.3f));
    float3 waterNormal = viewUp;
    float distance = input.Depth.x * FarClip;
    [branch]
    if (Ripples > 0.001f && puddle > 0 && distance < 40)
    {
        float2 p = input.WorldPos.xy / 0.3f;
        float2 slope = RippleLayer(p, Time, Ripples) + RippleLayer(p * 1.37f + 5.2f, Time + 0.43f, Ripples);
        slope *= Ripples * saturate(1 - distance / 40);
        waterNormal = normalize(mul(normalize(float3(-slope * 0.9f, 1)), (float3x3)View));
    }
    input.Normal = normalize(lerp(normal, waterNormal, puddle));
}

//See BufferSetup.dgml for overview
PixelShaderOutput WriteBuffers(Render_IN input, float2 texCoord)
{       
    if (OverrideSurface)
    {
        input.Color.rgb *= SurfaceTint;
        input.roughness = ComponentRoughnessMap ? RoughnessMap.Sample(TextureSampler, texCoord).r : ComponentSurface.x;
        input.Metallic = ComponentMetallicMap ? MetallicMap.Sample(TextureSampler, texCoord).r : ComponentSurface.y;
    }
    [branch]
    if (WetSurface)
        ApplyWetSurface(input, texCoord);
    float4 finalValue = input.Color;

    //Deferred MRT

    PixelShaderOutput Out;

    Out.Color = finalValue;

	//Flags Wet Surface pixels for the view-dependent fresnel in DeferredEnvironmentMap
	Out.Color.a = WetSurface ? 1 : 0; //encodeMetallicMattype(input.Metallic, MaterialType);

	//Only use rg with this encoding
    Out.Normal.rg =  encode(input.Normal).xy; //

	//us b as mat type/ metallic
	Out.Normal.b = encodeMetallicMattype(input.Metallic, MaterialType);

	//Test normal encoding
	
//#ifdef TESTNORMALENCODING
//
//	if (input.Position.x % 8 < 4 != input.Position.y % 8 < 4)
//		Out.Normal.rgb = decode(encode(input.Normal));
//	else
//		Out.Normal.rgb = input.Normal.rgb;
//
//	//to range
//	Out.Normal.rgb = (Out.Normal.rgb + float3(1, 1, 1)) * 0.5f;
//
//#endif


    Out.Normal.a = input.roughness;

	Out.Depth = input.Depth.x;

    return Out;
}

[earlydepthstencil]      //experimental
PixelShaderOutput DrawTexture_PixelShader(DrawBasic_VSOut input)
{
    Render_IN renderParams;

    float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
    float4 outputColor = textureColor; //* input.Color;
 
    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.Normal;
    renderParams.Color = outputColor;
    renderParams.Normal = normalize(input.Normal);
    renderParams.Depth = input.Depth;
    renderParams.Metallic = Metallic;
    renderParams.roughness = Roughness;
    
    return WriteBuffers(renderParams, input.TexCoord);
}

[earlydepthstencil]      //experimental
PixelShaderOutput DrawTextureSpecular_PixelShader(DrawBasic_VSOut input) 
{
    Render_IN renderParams;

    float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
    float4 outputColor = textureColor; //* input.Color;

    float RoughnessTexture = RoughnessMap.Sample(TextureSampler, input.TexCoord).r;

    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.Normal;
    renderParams.Color = outputColor;
    renderParams.Normal = normalize(input.Normal);
    renderParams.Depth = input.Depth;
    renderParams.Metallic = Metallic;
    renderParams.roughness = RoughnessTexture;

    return WriteBuffers(renderParams, input.TexCoord);
}

[earlydepthstencil]      //experimental
PixelShaderOutput DrawTextureSpecularMetallic_PixelShader(DrawBasic_VSOut input) 
{
    Render_IN renderParams;

    float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
    float4 outputColor = textureColor; //* input.Color;

    float RoughnessTexture = RoughnessMap.Sample(TextureSampler, input.TexCoord).r;
    float metallicTexture = MetallicMap.Sample(TextureSampler, input.TexCoord).r;

    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.Normal;
    renderParams.Color = outputColor;
    renderParams.Normal = normalize(input.Normal);
    renderParams.Depth = input.Depth;
    renderParams.Metallic = metallicTexture;
    renderParams.roughness = RoughnessTexture;

    return WriteBuffers(renderParams, input.TexCoord);
}

[earlydepthstencil]      //experimental
PixelShaderOutput DrawTextureSpecularNormal_PixelShader(DrawNormals_VSOut input) 
{
    Render_IN renderParams;

    float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
    float4 outputColor = textureColor; //* input.Color;

    float3x3 worldSpace = input.WorldToTangentSpace;

    float RoughnessTexture = RoughnessMap.Sample(TextureSampler, input.TexCoord).r;

    // NORMAL MAP ////
	float3 normalMap = GetNormalMap(input.TexCoord);
    normalMap = normalize(mul(normalMap, worldSpace));
  
    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.WorldToTangentSpace[2];
    renderParams.Color = outputColor;
    renderParams.Normal = normalMap;
    renderParams.Depth = input.Depth;
    renderParams.Metallic = Metallic;
    renderParams.roughness = RoughnessTexture;

    return WriteBuffers(renderParams, input.TexCoord);
}

[earlydepthstencil]      //experimental
PixelShaderOutput DrawTextureSpecularNormalMetallic_PixelShader(DrawNormals_VSOut input) 
{
    Render_IN renderParams;

    float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
    float4 outputColor = textureColor; //* input.Color;

    float3x3 worldSpace = input.WorldToTangentSpace;

    float RoughnessTexture = RoughnessMap.Sample(TextureSampler, input.TexCoord).r;
    float metallicTexture = MetallicMap.Sample(TextureSampler, input.TexCoord).r;
    // NORMAL MAP ////
    float3 normalMap = GetNormalMap(input.TexCoord);
    normalMap = normalize(mul(normalMap, worldSpace));
  
    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.WorldToTangentSpace[2];
    renderParams.Color = outputColor;
    renderParams.Normal = normalMap;
    renderParams.Depth = input.Depth;
    renderParams.Metallic = metallicTexture;
    renderParams.roughness = RoughnessTexture;

    return WriteBuffers(renderParams, input.TexCoord);
}



[earlydepthstencil]      //experimental
PixelShaderOutput DrawTextureNormal_PixelShader(DrawNormals_VSOut input) 
{
    Render_IN renderParams;

    float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
    float4 outputColor = textureColor; //* input.Color;

    float3x3 worldSpace = input.WorldToTangentSpace;

    // NORMAL MAP ////
    float3 normalMap = GetNormalMap(input.TexCoord);
    normalMap = normalize(mul(normalMap, worldSpace));
  
    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.WorldToTangentSpace[2];
    renderParams.Color = outputColor;
    renderParams.Normal = normalMap;
    renderParams.Depth = input.Depth;
    renderParams.Metallic = Metallic;
    renderParams.roughness = Roughness;
    
    return WriteBuffers(renderParams, input.TexCoord);
}

[earlydepthstencil]      //experimental
PixelShaderOutput DrawNormal_PixelShader(DrawNormals_VSOut input)
{
	Render_IN renderParams;
	float4 outputColor = DiffuseColor; //* input.Color;

	float3x3 worldSpace = input.WorldToTangentSpace;

	// NORMAL MAP ////
	float3 normalMap = GetNormalMap(input.TexCoord);
	normalMap = normalize(mul(normalMap, worldSpace));

	renderParams.Position = input.Position;
	renderParams.WorldPos = input.WorldPos;
	renderParams.GeoNormal = input.WorldToTangentSpace[2];
	renderParams.Color = outputColor;
	renderParams.Normal = normalMap;
	renderParams.Depth = input.Depth;
	renderParams.Metallic = Metallic;
	renderParams.roughness = Roughness;

	return WriteBuffers(renderParams, input.TexCoord);
}

      //experimental
PixelShaderOutput DrawTextureMask_PixelShader(DrawBasic_VSOut input) 
{
    Render_IN renderParams;

    float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
    float4 outputColor = textureColor; //* input.Color;

    float mask = Mask.Sample(TextureSampler, input.TexCoord).r;
    if (mask < CLIP_VALUE)
        clip(-1);
 
    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.Normal;
    renderParams.Color = outputColor;
    renderParams.Normal = normalize(input.Normal);
    renderParams.Depth = input.Depth;
    renderParams.Metallic = Metallic;
    renderParams.roughness = Roughness;
    
    return WriteBuffers(renderParams, input.TexCoord);
}


PixelShaderOutput DrawTextureSpecularMask_PixelShader(DrawBasic_VSOut input) 
{
    Render_IN renderParams;

	float mask = Mask.Sample(TextureSampler, input.TexCoord).r;
	if (mask < CLIP_VALUE)
	clip(-1);

    float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
    float4 outputColor = textureColor; //* input.Color;

    float RoughnessTexture = RoughnessMap.Sample(TextureSampler, input.TexCoord).r;

    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.Normal;
    renderParams.Color = outputColor;
    renderParams.Normal = normalize(input.Normal);
    renderParams.Depth = input.Depth;
    renderParams.Metallic = Metallic;
    renderParams.roughness = RoughnessTexture; // 1 - (RoughnessTexture.r+RoughnessTexture.b+RoughnessTexture.g) / 3;
    
    return WriteBuffers(renderParams, input.TexCoord);
}

      //experimental
PixelShaderOutput DrawTextureSpecularNormalMask_PixelShader(DrawNormals_VSOut input) 
{
    Render_IN renderParams;

	float mask = Mask.Sample(TextureSampler, input.TexCoord).r;
	if (mask < CLIP_VALUE)
	clip(-1);

    float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
    float4 outputColor = textureColor; //* input.Color;

    float3x3 worldSpace = input.WorldToTangentSpace;

    float RoughnessTexture = RoughnessMap.Sample(TextureSampler, input.TexCoord).r;


    // NORMAL MAP ////
    float3 normalMap = GetNormalMap(input.TexCoord);
    normalMap = normalize(mul(normalMap, worldSpace));
  
    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.WorldToTangentSpace[2];
    renderParams.Color = outputColor;
    renderParams.Normal = normalMap;
    renderParams.Depth = input.Depth;
    renderParams.Metallic = Metallic;
    renderParams.roughness = RoughnessTexture;

    return WriteBuffers(renderParams, input.TexCoord);
}

    //experimental
PixelShaderOutput DrawTextureNormalMask_PixelShader(DrawNormals_VSOut input) 
{
    Render_IN renderParams;

	float mask = Mask.Sample(TextureSampler, input.TexCoord).r;

	//Branching has shown to make no difference here
	if (mask < CLIP_VALUE)
	{
		clip(-1);
	}

	float4 textureColor = Texture.Sample(TextureSampler, input.TexCoord);
	float4 outputColor = textureColor; //* input.Color;

	float3x3 worldSpace = input.WorldToTangentSpace;


	// NORMAL MAP ////
	float3 normalMap = GetNormalMap(input.TexCoord);
	normalMap = normalize(mul(normalMap, worldSpace));

	renderParams.Position = input.Position;
	renderParams.WorldPos = input.WorldPos;
	renderParams.GeoNormal = input.WorldToTangentSpace[2];
	renderParams.Color = outputColor;
	renderParams.Normal = normalMap;
	renderParams.Depth = input.Depth;
	renderParams.Metallic = Metallic;
	renderParams.roughness = Roughness;

	return WriteBuffers(renderParams, input.TexCoord);
}

PixelShaderOutput DrawBasic_PixelShader(DrawBasic_VSOut input)
{
    Render_IN renderParams;

    float4 outputColor = DiffuseColor; //* input.Color;

         
    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.Normal;
    renderParams.Color = outputColor;
    renderParams.Normal = normalize(input.Normal);
    renderParams.Depth = input.Depth;
    renderParams.Metallic = Metallic;
    renderParams.roughness = Roughness;

    return WriteBuffers(renderParams, input.TexCoord);
}


//DISPLACEMENT / POM
PixelShaderOutput DrawTextureDisplacement_PixelShader(DrawNormals_VSOut input) 
{
    Render_IN renderParams;

    float3x3 worldToTangent = input.WorldToTangentSpace;

    float3 tangentPos = mul(input.Position.xyz, worldToTangent);
    float3 tangentCamera = mul(Camera, worldToTangent);
    
    float3 viewDir = normalize(tangentPos - tangentCamera);
    //POM
    float height = DisplacementMap.Sample(TextureSampler, input.TexCoord).r;
    float2 p = viewDir.xy / viewDir.z * height * 0.01f;

    float2 texCoordPOM = input.TexCoord - p;

    //float RoughnessTexture = RoughnessMap.Sample(TextureSampler, input.TexCoord).r;
    //float metallicTexture = MetallicMap.Sample(TextureSampler, input.TexCoord).r;

    
    float4 textureColor = Texture.Sample(TextureSampler, texCoordPOM);
    float4 outputColor = textureColor; //* input.Color;

    // NORMAL MAP ////
    float3 normalMap = GetNormalMap(input.TexCoord);
    normalMap = normalize(mul(normalMap, worldToTangent));

    renderParams.Position = input.Position;
    renderParams.WorldPos = input.WorldPos;
    renderParams.GeoNormal = input.WorldToTangentSpace[2];
    renderParams.Color = outputColor;
    renderParams.Normal = normalMap;
    renderParams.Depth = input.Depth;
    renderParams.Metallic = Metallic;
    renderParams.roughness = Roughness;

    return WriteBuffers(renderParams, texCoordPOM);
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  TECHNIQUES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

technique DrawBasic
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawBasic_VertexShader();
        PixelShader = compile ps_5_0 DrawBasic_PixelShader();
    }
}

technique DrawTexture
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawBasic_VertexShader();
        PixelShader = compile ps_5_0 DrawTexture_PixelShader();
    }
}

technique DrawTextureSpecular
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawBasic_VertexShader();
        PixelShader = compile ps_5_0 DrawTextureSpecular_PixelShader();
    }
}

technique DrawTextureSpecularMetallic
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawBasic_VertexShader();
        PixelShader = compile ps_5_0 DrawTextureSpecularMetallic_PixelShader();
    }
}

technique DrawTextureSpecularNormal
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawNormals_VertexShader();
        PixelShader = compile ps_5_0 DrawTextureSpecularNormal_PixelShader();
    }
}

technique DrawTextureSpecularNormalMetallic
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawNormals_VertexShader();
        PixelShader = compile ps_5_0 DrawTextureSpecularNormalMetallic_PixelShader();
    }
}


technique DrawTextureNormal
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawNormals_VertexShader();
        PixelShader = compile ps_5_0 DrawTextureNormal_PixelShader();
    }
}

technique DrawNormal
{
	pass Pass1
	{
		VertexShader = compile vs_4_0 DrawNormals_VertexShader();
		PixelShader = compile ps_5_0 DrawNormal_PixelShader();
	}
}

technique DrawTextureMask
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawBasic_VertexShader();
        PixelShader = compile ps_4_0 DrawTextureMask_PixelShader();
    }
}

technique DrawTextureSpecularMask
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawBasic_VertexShader();
        PixelShader = compile ps_4_0 DrawTextureSpecularMask_PixelShader();
    }
}

technique DrawTextureSpecularNormalMask
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawNormals_VertexShader();
        PixelShader = compile ps_4_0 DrawTextureSpecularNormalMask_PixelShader();
    }
}

technique DrawTextureNormalMask
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawNormals_VertexShader();
        PixelShader = compile ps_4_0 DrawTextureNormalMask_PixelShader();
    }
}

technique DrawTextureDisplacement
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 DrawNormals_VertexShader();
        PixelShader = compile ps_4_0 DrawTextureDisplacement_PixelShader();
    }
}
