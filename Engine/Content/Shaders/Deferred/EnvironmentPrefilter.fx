// GGX prefiltering of environment cubemaps (Source 2 look, Docs/Research/Source2Look phase 3.1).
// Renders one face of one mip of the prefiltered cube: mip m holds the radiance a GGX lobe of roughness
// m / (mips - 1) gathers around each direction (N = V = R, the split-sum assumption), importance sampled with
// filtered importance sampling (each sample reads the source mip that matches its solid angle).
// Renderer.Lighting.EnvironmentPrefilter copies the result into a TextureCube.

TextureCube SourceCube;
SamplerState SourceSampler
{
    Texture = <SourceCube>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};

int Face = 0;
float Roughness = 0;
float SourceSize = 512;     // texels per side of the source's mip 0
float SourceMaxMip = 9;
int SampleCount = 64;

static const float PI = 3.14159265f;

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VertexShaderOutput VertexShaderFunction(float2 position : POSITION0, uint id : SV_VERTEXID)
{
    VertexShaderOutput output;
    output.Position = float4(position, 0, 1);
    output.TexCoord.x = (float)(id / 2) * 2.0;
    output.TexCoord.y = 1.0 - (float)(id % 2) * 2.0;
    return output;
}

// Direction of a texel of a cube face (Direct3D convention: the hardware reads it back for this direction)
float3 FaceDirection(int face, float2 uv)
{
    float sc = uv.x * 2 - 1;
    float tc = uv.y * 2 - 1;
    float3 d;
    if (face == 0) d = float3(1, -tc, -sc);
    else if (face == 1) d = float3(-1, -tc, sc);
    else if (face == 2) d = float3(sc, 1, tc);
    else if (face == 3) d = float3(sc, -1, -tc);
    else if (face == 4) d = float3(sc, -tc, 1);
    else d = float3(-sc, -tc, -1);
    return normalize(d);
}

float RadicalInverse(uint bits)
{
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float(bits) * 2.3283064365386963e-10;
}

float3 ImportanceSampleGGX(float2 xi, float3 n, float alpha)
{
    float phi = 2 * PI * xi.x;
    float cosTheta = sqrt((1 - xi.y) / (1 + (alpha * alpha - 1) * xi.y));
    float sinTheta = sqrt(1 - cosTheta * cosTheta);
    float3 h = float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
    float3 up = abs(n.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 tangentX = normalize(cross(up, n));
    float3 tangentY = cross(n, tangentX);
    return tangentX * h.x + tangentY * h.y + n * h.z;
}

float4 PrefilterPS(VertexShaderOutput input) : SV_TARGET0
{
    float3 n = FaceDirection(Face, input.TexCoord);

    [branch]
    if (Roughness <= 0.001f)
        return float4(SourceCube.SampleLevel(SourceSampler, n, 0).rgb, 1);

    float alpha = Roughness * Roughness;
    float a2 = alpha * alpha;
    float texelSolidAngle = 4 * PI / (6 * SourceSize * SourceSize);

    float3 sum = 0;
    float weight = 0;
    [loop]
    for (int i = 0; i < SampleCount; i++)
    {
        float2 xi = float2((i + 0.5f) / SampleCount, RadicalInverse((uint)i));
        float3 h = ImportanceSampleGGX(xi, n, alpha);
        float3 l = 2 * dot(n, h) * h - n;
        float ndotl = saturate(dot(n, l));
        if (ndotl > 0)
        {
            float ndoth = saturate(dot(n, h));
            float d = ndoth * ndoth * (a2 - 1) + 1;
            float D = a2 / (PI * d * d);
            float pdf = D / 4 + 1e-4f;   // N = V: D * NdotH / (4 VdotH) = D / 4
            float sampleSolidAngle = 1 / (SampleCount * pdf);
            float mip = clamp(0.5f * log2(sampleSolidAngle / texelSolidAngle) + 1, 0, SourceMaxMip);
            sum += SourceCube.SampleLevel(SourceSampler, l, mip).rgb * ndotl;
            weight += ndotl;
        }
    }
    return float4(sum / max(weight, 1e-4f), 1);
}

technique Prefilter
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_5_0 PrefilterPS();
    }
}
