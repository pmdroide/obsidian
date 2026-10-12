// Editor ground grid: an infinite plane at Z = GridHeight (the ground, Z is up), drawn as a full-screen pass
// over the final image (Engine/Renderer/RenderModules/GridRenderModule.cs). Each pixel's view ray is cut with
// the plane; lines are anti-aliased with screen-space derivatives. Three levels (1x, 10x, 100x the cell size)
// fade in and out with how many pixels a cell covers, so the grid stays readable from any height. The X axis
// is red, the Y axis green. Geometry in front of the plane hides it through the linear G-buffer depth.
float4x4 InverseViewProjection; //unjittered
float4x4 View;
float3 CameraPosition;
float GridHeight;    //world Z of the ground
float CellSize;      //metres between minor lines
float FadeDistance;  //metres from the camera where the grid is gone
float Opacity;
float3 LineColor;
float3 AxisXColor;
float3 AxisYColor;

bool HasDepth;
Texture2D DepthMap; //linear G-buffer depth, view Z / -FarClip
float FarClip;
SamplerState DepthSampler
{
    Texture = <DepthMap>;
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = POINT;
    MinFilter = POINT;
    MipFilter = POINT;
};

struct VertexInput
{
    float2 Position : POSITION0;
};
struct VertexOutput
{
    float4 Position : SV_POSITION;
    float2 Clip : TEXCOORD0;
};

VertexOutput GridVS(VertexInput input)
{
    VertexOutput output;
    output.Position = float4(input.Position, 0, 1);
    output.Clip = input.Position;
    return output;
}

//Coverage of the lines through every multiple of 'size' along x and y, about 'width' pixels wide
float2 Lines(float2 p, float size, float width)
{
    float2 coord = p / size;
    float2 derivative = max(fwidth(coord), 1e-6f);
    float2 distance = abs(frac(coord - 0.5f) - 0.5f) / derivative;
    return 1 - saturate(distance - (width * 0.5f - 0.5f));
}

//One grid level: fades out once its cells shrink below a few pixels
float Level(float2 p, float size, float pixelSize, float width)
{
    float cellPixels = size / pixelSize;
    float2 lines = Lines(p, size, width);
    return max(lines.x, lines.y) * smoothstep(3, 12, cellPixels);
}

float4 GridPS(VertexOutput input) : SV_TARGET
{
    float4 nearPoint = mul(float4(input.Clip, 0, 1), InverseViewProjection);
    float4 farPoint = mul(float4(input.Clip, 1, 1), InverseViewProjection);
    float3 direction = normalize(farPoint.xyz / farPoint.w - nearPoint.xyz / nearPoint.w);

    //Ray from the camera to the plane; the derivatives below need every pixel, so no early clip
    float t = abs(direction.z) > 1e-5f ? (GridHeight - CameraPosition.z) / direction.z : -1;
    bool hit = t > 0;
    float3 world = CameraPosition + direction * max(t, 0);
    float2 p = world.xy;

    //World size of one pixel on the plane, for the level fades
    float2 dx = ddx(p), dy = ddy(p);
    float pixelSize = max(sqrt(max(dot(dx, dx), dot(dy, dy))), 1e-5f);

    float minor = Level(p, CellSize, pixelSize, 1.0f);
    float major = Level(p, CellSize * 10, pixelSize, 1.2f);
    float huge = Level(p, CellSize * 100, pixelSize, 1.4f);
    float alpha = max(minor * 0.22f, max(major * 0.45f, huge * 0.6f));
    float3 color = LineColor;

    //Axes: the X axis runs along y = 0, the Y axis along x = 0
    float2 axisDerivative = max(fwidth(p), 1e-6f);
    float2 axis = 1 - saturate(abs(p) / axisDerivative - 0.5f);
    if (axis.y > 0.001f) { color = lerp(color, AxisXColor, axis.y); alpha = max(alpha, axis.y * 0.85f); }
    if (axis.x > 0.001f) { color = lerp(color, AxisYColor, axis.x); alpha = max(alpha, axis.x * 0.85f); }

    //Fade with distance and at grazing angles, where the lines would turn into noise
    float distance = length(world - CameraPosition);
    alpha *= 1 - smoothstep(FadeDistance * 0.4f, FadeDistance, distance);
    alpha *= smoothstep(0.0f, 0.08f, abs(direction.z));

    //Hidden behind geometry; a floor lying on the plane keeps the grid on top
    if (HasDepth)
    {
        float2 uv = input.Clip * float2(0.5f, -0.5f) + 0.5f;
        float depth = DepthMap.SampleLevel(DepthSampler, uv, 0).r;
        if (depth < 0.9999f)
        {
            float sceneDepth = depth * FarClip;
            float gridDepth = -mul(float4(world, 1), View).z;
            alpha *= 1 - saturate((gridDepth - sceneDepth * 1.002f - 0.02f) / 0.05f);
        }
    }

    alpha *= Opacity * (hit ? 1 : 0);
    clip(alpha - 0.002f);
    return float4(color, alpha);
}

technique Grid
{
    pass Pass0
    {
        VertexShader = compile vs_5_0 GridVS();
        PixelShader = compile ps_5_0 GridPS();
    }
}
