// Weather: rain streaks, sand grains and snow flakes in a box of particles that follows the camera, plus
// a distance haze and a ground mist. Particles live on a world-space lattice that repeats every Box metres, so they stay put
// when the camera moves and wrap around it. Fall and wind are CPU-accumulated offsets per speed group
// (Engine/Renderer/RenderModules/WeatherRenderModule.cs), so changing the wind never makes them jump.
// Both passes read the linear G-buffer depth instead of a depth buffer: particles fade into geometry and
// the haze thickens with distance. Lighting follows the scene light and the captured sky, like Water.fx.
float4x4 ViewProj;
float4x4 View;
float4x4 InverseViewProjection;
float3 CameraPosition;
float3 CameraRight;
float3 CameraUp;

float3 Box;              //size of the particle box, metres
float3 Offsets[4];       //accumulated fall + wind per speed group, wrapped to Box
float4 GroupSpeed;       //speed multiplier per group, matches the offsets
float3 Velocity;         //average particle velocity, m/s
float Time;              //seconds, wrapped on the CPU
float StreakSeconds;     //streak length = speed * StreakSeconds
bool IsStreak;           //rain and sand are velocity-aligned streaks; snow is round flakes
float Size;              //streak width or flake diameter, metres
float Sway;              //side-to-side flutter, metres
float PixelAngle;        //world size of one pixel at 1 m; thinner particles widen and fade instead of aliasing
float Opacity;
float3 Tint;             //linear
float LightResponse;
float3 LightDirection;   //towards the light
float3 LightColor;       //colour * intensity * 0.1, like the deferred lights

float3 HazeColor;        //linear
float HazeDensity;       //per metre of view depth
float HazeAmount;        //most of the view the haze can cover

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

float3 SampleEnvironment(float3 direction, float mip)
{
    return EnvironmentMap.SampleLevel(EnvironmentSampler, float3(direction.xy, -direction.z), mip).rgb;
}

//Sky light around a direction: mostly the zenith, some of the horizon the particle is seen against
float3 Ambient(float3 direction)
{
    if (!HasEnvironment) return LightColor * 0.05f;
    float3 side = normalize(float3(direction.xy, 0.0001f) + float3(0, 0, 0.2f));
    return SampleEnvironment(float3(0, 0, 1), 9) * 0.6f + SampleEnvironment(side, 7) * 0.4f;
}

float Hash(float3 p)
{
    return frac(sin(dot(p, float3(12.9898f, 78.233f, 37.719f))) * 43758.5453f);
}

// ---------------------------------------------------------------------------------------------------
// Particles
// ---------------------------------------------------------------------------------------------------

struct ParticleInput
{
    float4 Seed : POSITION0;   //xyz = position in the box (0..1), w = speed group and size
    float2 Corner : TEXCOORD0; //-1..1
};
struct ParticleOutput
{
    float4 Position : SV_POSITION;
    float2 Corner : TEXCOORD0;
    float4 Color : TEXCOORD1;  //rgb lit colour, a opacity
    float ViewDepth : TEXCOORD2;
};

