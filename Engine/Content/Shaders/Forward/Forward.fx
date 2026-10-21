// Forward shaded materials (MaterialEffect.MaterialTypes.ForwardShaded: glass, transparent plastics), lit like the
// deferred path (Source 2 look, Docs/Research/Source2Look phase 3.4): the material's colour/texture, roughness and
// metalness; the sun with its cascaded shadows; point and spot lights with atlas shadows and cookies; image based light from the
// probe volume and the reflection probes; the froxel fog in front of it. Output is premultiplied: reflections stay
// visible on clear glass. Light units match DeferredCompose's output (the light buffers' 1/10 scale).
// The same shader draws the opaque scene into a Planar Reflection pane's mirror image (ReflectionPass), and panes
// with one show that image instead of the reflection probes (PlanarReflectionRenderModule).

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
bool HasMask = false;
Texture2D MaskMap;
SamplerState MaskSampler
{
    Texture = <MaskMap>;
    AddressU = Wrap;
    AddressV = Wrap;
};

// ---- Liquid component (Physics/LiquidSlosh.cs): Half-Life: Alyx's vr_simple_liquid (Docs/Research/BottleLiquid) ----
// The container's own shell shows liquid below a plane. LiquidSlosh computes the wobbling plane per instance; here are
// the per-pixel parts: ripples, the meniscus at grazing angles, the water line, foam and bubbles. Back faces under the
// surface draw its top (LiquidBackFace pass, drawn before the outside). Wave constants are Alyx's, in inches.
bool UseLiquid = false;
bool LiquidBackFace = false;
float4 LiquidSurface;   // xyz = world up of the surface, w = height: liquid where dot(p, up) <= w
float4 LiquidCentre;    // xyz = container centre (ripple origin), w = dot(container axis, up)
float4 LiquidAxis;      // xyz = container +Z, w = container height (m)
float4 LiquidWave;      // wobble amplitude, wobble scale, wavelength, surface tension
float4 LiquidPhaseA;    // ripple phases (rad)
float4 LiquidPhaseB;    // xy = ripple phases, z = clock (s)
float3 LiquidAmounts;   // agitation, foam, bubbles (0..1)
float3 LiquidColor = float3(0.77, 0.47, 0.13);     // sRGB
float3 LiquidFoamColor = float3(0.96, 0.92, 0.82); // sRGB
float4 LiquidLook;      // opacity, roughness, emissive, water line
float4 LiquidLook2;     // foaminess, fizz, container radius (m)

static const float LIQUID_INCH = 0.0254f;

// Ripple height in inches at a world offset o (inches) from the centre. facing = |normal * up| (component-wise, as Alyx).
float LiquidRipple(float2 o, float facing)
{
    float amp = LiquidWave.x;
    float side = abs(LiquidCentre.w) * 0.5f + 0.5f;
    float k = side * LiquidWave.z * (amp * 0.4f + 0.2f);
    float w = sin(dot(k * float2(-1.242110f, 1.142074f), o) + LiquidPhaseA.x) * amp;
    w = w * 0.125f + sin(dot(k * float2(2.821100f, 2.920740f), o) + LiquidPhaseA.y) * 0.333333f;
    w += amp * sin(dot(k * float2(-3.242110f, -3.642074f), o) + LiquidPhaseA.z) * 0.111111f;
    w += amp * sin(dot(k * float2(3.742110f, -3.642074f), o) + LiquidPhaseA.w) * 0.111111f;
    float chop = 1 - facing;
    w *= 1 + chop * 0.14f * sin(dot(k * float2(-8.742110f, 13.642074f), o) + LiquidPhaseB.x);
    w *= 1 + chop * 0.13f * sin(dot(k * float2(13.742110f, 14.642074f), o) + LiquidPhaseB.y);
    w *= 1 + chop * 0.10f * sin(dot(k * float2(-23.742109f, -13.642074f), o) + LiquidPhaseB.x);
    w *= 1 + chop * 0.08f * sin(dot(k * float2(31.742109f, -20.642075f), o) + LiquidPhaseB.y);
    return w * LiquidWave.y * amp * amp * side * 150;
}

