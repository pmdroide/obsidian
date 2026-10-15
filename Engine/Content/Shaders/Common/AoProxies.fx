// Ambient occlusion proxies (Source 2 look, Docs/Research/Source2Look phase 6.1; AO Proxy components, AoProxyShapes):
// analytic ellipsoids and capsules that darken the indirect light around dynamic objects, a soft contact shadow
// without SSAO. Sphere occlusion after Inigo Quilez: cos(theta) * r^2 / d^2, cut off smoothly Falloff radii away.

#define MAX_AO_PROXIES 16

int AoProxyCount = 0;
float4x4 AoProxyWorldToUnit[MAX_AO_PROXIES];  //ellipsoids: world -> unit sphere
float4 AoProxyA[MAX_AO_PROXIES];              //xyz = capsule start / ellipsoid centre, w = 0 ellipsoid, 1 capsule
float4 AoProxyB[MAX_AO_PROXIES];              //xyz = capsule end, w = capsule radius
float4 AoProxySettings[MAX_AO_PROXIES];       //x = intensity, y = falloff in radii

//Occlusion of a sphere (centre c, radius r) at a point p with normal n; 0 inside it (the proxy's own surface)
float SphereOcclusion(float3 p, float3 n, float3 c, float r, float falloff)
{
    float3 d = c - p;
    float l = length(d);
    if (l <= r) return 0;
    float cosine = saturate(dot(n, d / l));
    float reach = r * (1 + falloff);
    return cosine * saturate(r * r / (l * l)) * saturate((reach - l) / max(reach - r, 1e-4f));
}

//1 = unoccluded
float AoProxyOcclusion(float3 positionWS, float3 normalWS)
{
    float visibility = 1;
    [loop]
    for (int i = 0; i < AoProxyCount; i++)
    {
        float occlusion;
        float falloff = AoProxySettings[i].y;
        [branch]
        if (AoProxyA[i].w > 0.5f)
        {
            //Capsule: the sphere at the nearest point of its segment
            float3 a = AoProxyA[i].xyz, ab = AoProxyB[i].xyz - a;
            float t = saturate(dot(positionWS - a, ab) / max(dot(ab, ab), 1e-6f));
            occlusion = SphereOcclusion(positionWS, normalWS, a + ab * t, AoProxyB[i].w, falloff);
        }
        else
        {
            //Ellipsoid: distances in its unit sphere space, the direction to its centre in world space
            float3 p = mul(float4(positionWS, 1), AoProxyWorldToUnit[i]).xyz;
            float l = length(p);
            if (l <= 1) continue;
            float cosine = saturate(dot(normalWS, normalize(AoProxyA[i].xyz - positionWS)));
            float reach = 1 + falloff;
            occlusion = cosine * saturate(1 / (l * l)) * saturate((reach - l) / falloff);
        }
        visibility *= 1 - occlusion * AoProxySettings[i].x;
    }
    return visibility;
}
