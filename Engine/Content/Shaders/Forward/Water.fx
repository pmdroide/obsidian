// Procedural water: Gerstner swell that moves the (subdivided) mesh, finer ripples in the normal,
// depth-based absorption and shore/crest foam, environment reflections and a sun/moon highlight.
// All lighting comes from the scene's light and the captured environment cubemap (no constant ambient),
// so the surface darkens with the day/night cycle.
float4x4 World;
float4x4 ViewProj;
float4x4 WorldInverseTranspose;
float4x4 View;
float3 CameraPositionWS;
float Time;
float3 SurfaceColor;  //colour of deep water (sRGB)
float Roughness;
float Opacity;        //master blend; 0 hides the water
float WaveScale;
float WaveSpeed;
float WaveStrength;   //strength of the fine ripples in the normal
float WaveHeight = 0.5f; //crest-to-trough height of the swell that moves the mesh, metres
float GridSpacing = 1;   //world distance between the mesh's vertices; shorter swell waves are faded out
float Clarity = 4;    //metres of water you can see through
float Foam = 0.5f;    //shore and crest foam amount
float3 LightDirection; //towards the light
float3 LightColor;     //colour * intensity * 0.1, like the deferred lights
bool HasEnvironment;
TextureCube EnvironmentMap;
SamplerState EnvironmentSampler
{
    Texture = <EnvironmentMap>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    MipFilter = LINEAR;
};
bool HasDepth;
Texture2D DepthMap; //linear G-buffer depth, view Z / -FarClip
float FarClip;

#define SWELL_COUNT 4
#define RIPPLE_COUNT 8
#define CHOPPINESS 0.8f
#define PI 3.14159265f

struct VSInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
};
struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 PositionWS : TEXCOORD0;
    float3 NormalWS : TEXCOORD1;
    float2 RestXY : TEXCOORD2; //world XY before the swell moved it; the waves are a function of this
};

//Longest wavelength in metres: 20 m at the default scale
float BaseWavelength()
{
    return 6.0f / max(WaveScale, 0.001f);
}

float WaveAngle(int i)
{
    //Spread directions around the wind so the pattern never lines up
    return 0.6f + i * 0.839986f + (i % 2) * 0.9f;
}

//Gerstner swell on the world XY plane (Z is up). Returns how far the surface point resting at restXY
//has moved; slope gets the surface gradient and pinch how much the surface is squeezed into a crest
//(1 = flat, towards 0 = sharp crest). Engine/Physics/WaterWaves.cs mirrors this for buoyancy, so keep
//the two in step.
float3 Swell(float2 restXY, float spacing, out float2 slope, out float pinch)
{
    float3 offset = 0;
    slope = 0;
    pinch = 1;
    float totalWeight = 0;
    float weight = 1;
    [unroll]
    for (int j = 0; j < SWELL_COUNT; j++)
    {
        totalWeight += weight;
        weight *= 0.78f;
    }

    float wavelength = BaseWavelength();
    weight = 1;
    [unroll]
    for (int i = 0; i < SWELL_COUNT; i++)
    {
        float angle = WaveAngle(i);
        float2 direction = float2(cos(angle), sin(angle));
        float k = 2 * PI / wavelength;
        float omega = sqrt(9.81f * k) * WaveSpeed;
        float phase = dot(direction, restXY) * k - omega * Time + i * 1.7f;
        float amplitude = WaveHeight * 0.5f * weight / totalWeight;
        //A wave shorter than a few grid cells can't be drawn by the mesh; fade it instead of aliasing
        amplitude *= saturate(wavelength / max(spacing, 0.0001f) / 3 - 1);
        float sideways = min(amplitude, CHOPPINESS / (k * SWELL_COUNT));
        float s, c;
        sincos(phase, s, c);
        offset += float3(direction * sideways * c, amplitude * s);
        slope += direction * k * amplitude * c;
        pinch -= k * sideways * s;
        wavelength *= 0.68f;
        weight *= 0.78f;
    }
    return offset;
}

float WaterHash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031f);
    p3 += dot(p3, p3.yzx + 33.33f);
    return frac((p3.x + p3.y) * p3.z);
}

float WaterNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(WaterHash(i), WaterHash(i + float2(1, 0)), u.x),
                lerp(WaterHash(i + float2(0, 1)), WaterHash(i + float2(1, 1)), u.x), u.y);
}