ParticleOutput ParticleVS(ParticleInput input)
{
    ParticleOutput output;
    uint group = min((uint)(input.Seed.w * 4), 3);
    float speed = GroupSpeed[group];
    float h = Hash(input.Seed.xyz);

    //Pick the copy of this lattice point that lies in the box around the camera
    float3 origin = CameraPosition - Box * 0.5f;
    float3 p = input.Seed.xyz * Box + Offsets[group];
    p -= Box * floor((p - origin) / Box);
    float3 relative = p - CameraPosition;

    //Flutter: a slow loop for snow, gusty jitter for sand
    float phase = h * 6.2831853f;
    float frequency = 0.5f + h * 0.9f;
    float2 sway = float2(sin(Time * frequency + phase), cos(Time * frequency * 0.83f + phase * 1.7f)) * Sway;
    p.xy += sway;
    float3 velocity = Velocity * speed;
    velocity.xy += float2(cos(Time * frequency + phase), -sin(Time * frequency * 0.83f + phase * 1.7f) * 0.83f) * Sway * frequency;

    //Fade at the box faces so the wrap is never seen, and right in front of the eye
    float2 edge = abs(relative.xy) / (Box.xy * 0.5f);
    float fade = 1 - smoothstep(0.7f, 1.0f, max(edge.x, edge.y));
    fade *= 1 - smoothstep(0.65f, 1.0f, abs(relative.z) / (Box.z * 0.5f));
    float distance = length(p - CameraPosition);
    fade *= smoothstep(0.6f, 2.5f, distance);

    //Widen sub-pixel particles to one pixel and fade them by the same ratio, so distant rain stays smooth
    float width = Size * (0.7f + 0.6f * frac(h * 7.31f));
    float minWidth = distance * PixelAngle;
    float coverage = width / max(width, minWidth);
    width = max(width, minWidth);

    float3 toCamera = (CameraPosition - p) / max(distance, 0.001f);
    float3 world;
    if (IsStreak)
    {
        float particleSpeed = length(velocity);
        float3 axis = particleSpeed > 0.001f ? velocity / particleSpeed : float3(0, 0, -1);
        float3 side = cross(axis, toCamera);
        float sideLength = length(side);
        side = sideLength > 0.05f ? side / sideLength : CameraRight;
        float streak = max(particleSpeed * StreakSeconds, width);
        world = p + side * input.Corner.x * width * 0.5f + axis * input.Corner.y * streak * 0.5f;
    }
    else
    {
        world = p + (CameraRight * input.Corner.x + CameraUp * input.Corner.y) * width * 0.5f;
    }

    //Lit per particle: sky ambient plus the light, brighter when looking towards it (backlit drops and dust)
    float3 L = normalize(LightDirection);
    float forward = pow(saturate(dot(-toCamera, L)), 8);
    float3 color = Tint * (Ambient(-toCamera) + LightColor * LightResponse * (0.5f + 2.0f * forward));

    output.Position = mul(float4(world, 1), ViewProj);
    output.Corner = input.Corner;
    output.Color = float4(color, Opacity * fade * coverage);
    output.ViewDepth = -mul(float4(world, 1), View).z;
    return output;
}

float4 ParticlePS(ParticleOutput input) : SV_TARGET
{
    float2 c = input.Corner;
    float shape;
    if (IsStreak)
    {
        //Soft across, tapered towards both ends
        shape = (1 - c.x * c.x) * sqrt(saturate(1 - abs(c.y)));
    }
    else
    {
        shape = saturate(1 - dot(c, c));
        shape *= shape;
    }
    float alpha = input.Color.a * shape;
    if (HasDepth)
    {
        float sceneDepth = DepthMap.Load(int3(input.Position.xy, 0)).r * FarClip;
        alpha *= saturate((sceneDepth - input.ViewDepth) / 0.3f);
    }
    clip(alpha - 0.002f);
    return float4(input.Color.rgb, alpha);
}

// ---------------------------------------------------------------------------------------------------
// Haze
// ---------------------------------------------------------------------------------------------------

struct HazeInput
{
    float2 Position : POSITION0;
};
struct HazeOutput
{
    float4 Position : SV_POSITION;
    float2 Clip : TEXCOORD0;
};

HazeOutput HazeVS(HazeInput input)
{
    HazeOutput output;
    output.Position = float4(input.Position, 0, 1);
    output.Clip = input.Position;
    return output;
}

float4 HazePS(HazeOutput input) : SV_TARGET
{
    //The sky (cleared to depth 1) gets the full haze
    float depth = HasDepth ? DepthMap.Load(int3(input.Position.xy, 0)).r : 1;
    float viewDepth = depth >= 0.9999f ? 1e6f : depth * FarClip;
    float amount = HazeAmount * (1 - exp(-viewDepth * HazeDensity));

    float4 far = mul(float4(input.Clip, 1, 1), InverseViewProjection);
    float3 direction = normalize(far.xyz / far.w - CameraPosition);
    float3 L = normalize(LightDirection);
    float forward = pow(saturate(dot(direction, L)), 6);
    float3 color = HazeColor * (Ambient(direction) + LightColor * (0.06f + 0.5f * forward));
    return float4(color, amount);
}

// ---------------------------------------------------------------------------------------------------
// Ground mist: low fog lying on the ground in the rain (and dust in a sandstorm). Density falls off
// exponentially above MistBase; wind-blown wisps of 2D noise thin it out in patches. The view ray is
// clipped to the layer and marched in a few steps (jittered per pixel and frame, TAA smooths it).
// ---------------------------------------------------------------------------------------------------

float3 CameraForward;
float MistDensity;       //per metre at MistBase
float MistBase;          //world Z of the ground
float MistHeight;        //density falls to 1/e this far above the base, metres
float3 MistColor;        //linear
float4 MistDrift;        //wind-blown offset of the wisps, metres: xy broad shapes, zw faster detail (wrapped every MistPeriod)
float MistMaxDistance;   //the sky and far geometry see this much of the layer
float FrameIndex;        //jitter seed

float MistHash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031f);
    p3 += dot(p3, p3.yzx + 33.33f);
    return frac((p3.x + p3.y) * p3.z);
}

