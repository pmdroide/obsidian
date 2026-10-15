// Baked probe volumes (Engine/Renderer/Lighting): parameters and lookup shared by the environment pass and the forward
// pass (Source 2 look, Docs/Research/Source2Look phases 3.4 and 4.1).
//
// Each probe stores cosine-convolved L1 SH per colour channel as (c0, cx, cy, cz): irradiance E(n) = c0 + dot(c.yzw, n),
// in deferred light units. Probes sit on the corners of a regular grid. A scene has its main volume and up to three
// detail volumes (Probe Volume components), blended highest priority first, each fading in over its margin.

#define MAX_PROBE_VOLUMES 4

bool UseProbeVolume = false;
float ProbeVolumeIntensity = 1.0f;
//The main volume (the froxel fog and the "half a cell out" offset use it)
float3 ProbeVolumeMin;
float3 ProbeVolumeExtentRcp;
float3 ProbeVolumeCells; //probes per axis - 1
//Probe SH at the reflection cubemap's capture point (EnvironmentSample position), same layout as a probe
float4 CaptureSHR;
float4 CaptureSHG;
float4 CaptureSHB;

//Every volume, highest priority first (the main one last): xyz min / 1 / extent / probes per axis - 1,
//VolumeCells.w = the volume's first Z slice in ProbeData, VolumeMin.w = blend margin in metres (0 = half a cell)
int ProbeVolumeCount = 0;
float4 ProbeVolumeMins[MAX_PROBE_VOLUMES];
float4 ProbeVolumeExtentRcps[MAX_PROBE_VOLUMES];
float4 ProbeVolumeCellCounts[MAX_PROBE_VOLUMES];

//Baked probe data (LightingSystem.ProbeData): per volume five grids side by side along X (SH red, green, blue, then the
//mean free distances along +X/+Y/+Z and -X/-Y/-Z), volumes stacked along Z. One texture keeps the passes under
//MonoGame's 16 texture slots.
Texture3D ProbeData;

//A probe of the main volume (Froxel.fx has its own copy for the fog)
float4 ProbeTexel(int4 index, int grid)
{
    index.x += grid * ((int)ProbeVolumeCells.x + 1);
    return ProbeData.Load(index);
}

float4 VolumeTexel(int4 index, int grid, float4 cellCounts)
{
    index.x += grid * ((int)cellCounts.x + 1);
    index.z += (int)cellCounts.w;
    return ProbeData.Load(index);
}

//1 while the shaded point is within the probe's free distance in that direction, falling off
//steeply beyond it, i.e. once a wall is in the way. Same as ProbeVolumeData.Visibility.
float ProbeVisibility(float3 toPoint, float3 distPos, float3 distNeg)
{
	float d = length(toPoint);
	if (d < 0.0001f) return 1;
	float3 dir = toPoint / d;
	//Harmonic blend of the three axis distances facing dir, so the nearest blocker dominates
	float3 axisDistance = max(dir >= 0 ? distPos : distNeg, 0.0001f);
	float free = 1.0f / max(dot(dir * dir, 1.0f / axisDistance), 0.000001f);
	if (d <= free) return 1;
	float ratio = free / d;
	ratio *= ratio;
	return ratio * ratio;
}

//Down-weights probes behind the surface (DDGI's smooth backface term). Same as ProbeVolumeData.BackfaceWeight.
float ProbeBackfaceWeight(float3 toPoint, float3 normalWS)
{
	float d = length(toPoint);
	if (d < 0.0001f) return 1;
	float facing = (dot(-toPoint / d, normalWS) + 1) * 0.5f;
	return facing * facing + 0.2f;
}

//One volume: rgb = baked irradiance for a world-space normal, a = blend weight (fades to 0 at the volume's border).
//Blends the 8 surrounding probes by hand so probes that can't see the point (behind a wall, ceiling
//or floor thinner than the probe spacing) are left out instead of leaking their light through it.
float4 SampleOneProbeVolume(int v, float3 positionWS, float3 normalWS)
{
	float3 volumeMin = ProbeVolumeMins[v].xyz;
	float3 extentRcp = ProbeVolumeExtentRcps[v].xyz;
	float4 cellCounts = ProbeVolumeCellCounts[v];
	float3 uvw = (positionWS - volumeMin) * extentRcp;

	float3 cells = max(cellCounts.xyz, 1);
	float3 cellSize = 1.0f / (extentRcp * cells);
	float borderWeight;
	[branch]
	if (ProbeVolumeMins[v].w > 0)
	{
		//Detail volume: fade in over its margin (metres inside the box)
		float3 inside = min(uvw, 1 - uvw) / extentRcp;
		borderWeight = saturate(min(inside.x, min(inside.y, inside.z)) / ProbeVolumeMins[v].w);
	}
	else
	{
		//Main volume: distance to the nearest face in probe cells; full weight half a cell inside
		float3 edge = min(uvw, 1 - uvw) * cellCounts.xyz;
		borderWeight = saturate(min(edge.x, min(edge.y, edge.z)) * 2);
	}
	if (borderWeight <= 0) return float4(0, 0, 0, 0);

	float3 grid = saturate(uvw) * cells;
	float3 base = min(floor(grid), cells - 1);
	float3 f = grid - base;

	float4 r = 0, g = 0, b = 0;
	float4 plainR = 0, plainG = 0, plainB = 0;
	float total = 0;

	[unroll]
	for (int i = 0; i < 8; i++)
	{
		float3 corner = float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
		float3 t = lerp(1 - f, f, corner);
		float trilinear = t.x * t.y * t.z;
		int4 index = int4(base + corner, 0);

		float3 toPoint = positionWS - (volumeMin + (base + corner) * cellSize);
		float w = trilinear
			* ProbeVisibility(toPoint, VolumeTexel(index, 3, cellCounts).xyz, VolumeTexel(index, 4, cellCounts).xyz)
			* ProbeBackfaceWeight(toPoint, normalWS);

		float4 sr = VolumeTexel(index, 0, cellCounts);
		float4 sg = VolumeTexel(index, 1, cellCounts);
		float4 sb = VolumeTexel(index, 2, cellCounts);
		r += sr * w; g += sg * w; b += sb * w;
		total += w;
		plainR += sr * trilinear; plainG += sg * trilinear; plainB += sb * trilinear;
	}

	//Every neighbour blocked (e.g. a point inside geometry): fall back to plain trilinear
	if (total > 0.00001f) { r /= total; g /= total; b /= total; }
	else { r = plainR; g = plainG; b = plainB; }

	float3 irradiance = float3(r.x + dot(r.yzw, normalWS),
	                           g.x + dot(g.yzw, normalWS),
	                           b.x + dot(b.yzw, normalWS));
	return float4(max(irradiance, 0), borderWeight);
}

//rgb = baked irradiance, a = how much of the point the volumes cover (0 = outside all of them)
float4 SampleProbeVolume(float3 positionWS, float3 normalWS)
{
	float3 irradiance = 0;
	float remaining = 1;
	[loop]
	for (int v = 0; v < ProbeVolumeCount && remaining > 0.001f; v++)
	{
		float4 sample = SampleOneProbeVolume(v, positionWS, normalWS);
		irradiance += sample.rgb * sample.a * remaining;
		remaining *= 1 - sample.a;
	}
	float coverage = 1 - remaining;
	return float4(coverage > 0.0001f ? irradiance / coverage : 0, coverage);
}
