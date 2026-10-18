// Forward shaded materials (MaterialEffect.MaterialTypes.ForwardShaded: glass, transparent plastics), lit like the
// deferred path (Source 2 look, Docs/Research/Source2Look phase 3.4): the material's colour/texture, roughness and
// metalness; the sun with its cascaded shadows; point and spot lights with atlas shadows and cookies; image based light from the
// probe volume and the reflection probes; the froxel fog in front of it. Output is premultiplied: reflections stay
// visible on clear glass. Light units match DeferredCompose's output (the light buffers' 1/10 scale).

#define MAXLIGHTS 40

#include "../Common/helper.fx"
#include "../Common/ProbeVolume.fx"
#include "../Common/Reflections.fx"
#include "../Common/FroxelFog.fx"

float2 Resolution = { 1280.0, 720.0 };

float4x4  World;
float4x4  WorldViewProj;
float3x3  WorldViewIT;

float3 CameraPositionWS;
float3 CameraForwardWS;

// ---- Material ----
float3 DiffuseColor = float3(0.5, 0.5, 0.5);  //sRGB, multiplies the texture
float Roughness = 0.2f;
float Metallic = 0;
float Opacity = 0.5f;
bool HasAlbedo = false;
Texture2D AlbedoMap;
SamplerState AlbedoSampler
{
    Texture = <AlbedoMap>;
    Filter = Anisotropic;
    MaxAnisotropy = 8;
    AddressU = Wrap;
    AddressV = Wrap;
};
float2 TextureTiling = float2(1, 1);

// ---- Point / spot lights (ForwardRenderModule ranks them; spot lights use cos outer -2 for point lights) ----
int LightAmount = 0;
float3 LightPositionWS[MAXLIGHTS];
float LightRadius[MAXLIGHTS];
float LightIntensity[MAXLIGHTS];
float3 LightColor[MAXLIGHTS];
float3 LightSpotDirectionWS[MAXLIGHTS];
float2 LightSpotCos[MAXLIGHTS];
float LightSourceRadius[MAXLIGHTS];

// The same atlas faces and distance encoding as deferred point/spot shadows.
Texture2D PointShadowAtlas;
float4 LightShadowTiles[MAXLIGHTS * 6];
float4 LightShadowSettings[MAXLIGHTS]; // strength, tile size, PCF radius setting, maximum bias
Texture2D CookieAtlas;
float4 LightCookieRect[MAXLIGHTS]; // xy = first texel centre, zw = span to last texel centre; x = 0 means none
float4 LightCookieRight[MAXLIGHTS]; // world right, tan(outer half angle)
float3 LightCookieUp[MAXLIGHTS];
SamplerState CookieSampler
{
    AddressU = Clamp;
    AddressV = Clamp;
    Filter = Linear;
};

float3 LocalCookie(int i, float3 lightToSurface)
{
    float4 rect = LightCookieRect[i];
    if (rect.x <= 0) return 1;
    float forward = dot(lightToSurface, LightSpotDirectionWS[i]);
    if (forward <= 0.001f) return 0;
    float4 right = LightCookieRight[i];
    float2 uv = float2(dot(lightToSurface, right.xyz), dot(lightToSurface, LightCookieUp[i])) / (forward * right.w);
    uv = uv * 0.5f + 0.5f;
    return CookieAtlas.SampleLevel(CookieSampler, rect.xy + saturate(float2(uv.x, 1 - uv.y)) * rect.zw, 0).rgb;
}

float LocalShadowDepth(int i, float2 coord)
{
    int face = clamp((int)floor(coord.y * 6), 0, 5);
    float4 tile = LightShadowTiles[i * 6 + face];
    if (tile.w <= 0) return 1; // Missing tiles and spot faces outside the cone are lit.
    float2 local = saturate(float2(coord.x, coord.y * 6 - face));
    int2 texel = int2(tile.xy) + min(int2(local * tile.z), (int)tile.z - 1);
    return 1 - PointShadowAtlas.Load(int3(texel, 0)).r;
}