//Drift wraps every MistPeriod metres (WeatherRenderModule.MistDriftPeriod); each octave repeats after a whole
//number of its cells over that distance, so the wrap never shows.
static const float MistPeriod = 2000;

//Value noise that repeats every 'period' cells
float MistNoise(float2 p, float period)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    float2 i0 = i - period * floor(i / period);
    float2 i1 = i0 + 1;
    i1 -= period * floor(i1 / period);
    return lerp(lerp(MistHash(i0), MistHash(float2(i1.x, i0.y)), u.x),
                lerp(MistHash(float2(i0.x, i1.y)), MistHash(i1), u.x), u.y);
}

//Wisps: 0 (clear) .. ~1.6 (thick), 1 on average. Detail fades out with distance (detail 0 = average).
float MistWisps(float3 p, float detail)
{
    //Broad banks drift with the wind, the finer curls faster; height shifts the pattern so the mist has depth.
    //Scales are 90, 260 and 740 cells per MistPeriod.
    float2 q = p.xy + MistDrift.xy;
    float2 r = p.xy + MistDrift.zw;
    float n = MistNoise(q * 0.045f, 90) * 0.55f;
    n += MistNoise(r * 0.13f + float2(p.z * 0.35f, 31.7f), 260) * 0.3f;
    n += MistNoise(r * 0.37f + float2(17.3f, -p.z * 0.6f), 740) * 0.15f;
    float wisps = saturate((n - 0.25f) * 2.2f) * 1.6f;
    return lerp(1, wisps, detail);
}

float MistLayerDensity(float3 p)
{
    return MistDensity * exp(-max(p.z - MistBase, 0) / MistHeight);
}

float4 GroundFogPS(HazeOutput input) : SV_TARGET
{
    float depth = HasDepth ? DepthMap.Load(int3(input.Position.xy, 0)).r : 1;
    float4 far = mul(float4(input.Clip, 1, 1), InverseViewProjection);
    float3 direction = normalize(far.xyz / far.w - CameraPosition);
    //Linear depth is view Z; turn it into a distance along this pixel's ray
    float along = max(dot(direction, CameraForward), 0.01f);
    float distance = depth >= 0.9999f ? MistMaxDistance : min(depth * FarClip / along, MistMaxDistance);

    //Clip the ray to the slab below the layer's top, where the density has fallen to ~1%
    float top = MistBase + MistHeight * 4.5f;
    float t0 = 0;
    float t1 = distance;
    if (abs(direction.z) > 1e-4f)
    {
        float tTop = (top - CameraPosition.z) / direction.z;
        if (CameraPosition.z > top)
        {
            if (direction.z >= 0) discard;
            t0 = tTop;
        }
        else if (direction.z > 0)
            t1 = min(t1, tTop);
    }
    else if (CameraPosition.z > top) discard;
    if (t1 <= t0) discard;

    //March: steps packed towards the camera, where the wisps are seen in detail
    const int Steps = 12;
    float jitter = MistHash(input.Position.xy + FrameIndex * 17.13f);
    float opticalDepth = 0;
    float previous = t0;
    [loop]
    for (int i = 0; i < Steps; i++)
    {
        float s = (i + jitter) / Steps;
        float t = lerp(t0, t1, s * s);
        float next = lerp(t0, t1, ((i + 1.0f) / Steps) * ((i + 1.0f) / Steps));
        float3 p = CameraPosition + direction * t;
        float detail = 1 - smoothstep(40, 140, t);
        opticalDepth += MistLayerDensity(p) * MistWisps(p, detail) * (next - previous);
        previous = next;
    }
    float alpha = 1 - exp(-opticalDepth);
    clip(alpha - 0.002f);

    //Lit like the haze: sky ambient from below the horizon line, the sun glowing through it
    float3 L = normalize(LightDirection);
    float forward = pow(saturate(dot(direction, L)), 6);
    float3 color = MistColor * (Ambient(direction) + LightColor * (0.08f + 0.6f * forward));
    return float4(color, alpha);
}

technique GroundFog
{
    pass Pass0
    {
        VertexShader = compile vs_5_0 HazeVS();
        PixelShader = compile ps_5_0 GroundFogPS();
    }
}

technique Particles
{
    pass Pass0
    {
        VertexShader = compile vs_5_0 ParticleVS();
        PixelShader = compile ps_5_0 ParticlePS();
    }
}

technique Haze
{
    pass Pass0
    {
        VertexShader = compile vs_5_0 HazeVS();
        PixelShader = compile ps_5_0 HazePS();
    }
}