// Signed depth (m) of p under the surface; > 0 = liquid. fres = 1 - N.V of the shell.
float LiquidDepth(float3 p, float3 normal, float fres)
{
    float3 up = LiquidSurface.xyz;
    float facing = length(normal * up);
    float meniscus = ((0.75f * facing + 0.25f) * fres + 0.8f * pow(fres, 5)) * LiquidWave.w;
    float ripple = LiquidRipple((p.xy - LiquidCentre.xy) / LIQUID_INCH, facing) * (1.5f - fres);
    return LiquidSurface.w + (meniscus + ripple) * LIQUID_INCH - dot(p, up);
}

float LiquidHash(float2 p)
{
    return frac(sin(dot(p, float2(12.9898f, 78.233f))) * 43758.5453f);
}

// Rising bubbles on the shell (0..1): dots on a grid around the container's axis, columns at their own speed.
float LiquidBubbles(float3 p, float amount)
{
    if (amount <= 0.002f) return 0;
    float3 axis = LiquidAxis.xyz;
    float3 rel = p - LiquidCentre.xyz;
    float h = dot(rel, axis);
    float3 radial = rel - axis * h;
    float3 t = normalize(abs(axis.z) < 0.9f ? cross(axis, float3(0, 0, 1)) : cross(axis, float3(1, 0, 0)));
    float3 b = cross(axis, t);
    float angle = atan2(dot(radial, b), dot(radial, t));
    float2 uv = float2(angle * max(LiquidLook2.z, 0.005f), h) / 0.005f; // 5 mm cells
    float sum = 0;
    [unroll]
    for (int layer = 0; layer < 2; layer++)
    {
        float2 q = uv * (1 + layer * 0.7f) + float2(layer * 17.3f, layer * 5.1f);
        float column = floor(q.x);
        q.y -= LiquidPhaseB.z * (3 + LiquidHash(float2(column, layer)) * 4);
        float2 cell = floor(q);
        float rnd = LiquidHash(cell + layer * 31.0f);
        float2 offset = (float2(frac(rnd * 7.31f), frac(rnd * 13.7f)) - 0.5f) * 0.45f;
        float radius = 0.1f + 0.14f * frac(rnd * 3.17f);
        float dist = length(frac(q) - 0.5f - offset);
        sum += smoothstep(radius, radius * 0.55f, dist) * step(rnd, amount);
    }
    return saturate(sum);
}

