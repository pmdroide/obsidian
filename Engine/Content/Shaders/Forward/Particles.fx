// Particle systems (Engine/Components/ParticleSystemComponent.cs): one instanced quad per particle, drawn by
// Engine/Renderer/RenderModules/ParticleRenderModule.cs after water, on the HDR image. Particles don't use a
// depth buffer: like Weather.fx they read the linear G-buffer depth, which both hides them behind geometry and
// fades them softly into it. Colours arrive as sRGB 0..1 and are linearised here; Brightness above 1 blooms.
float4x4 ViewProj;
float4x4 View;
float3 CameraPosition;
float3 CameraRight;
float3 CameraUp;
float PixelAngle;        //world size of one pixel at 1 m; thinner particles widen and fade instead of aliasing

float RenderMode;        //0 billboard, 1 stretched, 2 horizontal, 3 vertical
float LengthScale;       //stretched: length as a multiple of the size
float SpeedScale;        //stretched: extra length per m/s
float2 Tiles;            //texture sheet columns, rows (1,1 = whole texture)
float Brightness;
bool Lit;
float SoftParticles;     //metres over which particles fade into geometry
float CameraFade;        //metres in front of the camera over which particles fade in

float3 LightDirection;   //towards the light
float3 LightColor;       //colour * intensity * 0.1, like the deferred lights

Texture2D ParticleTexture;
SamplerState ParticleSampler
{
    Texture = <ParticleTexture>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    MipFilter = LINEAR;
};

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

float3 Ambient(float3 direction)
{
    if (!HasEnvironment) return LightColor * 0.05f + 0.02f;
    float3 d = float3(direction.xy, -direction.z);
    return EnvironmentMap.SampleLevel(EnvironmentSampler, float3(0, 0, -1), 9).rgb * 0.5f +
           EnvironmentMap.SampleLevel(EnvironmentSampler, d, 7).rgb * 0.5f;
}

struct VertexInput
{
    float2 Corner : TEXCOORD0;   //-1..1, per quad vertex
    float3 Center : TEXCOORD1;   //per particle from here on
    float4 Color : TEXCOORD2;    //sRGB rgb, alpha
    float4 Shape : TEXCOORD3;    //width, height, rotation (radians), sheet frame
    float4 Velocity : TEXCOORD4; //xyz m/s
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
    float4 Color : TEXCOORD1;    //linear, lit, alpha
    float ViewDepth : TEXCOORD2;
};

VertexOutput ParticleVS(VertexInput input)
{
    VertexOutput output;
    float3 center = input.Center;
    float2 size = input.Shape.xy;
    float2 c = input.Corner;
    float3 toCamera = CameraPosition - center;
    float distance = max(length(toCamera), 0.001f);
    toCamera /= distance;

    //Widen sub-pixel particles to a pixel and fade them by the same ratio (distant sparks stay smooth)
    float minSize = distance * PixelAngle;
    float coverage = saturate(size.x / max(minSize, 0.0001f)) * saturate(size.y / max(minSize, 0.0001f));
    size = max(size, minSize);

    float s, co;
    sincos(input.Shape.z, s, co);
    float2 r = float2(c.x * co - c.y * s, c.x * s + c.y * co);

    float3 offset;
    if (RenderMode < 0.5f)
    {
        offset = (CameraRight * r.x * size.x + CameraUp * r.y * size.y) * 0.5f;
    }
    else if (RenderMode < 1.5f)
    {
        float speed = length(input.Velocity.xyz);
        float3 axis = speed > 0.0001f ? input.Velocity.xyz / speed : CameraUp;
        float3 side = cross(axis, toCamera);
        float sideLength = length(side);
        side = sideLength > 0.02f ? side / sideLength : CameraRight;
        float stretch = max(size.y * LengthScale + speed * SpeedScale, size.x);
        offset = side * c.x * size.x * 0.5f + axis * c.y * stretch * 0.5f;
    }
    else if (RenderMode < 2.5f)
    {
        offset = float3(r.x * size.x, r.y * size.y, 0) * 0.5f;
    }
    else
    {
        float3 right = cross(float3(0, 0, 1), toCamera);
        float rightLength = length(right);
        right = rightLength > 0.02f ? right / rightLength : CameraRight;
        offset = (right * r.x * size.x + float3(0, 0, 1) * r.y * size.y) * 0.5f;
    }
    float3 world = center + offset;

    //Texture sheet frame: left to right, top to bottom
    float2 uv = float2(c.x * 0.5f + 0.5f, 0.5f - c.y * 0.5f);
    float frame = input.Shape.w;
    float column = fmod(frame, Tiles.x);
    float row = floor(frame / Tiles.x);
    uv = (uv + float2(column, row)) / Tiles;

    float3 color = pow(saturate(input.Color.rgb), 2.2f) * Brightness;
    if (Lit)
    {
        //A puff, not a card: the normal bulges towards the camera from the quad's edges
        float3 normal = normalize(normalize(offset + 0.0001f) * 0.75f + toCamera * 0.66f);
        float3 L = normalize(LightDirection);
        float wrap = saturate(dot(normal, L) * 0.5f + 0.5f);
        float behind = pow(saturate(dot(-toCamera, L)), 8);
        color *= Ambient(normal) + LightColor * (wrap * 0.9f + behind * 0.6f);
    }

    output.Position = mul(float4(world, 1), ViewProj);
    output.TexCoord = uv;
    output.Color = float4(color, saturate(input.Color.a) * coverage);
    output.ViewDepth = -mul(float4(world, 1), View).z;
    return output;
}