//Fine ripples that only bend the normal, continuing below the swell's shortest wave. Returns the surface
//slope in xy and a normalised height in z. Each wave moves at deep-water speed for its length, and waves
//shorter than a couple of pixels fade out so distant water does not shimmer.
float3 Ripples(float2 positionWS, float pixelFootprint)
{
    float2 slope = 0;
    float height = 0;
    float total = 0;
    float wavelength = BaseWavelength() * pow(0.68f, SWELL_COUNT);
    float amplitude = 1;
    [unroll]
    for (int i = 0; i < RIPPLE_COUNT; i++)
    {
        float angle = WaveAngle(i + SWELL_COUNT);
        float2 direction = float2(cos(angle), sin(angle));
        float k = 2 * PI / wavelength;
        float omega = sqrt(9.81f * k) * WaveSpeed;
        float phase = dot(direction, positionWS) * k - omega * Time + i * 1.7f;
        float fade = saturate(1.5f - pixelFootprint * 2 / wavelength);
        float a = amplitude * fade;
        height += a * sin(phase);
        slope += a * cos(phase) * direction;
        total += amplitude;
        wavelength *= 0.68f;
        amplitude *= 0.78f;
    }
    return float3(slope / total, height / total);
}

VSOutput WaterVS(VSInput input)
{
    VSOutput output;
    float3 rest = mul(input.Position, World).xyz;
    float2 slope;
    float pinch;
    float3 positionWS = rest + Swell(rest.xy, GridSpacing, slope, pinch);
    output.Position = mul(float4(positionWS, 1), ViewProj);
    output.PositionWS = positionWS;
    output.NormalWS = mul(float4(input.Normal, 0), WorldInverseTranspose).xyz;
    output.RestXY = rest.xy;
    return output;
}

//The captured cubemap is stored with Z flipped (see DeferredEnvironmentMap.fx), so flip lookups to match.
float3 SampleEnvironment(float3 direction, float mip)
{
    return EnvironmentMap.SampleLevel(EnvironmentSampler, float3(direction.xy, -direction.z), mip).rgb;
}