float LocalShadow(int i, float3 lightToSurface, float relativeDistance, float ndotl)
{
    float4 settings = LightShadowSettings[i];
    if (settings.x <= 0) return 1;
    // Same slope bias, kernel and face-edge traversal as DeferredPointLight.CalcShadowTermPCF.
    float bias = clamp(0.005f * sqrt(saturate(1 - ndotl * ndotl)) / max(ndotl, 1e-5f), 0, settings.w);
    float testDepth = relativeDistance - bias;
    float2 coord = GetSampleCoordinate(lightToSurface);
    float radius = settings.z - 1;
    if (radius < 1) return lerp(1, (float)(testDepth < LocalShadowDepth(i, coord)), settings.x);
    float2 fractions = frac(float2(settings.y * coord.x, settings.y * coord.y * 6));
    float sum = 0;
    [loop]
    for (float y = -radius; y <= radius; y++)
    {
        [loop]
        for (float x = -radius; x <= radius; x++)
        {
            float2 offsetCoord = GetSampleOffset(coord, float2(x, y) / settings.y);
            float weightX = x == -radius ? 1 - fractions.x : x == radius ? fractions.x : 1;
            float weightY = y == -radius ? 1 - fractions.y : y == radius ? fractions.y : 1;
            sum += (testDepth < LocalShadowDepth(i, offsetCoord)) * weightX * weightY;
        }
    }
    return lerp(1, sum / (radius * radius * 4), settings.x);
}

// ---- Sun with cascaded shadows (world space; DeferredDirectionalLight.fx works in view space) ----
bool UseSun = false;
float3 SunDirection;   //direction the light travels
float3 SunColor;       //colour * intensity
#define MAX_CASCADES 4
float SunCascadeCount = 0;
float4x4 SunCascadeViewProjection[MAX_CASCADES];
float4x4 SunCascadeView[MAX_CASCADES];
float4 SunCascadeEnds;
float4 SunCascadeFarClip;
float4 SunCascadeTexelSize;
float SunShadowMapSize = 2048;
Texture2D SunShadowMap;

// ---- Ambient / fog ----
float AmbientStrength = 1;   //EnvironmentSample Diffuse Strength * 5 (DeferredEnvironmentMap: 0.2 = full irradiance)
bool UseFog = false;

static const float OUTPUTCONST = 0.1f;

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float3 PositionWS : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
};

VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output;
    output.Position = mul(input.Position, WorldViewProj);
    output.PositionWS = mul(input.Position, World).xyz;
    output.Normal = mul(input.Normal, WorldViewIT);
    output.TexCoord = input.TexCoord * TextureTiling;
    return output;
}

//1 = lit. The first cascade whose box holds the point (they are nested around the camera); beyond them all, lit.
float SunShadow(float3 positionWS, float3 normalWS, float ndotl)
{
    int count = (int)SunCascadeCount;
    float viewZ = dot(positionWS - CameraPositionWS, CameraForwardWS);
    [loop]
    for (int i = 0; i < count; i++)
    {
        if (viewZ > SunCascadeEnds[i]) continue;
        float texel = SunCascadeTexelSize[i];
        float3 p = positionWS + normalWS * texel * 1.25f;
        float4 lvp = mul(float4(p, 1), SunCascadeViewProjection[i]);
        float2 uv = lvp.xy / lvp.w * 0.5f + 0.5f;
        uv.y = 1 - uv.y;
        if (any(uv < 0) || any(uv > 1)) continue;
        float depth = mul(float4(p, 1), SunCascadeView[i]).z / -SunCascadeFarClip[i];
        float tanAngle = min(sqrt(saturate(1 - ndotl * ndotl)) / max(ndotl, 1e-3), 4);
        depth -= texel / SunCascadeFarClip[i] * (0.5f + tanAngle);

        //2x2 bilinear PCF inside the cascade's tile
        int2 tileMin = int2(i % 2, i / 2) * (int)SunShadowMapSize;
        float2 texelPos = tileMin + uv * SunShadowMapSize - 0.5f;
        int2 base = int2(floor(texelPos));
        float2 f = texelPos - base;
        int2 tileMax = tileMin + (int)SunShadowMapSize - 1;
        float s00 = depth <= SunShadowMap.Load(int3(clamp(base, tileMin, tileMax), 0)).r;
        float s10 = depth <= SunShadowMap.Load(int3(clamp(base + int2(1, 0), tileMin, tileMax), 0)).r;
        float s01 = depth <= SunShadowMap.Load(int3(clamp(base + int2(0, 1), tileMin, tileMax), 0)).r;
        float s11 = depth <= SunShadowMap.Load(int3(clamp(base + int2(1, 1), tileMin, tileMax), 0)).r;
        return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
    }
    return 1;
}