// ---- Planar reflections (PlanarReflectionRenderModule) ----
// ReflectionPass: drawing an opaque gameobject into a mirror image, seen from the mirrored camera; pixels behind the
// pane (dot(ClipPlane.xyz, p) + ClipPlane.w < 0) are clipped.
bool ReflectionPass = false;
float4 ClipPlane;
// The pane being drawn has a mirror image: rgb = light, a = coverage (0 where nothing was drawn: keep the probes).
bool UsePlanarReflection = false;
float3 PlanarReflectionSize = float3(1280, 720, 1); // width, height, mip levels
Texture2D PlanarReflectionMap;
SamplerState PlanarReflectionSampler
{
    Texture = <PlanarReflectionMap>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

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
    [branch]
    if (ReflectionPass) clip(dot(ClipPlane.xyz, input.PositionWS) + ClipPlane.w);

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
    //Mirror images hold opaque surfaces (cut-outs keep their mask)
    [branch]
    if (ReflectionPass)
    {
        if (HasMask) clip(MaskMap.Sample(MaskSampler, input.TexCoord).r - 0.5f);
        opacity = 1;
    }

    // Liquid component: the shell below the surface shows the liquid; from the inside (back faces) its top
    float3 positionWS = input.PositionWS;
    float metallic = Metallic;
    float roughnessIn = Roughness;
    float liquidLine = 0;
    float3 liquidEmission = 0;
    [branch]
    if (UseLiquid && !ReflectionPass)
    {
        float foamThickness = LiquidAmounts.y * LiquidLook2.x * 0.02f;
        [branch]
        if (LiquidBackFace)
        {
            // Where the view ray crosses the surface, if it does inside the container
            float3 up = LiquidSurface.xyz;
            float cameraHeight = dot(CameraPositionWS, up) - LiquidSurface.w;
            float pixelHeight = dot(input.PositionWS, up) - LiquidSurface.w;
            clip(cameraHeight);
            clip(-pixelHeight);
            float3 hit = lerp(CameraPositionWS, input.PositionWS, cameraHeight / max(cameraHeight - pixelHeight, 1e-6f));
            float3 rel = hit - LiquidCentre.xyz;
            float along = dot(rel, LiquidAxis.xyz);
            clip(LiquidLook2.z * 1.02f - length(rel - LiquidAxis.xyz * along));
            positionWS = hit;
            // The ripples tilt the top
            float2 o = (hit.xy - LiquidCentre.xy) / LIQUID_INCH;
            float r0 = LiquidRipple(o, 1);
            float3 slope = float3(LiquidRipple(o + float2(0.1f, 0), 1) - r0, LiquidRipple(o + float2(0, 0.1f), 1) - r0, 0) / 0.1f;
            normal = normalize(up - (slope - up * dot(slope, up)));
            float foamTop = saturate(LiquidAmounts.y * LiquidLook2.x * 3);
            albedoGamma = lerp(LiquidColor, LiquidFoamColor, foamTop);
            opacity = lerp(max(LiquidLook.x, 0.5f), 1, foamTop);
            roughnessIn = lerp(LiquidLook.y, 0.6f, foamTop);
            metallic = 0;
            liquidEmission = AlbedoToLinear(LiquidColor) * LiquidLook.z * (1 - foamTop);
        }
        else
        {
            float fres = saturate(1 - dot(normal, cameraDirection));
            float depth = LiquidDepth(input.PositionWS, normal, fres);
            float cover = saturate(depth / max(fwidth(depth), 1e-5f) + 0.5f);
            float foam = cover * saturate((foamThickness - depth) / 0.002f) * step(0.0005f, foamThickness);
            float bubbles = cover * LiquidBubbles(input.PositionWS, saturate(LiquidAmounts.z * 0.5f + 0.06f) * LiquidLook2.y * 0.5f);
            // Seen at a grazing angle the light crosses more liquid
            float liquidOpacity = 1 - pow(saturate(1 - LiquidLook.x), 1 / max(1 - fres, 0.2f));
            float3 liquid = lerp(LiquidColor, LiquidFoamColor, saturate(foam + bubbles * 0.8f));
            albedoGamma = lerp(albedoGamma, liquid, cover);
            opacity = lerp(opacity, max(opacity, saturate(liquidOpacity + foam + bubbles * 0.5f)), cover);
            liquidEmission = AlbedoToLinear(LiquidColor) * LiquidLook.z * cover * (1 - foam);
            // Water line: a thin bright band just under the surface (Alyx: 0.05 and 0.09 inch down)
            liquidLine = cover * (1 - fres) * (1 - fres) * saturate(1 - abs(depth / LIQUID_INCH - 0.07f) * 10);
        }
    }

    float3 albedo = AlbedoToLinear(albedoGamma);
    float roughness = max(roughnessIn, PBR_MIN_ROUGHNESS);
    float3 f0 = GetF0(albedo, metallic);
    float NdotV = saturate(dot(normal, cameraDirection));

    float3 diffuseLight = 0;
    float3 specularLight = 0;

    // Sun
    [branch]
    if (UseSun)
    {
        float3 l = -SunDirection;
        float ndotl = saturate(dot(normal, l));
        float shadow = ndotl > 0 ? SunShadow(positionWS, normal, ndotl) : 0;
        float3 d, s;
        EvaluateLight(normal, l, cameraDirection, SunColor, f0, roughness, 1, d, s);
        diffuseLight += d * shadow * OUTPUTCONST;
        specularLight += s * shadow * OUTPUTCONST;
    }

    // Point and spot lights
    [loop]
    for (int i = 0; i < LightAmount; i++)
    {
        float3 lightVector = LightPositionWS[i] - positionWS;
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
        float4 probe = SampleProbeVolume(positionWS + normal * probeCell * 0.5f, normal);
        ambient = lerp(ambient, probe.rgb * ProbeVolumeIntensity, probe.a);
        pixelIrradiance = probe.rgb;
        irradianceWeight = probe.a;
    }
    diffuseLight += ambient * diffuseWeight;

    float3 r = reflect(-cameraDirection, normal);
    float2 envBRDF = UseBrdfLut ? BrdfLut.SampleLevel(BrdfSampler, float2(NdotV, roughness), 0).rg : EnvBRDFApprox(NdotV, roughness);
    float3 reflection = SampleReflections(positionWS, r, normal, roughness, UseProbeVolume, pixelIrradiance, irradianceWeight);
    // A Planar Reflection pane: the mirrored scene at this pixel (premultiplied by its coverage), blurred with roughness
    [branch]
    if (UsePlanarReflection && !ReflectionPass)
    {
        float2 screenUV = input.Position.xy / Resolution;
        float lobe = roughness * roughness * PlanarReflectionSize.x * 0.5f;
        float mip = min(log2(1 + lobe), PlanarReflectionSize.z - 1);
        float4 planar = PlanarReflectionMap.SampleLevel(PlanarReflectionSampler, screenUV, mip);
        reflection = reflection * (1 - saturate(planar.a)) + planar.rgb;
    }
    specularLight += reflection * (f0 * envBRDF.x + envBRDF.y);

    // Premultiplied: light passing through is scaled by the opacity, reflections are not
    float3 color = albedo * (1 - metallic) * diffuseLight * opacity + specularLight;
    // Liquid: its own glow, and the water line catching the light around it
    color += liquidEmission * OUTPUTCONST * opacity;
    color += liquidLine * LiquidLook.w * 0.5f * (ambient + reflection);
    float alpha = saturate(opacity);

    // Fog between the camera and the surface
    [branch]
    if (UseFog)
    {
        float2 screenUV = input.Position.xy / Resolution;
        float viewDepth = dot(positionWS - CameraPositionWS, CameraForwardWS);
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

// ---- Planar reflection sky (PlanarSky): fills a mirror image where nothing was drawn (depth still 1) with the
// background sky along the mirrored rays, at the main view's brightness (DeferredEnvironmentMap: sky * 0.5) ----
float4x4 InverseViewProjection; // of the mirrored view
bool UseSkyMap2D = false;
TextureCube SkyCubeMap;
SamplerState SkyCubeSampler
{
    Texture = <SkyCubeMap>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};
Texture2D SkyMap2D;
SamplerState SkyMap2DSampler
{
    Texture = <SkyMap2D>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Clamp;
};

struct SkyVertexOutput
{
    float4 Position : SV_POSITION;
    float2 Ndc : TEXCOORD0;
};

SkyVertexOutput SkyVertexShader(float2 position : POSITION0)
{
    SkyVertexOutput output;
    output.Position = float4(position, 1, 1); //on the far plane: only where the depth is still clear
    output.Ndc = position;
    return output;
}

float4 SkyPixelShader(SkyVertexOutput input) : COLOR
{
    float4 far = mul(float4(input.Ndc, 1, 1), InverseViewProjection);
    float3 direction = normalize(far.xyz / far.w - CameraPositionWS);
    float3 sky;
    [branch]
    if (UseSkyMap2D)
    {
        //Z up, like SampleSky in DeferredEnvironmentMap.fx
        float u = atan2(direction.y, direction.x) / 6.2831853f + 0.5f;
        float v = acos(clamp(direction.z, -1.0f, 1.0f)) / 3.14159265f;
        sky = SkyMap2D.SampleLevel(SkyMap2DSampler, float2(u, v), 0).rgb;
    }
    else
        sky = SkyCubeMap.SampleLevel(SkyCubeSampler, direction, 0).rgb;
    return float4(sky * 0.5f, 1);
}

technique PlanarSky
{
    pass Pass1
    {
        VertexShader = compile vs_5_0 SkyVertexShader();
        PixelShader = compile ps_5_0 SkyPixelShader();
    }
}