float4 ParticlePS(VertexOutput input) : SV_TARGET
{
    float4 texel = ParticleTexture.Sample(ParticleSampler, input.TexCoord);
    float alpha = input.Color.a * texel.a;
    if (HasDepth)
    {
        float sceneDepth = DepthMap.Load(int3(input.Position.xy, 0)).r * FarClip;
        alpha *= saturate((sceneDepth - input.ViewDepth) / max(SoftParticles, 0.01f));
    }
    alpha *= smoothstep(0.05f, 0.05f + max(CameraFade, 0.001f), input.ViewDepth);
    clip(alpha - 0.002f);
    return float4(input.Color.rgb * texel.rgb, alpha);
}

// ---------------------------------------------------------------------------------------------------
// Volumetric systems (ParticleVolumetricModule): a full-screen pass per volume, scissored to its box on screen,
// marches the camera ray through the density grid (Engine/Particles/ParticleVolume.cs) up to the scene depth.
// Detail noise erodes the edges into wisps, carves (bullet tunnels, blast holes) cut through it, and every
// sample is lit by the sky plus the sun, which is marched a few cells towards the light for self-shadowing.
// Output is premultiplied: rgb = scattered light, a = 1 - transmittance.
// ---------------------------------------------------------------------------------------------------

#define MAX_CARVES 16

float4x4 InverseViewProjection;
float3 CameraForward;
float3 VolumeMin;           //world minimum corner of the grid
float3 VolumeSize;          //world extent of the grid
float VolumeCell;           //cell edge, metres
float VolumeDensity;        //extinction per metre at density 1
float VolumeErosion;        //0..1 noise eating into the density
float VolumeNoiseScale;     //noise features per metre
float3 VolumeNoiseOffset;   //drift, in noise tiles
float VolumeSteps;          //most samples per pixel
float VolumeJitter;         //frame number: decorrelates the start offset (TAA averages it)
bool VolumeUniformColor;    //voxels: colour is VolumeColor and the texture's R is the sunlight
float3 VolumeColor;         //sRGB
int CarveCount;
float4 CarveFrom[MAX_CARVES]; //xyz start, w radius
float4 CarveTo[MAX_CARVES];   //xyz end

Texture3D VolumeTexture;
SamplerState VolumeSampler
{
    Texture = <VolumeTexture>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    AddressW = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    MipFilter = POINT;
};

Texture3D NoiseTexture;
SamplerState NoiseSampler
{
    Texture = <NoiseTexture>;
    AddressU = WRAP;
    AddressV = WRAP;
    AddressW = WRAP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    MipFilter = POINT;
};

struct VolumeInput
{
    float2 Position : POSITION0;
};
struct VolumeOutput
{
    float4 Position : SV_POSITION;
    float2 Clip : TEXCOORD0;
};

VolumeOutput VolumeVS(VolumeInput input)
{
    VolumeOutput output;
    output.Position = float4(input.Position, 0, 1);
    output.Clip = input.Position;
    return output;
}

float4 VolumeCellAt(float3 p)
{
    return VolumeTexture.SampleLevel(VolumeSampler, (p - VolumeMin) / VolumeSize, 0);
}

//Billowy (Worley) and wispy (value fbm) noise in three octaves, the first warping the others so the billows
//don't line up into a pattern; the texture tiles every 4 features
float VolumeNoise(float3 p)
{
    float3 q = p * (VolumeNoiseScale * 0.25f) + VolumeNoiseOffset;
    float4 a = NoiseTexture.SampleLevel(NoiseSampler, q, 0);
    float3 warp = (a.gbr - 0.5f) * 0.35f;
    float4 b = NoiseTexture.SampleLevel(NoiseSampler, q * 2.13f + warp + 0.37f, 0);
    float4 c = NoiseTexture.SampleLevel(NoiseSampler, q * 4.71f - warp * 1.7f + 0.71f, 0);
    float billow = lerp(a.g, a.r, 0.45f);
    return saturate(billow * 0.55f + lerp(b.g, b.r, 0.35f) * 0.3f + c.g * 0.15f);
}

//0 inside a hole, 1 outside (soft rim); ParticleVolume.CarveFactor is the same shape on the CPU
float Carve(float3 p)
{
    float f = 1;
    [loop]
    for (int i = 0; i < CarveCount; i++)
    {
        float3 a = CarveFrom[i].xyz;
        float3 ab = CarveTo[i].xyz - a;
        float r = CarveFrom[i].w;
        float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6f));
        float d = length(p - (a + ab * t));
        f *= saturate((d - r * 0.6f) / max(r * 0.4f, 1e-4f));
    }
    return f;
}