float4 WaterPS(VSOutput input) : SV_TARGET
{
    float3 baseNormal = normalize(input.NormalWS);
    float3 toCamera = CameraPositionWS - input.PositionWS;
    float cameraDistance = length(toCamera);
    float3 view = toCamera / cameraDistance;
    // Two-sided shading is useful for a thin water surface viewed from below.
    if (dot(baseNormal, view) < 0) baseNormal = -baseNormal;

    // ---- Surface shape: the swell (per pixel, unfaded, so lighting keeps its detail) plus ripples ----
    float2 swellSlope;
    float pinch;
    float3 swell = Swell(input.RestXY, 0, swellSlope, pinch);
    float footprint = length(fwidth(input.RestXY));
    float3 ripple = Ripples(input.RestXY, footprint);
    float3 tilt = float3(-swellSlope - ripple.xy * WaveStrength * 2, 0);
    // Project the tilt onto the mesh's tangent plane; on an upward surface the pinch sharpens crests.
    float3 normal = normalize(baseNormal * lerp(1, max(pinch, 0.2f), saturate(baseNormal.z)) +
                              tilt - baseNormal * dot(tilt, baseNormal));
    float NdotV = saturate(dot(normal, view));
    // 0 in the troughs, 1 on the crests
    float swellCrest = WaveHeight > 0 ? swell.z / (WaveHeight * 0.5f) : 0;
    float crest = saturate(0.5f + 0.5f * lerp(ripple.z, swellCrest, WaveHeight > 0 ? 0.75f : 0));

    // ---- Lighting inputs: everything scales with the cycle's light and the captured sky ----
    float3 L = normalize(LightDirection);
    float NdotL = saturate(dot(normal, L));
    float3 ambient = 0;
    float3 reflection = 0;
    float3 reflectedDirection = reflect(-view, normal);
    // Keep reflections above the horizon; waves can tilt the vector into the ground.
    reflectedDirection.z = max(reflectedDirection.z, 0.02f);
    reflectedDirection = normalize(reflectedDirection);
    if (HasEnvironment)
    {
        ambient = SampleEnvironment(float3(0, 0, 1), 9) * 0.6f + SampleEnvironment(normal, 6) * 0.4f;
        reflection = SampleEnvironment(reflectedDirection, Roughness * 6);
    }
    else
    {
        ambient = LightColor * 0.05f;
        reflection = LightColor * 0.05f * float3(0.6f, 0.75f, 1.0f);
    }

    // ---- Depth: how much water lies between the surface and the scene behind it ----
    float thickness = 1000;
    if (HasDepth)
    {
        float sceneDepth = DepthMap.Load(int3(input.Position.xy, 0)).r * FarClip;
        float waterDepth = -mul(float4(input.PositionWS, 1), View).z;
        thickness = max(sceneDepth - waterDepth, 0);
    }
    // Absorption: red fades first, so shallow water reads turquoise and deep water takes the surface colour.
    float3 deepColor = pow(abs(SurfaceColor), 2.2f);
    float3 extinction = (1.15f - deepColor / max(max(deepColor.r, deepColor.g), max(deepColor.b, 0.001f)) * 0.85f)
                        / max(Clarity, 0.05f);
    float3 transmittance = exp(-thickness * extinction * 1.5f);
    float clear = dot(transmittance, float3(0.3f, 0.4f, 0.3f));

    // Light scattered back up from inside the water, plus a glow through crests that face away from the
    // light: thin wave tops let sunlight through and turn bright turquoise.
    float3 scatter = deepColor * (ambient + LightColor * saturate(L.z) * 0.6f) * 0.35f;
    float backlit = pow(saturate(dot(view, -L) * 0.5f + 0.5f), 4) * crest;
    float3 crestTint = deepColor * float3(0.6f, 1.4f, 1.2f);
    scatter += crestTint * LightColor * backlit * (0.15f + 0.35f * saturate(swellCrest));

    // ---- Surface reflection ----
    float fresnel = 0.02f + 0.98f * pow(1 - NdotV, 5);
    fresnel *= lerp(1, 0.7f, Roughness);
    float3 halfVector = normalize(view + L);
    float NdotH = saturate(dot(normal, halfVector));
    float alpha = max(Roughness * Roughness, 0.004f);
    float alpha2 = alpha * alpha;
    float denom = NdotH * NdotH * (alpha2 - 1) + 1;
    float distribution = alpha2 / (PI * denom * denom);
    float3 specular = LightColor * min(distribution * 0.02f * NdotL, 40) * 0.25f;

    // ---- Foam on the shore, on high crests and where the swell squeezes into a sharp peak ----
    float foam = 0;
    if (Foam > 0)
    {
        float2 foamUV = input.RestXY * 1.5f;
        float pattern = WaterNoise(foamUV + Time * 0.15f) * 0.6f + WaterNoise(foamUV * 2.3f - Time * 0.1f) * 0.4f;
        float verticalDepth = thickness * max(NdotV, 0.2f);
        float shore = HasDepth ? saturate(1 - verticalDepth / (0.15f + Foam * 0.9f)) : 0;
        float sharp = saturate((0.6f - pinch) * 2.5f);
        float crestFoam = max(saturate((crest - 0.78f) * 4), sharp) * Foam * 0.6f;
        foam = saturate(smoothstep(pattern * 0.9f, pattern * 0.9f + 0.25f, shore) + crestFoam * pattern) * Foam;
    }
    float3 foamColor = 0.6f * (LightColor * saturate(dot(baseNormal, L)) + ambient * 0.5f);

    // ---- Compose for NonPremultiplied blending: background shows through by transmittance ----
    float3 water = scatter * (1 - clear) * (1 - fresnel) + reflection * fresnel + specular;
    float coverage = 1 - clear * (1 - fresnel);
    float3 color = water / max(coverage, 0.001f);
    color = lerp(color, foamColor, foam);
    coverage = lerp(coverage, 1, foam);
    // Fade the edge where the surface meets geometry so the intersection stays soft
    float edge = HasDepth ? saturate(thickness * 4) : 1;
    return float4(color, saturate(coverage * Opacity * edge));
}

technique Water
{
    pass Pass0
    {
        VertexShader = compile vs_5_0 WaterVS();
        PixelShader = compile ps_5_0 WaterPS();
    }
}