float4 PixelShaderFunction(VertexShaderOutput input) : COLOR
{
    float3 normal = normalize(input.Normal);
    float3 cameraDirection = normalize(CameraPositionWS - input.PositionWS);
    //Thin glass is lit from the side the camera sees
    if (dot(normal, cameraDirection) < 0) normal = -normal;

    float3 albedoGamma = DiffuseColor;
    float opacity = Opacity;
    [branch]
    if (HasAlbedo)
    {
        float4 texel = AlbedoMap.Sample(AlbedoSampler, input.TexCoord);
        albedoGamma *= texel.rgb;
        opacity *= texel.a;
    }
    float3 albedo = AlbedoToLinear(albedoGamma);
    float roughness = max(Roughness, PBR_MIN_ROUGHNESS);
    float3 f0 = GetF0(albedo, Metallic);
    float NdotV = saturate(dot(normal, cameraDirection));

    float3 diffuseLight = 0;
    float3 specularLight = 0;

    // Sun
    [branch]
    if (UseSun)
    {
        float3 l = -SunDirection;
        float ndotl = saturate(dot(normal, l));
        float shadow = ndotl > 0 ? SunShadow(input.PositionWS, normal, ndotl) : 0;
        float3 d, s;
        EvaluateLight(normal, l, cameraDirection, SunColor, f0, roughness, 1, d, s);
        diffuseLight += d * shadow * OUTPUTCONST;
        specularLight += s * shadow * OUTPUTCONST;
    }

    // Point and spot lights
    [loop]
    for (int i = 0; i < LightAmount; i++)
    {
        float3 lightVector = LightPositionWS[i] - input.PositionWS;
        float lightDistance = length(lightVector);
        float lightRadius = LightRadius[i];
        if (lightDistance >= lightRadius) continue;

        float attenuation = LightAttenuation(lightDistance, lightRadius, LightSourceRadius[i]);
        float3 l = lightVector / max(lightDistance, 1e-5f);
        float2 spotCos = LightSpotCos[i];
        float spotT = saturate((dot(-l, LightSpotDirectionWS[i]) - spotCos.x) / max(spotCos.y - spotCos.x, 0.0001f));
        attenuation *= spotT * spotT;
        if (attenuation <= 0) continue;

        attenuation *= LocalShadow(i, -l, lightDistance / lightRadius, saturate(dot(normal, l)));
        float3 radiance = LightColor[i] * LocalCookie(i, -l) * LightIntensity[i] * attenuation;

        float3 d, s;
        EvaluateLight(normal, l, cameraDirection, radiance, f0, roughness, 1, d, s);
        diffuseLight += d * OUTPUTCONST;
        specularLight += s * OUTPUTCONST;
    }

    // Image based light: the probe volume (or the global probe) for diffuse, the reflection probes for specular
    float3 diffuseWeight = 1 - FresnelSchlickRoughness(f0, NdotV, roughness);
    float3 ambient = ReflectionCubeMap.SampleLevel(ReflectionCubeMapSampler, float3(normal.xy, -normal.z), GlobalMaxMip).rgb * AmbientStrength;
    float3 pixelIrradiance = 0;
    float irradianceWeight = 0;
    [branch]
    if (UseProbeVolume)
    {
        float3 probeCell = 1.0f / (ProbeVolumeExtentRcp * max(ProbeVolumeCells, 1));
        float4 probe = SampleProbeVolume(input.PositionWS + normal * probeCell * 0.5f, normal);
        ambient = lerp(ambient, probe.rgb * ProbeVolumeIntensity, probe.a);
        pixelIrradiance = probe.rgb;
        irradianceWeight = probe.a;
    }
    diffuseLight += ambient * diffuseWeight;

    float3 r = reflect(-cameraDirection, normal);
    float2 envBRDF = UseBrdfLut ? BrdfLut.SampleLevel(BrdfSampler, float2(NdotV, roughness), 0).rg : EnvBRDFApprox(NdotV, roughness);
    specularLight += SampleReflections(input.PositionWS, r, normal, roughness, UseProbeVolume, pixelIrradiance, irradianceWeight)
        * (f0 * envBRDF.x + envBRDF.y);

    // Premultiplied: light passing through is scaled by the opacity, reflections are not
    float3 color = albedo * (1 - Metallic) * diffuseLight * opacity + specularLight;
    float alpha = saturate(opacity);

    // Fog between the camera and the surface
    [branch]
    if (UseFog)
    {
        float2 screenUV = input.Position.xy / Resolution;
        float viewDepth = dot(input.PositionWS - CameraPositionWS, CameraForwardWS);
        float4 fog = SampleFroxelFog(screenUV, viewDepth);
        color = color * fog.a + fog.rgb * alpha;
    }

    return float4(color, alpha);
}

technique Default
{
    pass Pass1
    {
        VertexShader = compile vs_5_0 VertexShaderFunction();
        PixelShader = compile ps_5_0 PixelShaderFunction();
    }
}