//Share of the sun reaching p through the volume itself
float SunThroughVolume(float3 p, float3 L)
{
    float step = VolumeCell * 2.0f;
    float optical = 0;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        p += L * step * (i == 0 ? 0.5f : 1.0f);
        optical += VolumeCellAt(p).a * 2.0f;
    }
    //Light scattered many times inside smoke still gets deep in: a second, much weaker extinction
    float depth = optical * step * VolumeDensity * 0.6f;
    return exp(-depth) * 0.65f + exp(-depth * 0.2f) * 0.35f;
}

float4 VolumePS(VolumeOutput input) : SV_TARGET
{
    float4 far = mul(float4(input.Clip, 1, 1), InverseViewProjection);
    float3 dir = normalize(far.xyz / far.w - CameraPosition);

    //Where the ray is inside the grid's box
    float3 safe = float3(abs(dir.x) > 1e-6f ? dir.x : 1e-6f, abs(dir.y) > 1e-6f ? dir.y : 1e-6f, abs(dir.z) > 1e-6f ? dir.z : 1e-6f);
    float3 t0 = (VolumeMin - CameraPosition) / safe;
    float3 t1 = (VolumeMin + VolumeSize - CameraPosition) / safe;
    float3 tMin = min(t0, t1), tMax = max(t0, t1);
    float tEnter = max(max(tMin.x, tMin.y), max(tMin.z, 0.05f));
    float tExit = min(min(tMax.x, tMax.y), tMax.z);
    //...and in front of the scene (the sky is cleared to depth 1)
    if (HasDepth)
    {
        float depth = DepthMap.Load(int3(input.Position.xy, 0)).r;
        if (depth < 0.9999f) tExit = min(tExit, depth * FarClip / max(dot(dir, CameraForward), 0.001f));
    }
    clip(tExit - tEnter - 0.001f);

    float span = tExit - tEnter;
    float steps = clamp(ceil(span / (VolumeCell * 0.5f)), 4, VolumeSteps);
    float stepLength = span / steps;
    //Interleaved gradient noise: a different start offset per pixel and frame
    float2 pixel = input.Position.xy + VolumeJitter * float2(5.588238f, 5.588238f);
    float jitter = frac(52.9829189f * frac(dot(pixel, float2(0.06711056f, 0.00583715f))));
    float t = tEnter + stepLength * jitter;

    float3 L = normalize(LightDirection);
    //Henyey-Greenstein (g = 0.35) relative to isotropic: brighter looking towards the sun
    float g = 0.35f;
    float cosTheta = dot(dir, L);
    float phase = (1 - g * g) / pow(abs(1 + g * g - 2 * g * cosTheta), 1.5f);
    float3 sky = Ambient(float3(0, 0, 1));
    float3 uniformAlbedo = pow(saturate(VolumeColor), 2.2f);

    float transmittance = 1;
    float3 light = 0;
    [loop]
    for (float i = 0; i < steps; i++)
    {
        float3 p = CameraPosition + dir * t;
        t += stepLength;
        float4 cell = VolumeCellAt(p);
        float base = cell.a * 2.0f;
        [branch]
        if (base > 0.004f)
        {
            //Noise eats into the thin edges more than the thick core
            float erosion = VolumeErosion * (1.15f - 0.5f * saturate(base));
            float density = max(0, base * 1.25f - (1 - VolumeNoise(p)) * erosion);
            if (CarveCount > 0) density *= Carve(p);
            [branch]
            if (density > 0.001f)
            {
                float3 albedo = VolumeUniformColor ? uniformAlbedo : pow(saturate(cell.rgb), 2.2f);
                float3 color = albedo;
                if (Lit)
                {
                    //Voxels under a roof or behind a wall get no sun (R), only the sky
                    float sunlit = VolumeUniformColor ? cell.r : 1;
                    float sun = sunlit > 0.004f ? SunThroughVolume(p, L) * sunlit : 0;
                    color *= sky * (0.55f + 0.45f * sun) + LightColor * sun * phase;
                }
                color *= Brightness;
                float through = exp(-density * VolumeDensity * stepLength);
                light += transmittance * color * (1 - through);
                transmittance *= through;
                if (transmittance < 0.01f) break;
            }
        }
    }
    float alpha = transmittance < 0.01f ? 1 : 1 - transmittance;
    clip(alpha - 0.001f);
    return float4(light, alpha);
}

technique Particles
{
    pass Pass0
    {
        VertexShader = compile vs_5_0 ParticleVS();
        PixelShader = compile ps_5_0 ParticlePS();
    }
}

technique Volume
{
    pass Pass0
    {
        VertexShader = compile vs_5_0 VolumeVS();
        PixelShader = compile ps_5_0 VolumePS();
    }
}
