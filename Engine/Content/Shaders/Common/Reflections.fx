// Image based reflections (Source 2 look, Docs/Research/Source2Look phase 3): the GGX-prefiltered global probe, local
// Reflection Probe components (box projected, priority blended, normalised), the split-sum BRDF lookup.
// Shared by the environment pass and the forward pass. Include ProbeVolume.fx first (CaptureSH*).

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

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  REFLECTIONS (Source 2 look, Docs/Research/Source2Look phase 3)
//  ReflectionCubeMap is the scene's GGX-prefiltered global probe (mip m = roughness m / GlobalMaxMip).
//  Up to MAX_PROBES local Reflection Probe components blend over it, highest priority first, each box projected
//  and normalised by the baked irradiance here vs. at its capture point.

float GlobalMaxMip = 6;

//Split-sum BRDF: x = NdotV, y = roughness -> F0 scale, bias (EnvironmentPrefilter.IntegrateBrdf)
Texture2D BrdfLut;
bool UseBrdfLut = false;
SamplerState BrdfSampler
{
    Texture = <BrdfLut>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = POINT;
};

//Cubemap normalisation (vr_cubemap_normalization_max / _roughness_begin): reflections are scaled by the baked
//irradiance at the pixel over that at the capture point, at most NormalizationMax, fading in up to RoughnessBegin
float NormalizationMax = 1;
float NormalizationRoughnessBegin = 0;

//Capture pass of a cubemap (probe or global): only the baked diffuse light, no reflections, SSR, AO or sky
bool CaptureMode = false;

#define MAX_PROBES 4
int ProbeCount = 0;
float4x4 ProbeWorldToBox[MAX_PROBES];
float4x4 ProbeBoxToWorld[MAX_PROBES];
//x = blend distance (m), y = box projection (1/0), z = mips - 1, w = has capture irradiance (1/0)
float4 ProbeSettings[MAX_PROBES];
float4 ProbeExtent[MAX_PROBES];      //half size of the box in metres
float4 ProbeCaptureBox[MAX_PROBES];  //capture point in box space
float4 ProbeCaptureSHR[MAX_PROBES];
float4 ProbeCaptureSHG[MAX_PROBES];
float4 ProbeCaptureSHB[MAX_PROBES];

TextureCube ProbeCube0;
TextureCube ProbeCube1;
TextureCube ProbeCube2;
TextureCube ProbeCube3;
SamplerState ProbeSampler
{
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};

float3 SampleProbeCube(int i, float3 dir, float mip)
{
    //Cubemaps are sampled with Z flipped (captured with XNA's Y-up faces)
    dir.z = -dir.z;
    if (i == 0) return ProbeCube0.SampleLevel(ProbeSampler, dir, mip).rgb;
    if (i == 1) return ProbeCube1.SampleLevel(ProbeSampler, dir, mip).rgb;
    if (i == 2) return ProbeCube2.SampleLevel(ProbeSampler, dir, mip).rgb;
    return ProbeCube3.SampleLevel(ProbeSampler, dir, mip).rgb;
}

static const float3 LumaWeights = float3(0.2126f, 0.7152f, 0.0722f);

//How much of a reflection captured where the baked irradiance was captureSH survives here (pixelIrradiance)
float Normalization(float4 shR, float4 shG, float4 shB, float3 normal, float3 pixelIrradiance, float roughness)
{
    float4 n = float4(1, normal);
    float3 captureIrradiance = max(float3(dot(shR, n), dot(shG, n), dot(shB, n)), 0);
    float ratio = min(dot(pixelIrradiance, LumaWeights) / max(dot(captureIrradiance, LumaWeights), 0.0001f), NormalizationMax);
    float fade = NormalizationRoughnessBegin > 0 ? saturate(roughness / NormalizationRoughnessBegin) : 1;
    return lerp(1, ratio, fade);
}

//Reflection of the environment in direction r (world space) at a surface point: local probes over the global one.
//hasIrradiance: pixelIrradiance is baked (probe volume) and the reflections can be normalised.
float3 SampleReflections(float3 positionWS, float3 r, float3 normal, float roughness, bool hasIrradiance,
    float3 pixelIrradiance, float irradianceWeight)
{
    float3 result = 0;
    float remaining = 1;

    [loop]
    for (int i = 0; i < ProbeCount && remaining > 0.001f; i++)
    {
        float3 p = mul(float4(positionWS, 1), ProbeWorldToBox[i]).xyz;
        float3 extent = ProbeExtent[i].xyz;
        //Metres from the nearest box face, inside
        float3 inside = (1 - abs(p)) * extent;
        float distance = min(inside.x, min(inside.y, inside.z));
        float blend = ProbeSettings[i].x;
        float weight = distance <= 0 ? 0 : blend > 0 ? saturate(distance / blend) : 1;
        if (weight <= 0) continue;

        float3 dir = r;
        [branch]
        if (ProbeSettings[i].y > 0.5f)
        {
            //Box projection: where the reflection ray leaves the box, seen from the capture point
            float3 d = mul(float4(r, 0), ProbeWorldToBox[i]).xyz;
            bool3 moving = abs(d) > 1e-5f;
            float3 planes = moving ? (sign(d) - p) / (moving ? d : 1) : 1e10;
            float t = min(planes.x, min(planes.y, planes.z));
            float3 hit = p + d * max(t, 0);
            float3 local = hit - ProbeCaptureBox[i].xyz;
            dir = normalize(mul(float4(local, 0), ProbeBoxToWorld[i]).xyz);
            //Rough reflections don't come from a point: fade back to the plain direction
            dir = normalize(lerp(dir, r, roughness * roughness));
        }

        float3 color = SampleProbeCube(i, dir, roughness * ProbeSettings[i].z);
        if (hasIrradiance && ProbeSettings[i].w > 0.5f)
            color *= lerp(1, Normalization(ProbeCaptureSHR[i], ProbeCaptureSHG[i], ProbeCaptureSHB[i], normal, pixelIrradiance, roughness), irradianceWeight);

        result += color * weight * remaining;
        remaining *= 1 - weight;
    }

    [branch]
    if (remaining > 0.001f)
    {
        float3 global = ReflectionCubeMap.SampleLevel(ReflectionCubeMapSampler, float3(r.xy, -r.z), roughness * GlobalMaxMip).rgb;
        if (hasIrradiance)
            global *= lerp(1, Normalization(CaptureSHR, CaptureSHG, CaptureSHB, normal, pixelIrradiance, roughness), irradianceWeight);
        result += global * remaining;
    }
    return result;
}
