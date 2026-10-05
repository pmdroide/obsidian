//Procedural clouds for the day/night sky, included by Deferred/DeferredEnvironmentMap.fx.
//One cloud layer: domain-warped value-noise fbm projected onto a flat plane above the camera,
//lit by the sun or moon with cheap self-shadowing and a silver lining toward the light.

float CloudCoverage = 0.45f; //0 = clear sky, 1 = overcast
float2 CloudOffset;          //wind drift in noise space; EnvironmentSky wraps it to CLOUD_PERIOD

//The noise lattice repeats every CLOUD_PERIOD cells, so wrapping CloudOffset on the CPU is seamless
#define CLOUD_PERIOD 256.0f

float CloudHash(float2 cell)
{
	cell -= floor(cell / CLOUD_PERIOD) * CLOUD_PERIOD;
	float3 p3 = frac(cell.xyx * 0.1031f);
	p3 += dot(p3, p3.yzx + 33.33f);
	return frac((p3.x + p3.y) * p3.z);
}

float CloudNoise(float2 p)
{
	float2 i = floor(p);
	float2 f = frac(p);
	float2 u = f * f * (3 - 2 * f);
	float a = CloudHash(i);
	float b = CloudHash(i + float2(1, 0));
	float c = CloudHash(i + float2(0, 1));
	float d = CloudHash(i + float2(1, 1));
	return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

//Large shapes drift with the wind; finer octaves drift twice as fast so the edges keep changing.
//Every octave scales the offset by a whole multiple of the period, which keeps the wrap seamless.
float CloudFbm(float2 p, int octaves)
{
	float2 warp = float2(CloudNoise(p + CloudOffset + 3.1f), CloudNoise(p + CloudOffset + 8.7f)) - 0.5f;
	p += warp * 0.8f;

	float sum = 0;
	float amplitude = 0.5f;
	float frequency = 1;
	float total = 0;
	for (int i = 0; i < octaves; i++)
	{
		float2 drift = CloudOffset * (i < 2 ? 1 : 2);
		sum += amplitude * CloudNoise((p + drift) * frequency + i * 17.31f);
		total += amplitude;
		frequency *= 2;
		amplitude *= 0.5f;
	}
	return sum / total;
}

//fbm sits mostly in 0.25..0.75; the curve keeps mid coverage scattered and reaches full overcast at 1
float CloudThreshold()
{
	return lerp(0.72f, 0.05f, pow(CloudCoverage, 1.4f));
}

//Blends the cloud layer over an already shaded sky. Directions are world space, Z up.
float3 ApplyClouds(float3 sky, float3 viewDir, float3 sunDir, float daylight)
{
	if (CloudCoverage <= 0.001f || viewDir.z <= 0.01f) return sky;

	//Flat layer projection; stretched noise near the horizon is faded out
	float2 p = viewDir.xy / (viewDir.z + 0.08f) * 1.2f;
	float threshold = CloudThreshold();
	float noise = CloudFbm(p, 5);
	float density = saturate((noise - threshold) / 0.18f);
	float alpha = density * smoothstep(0.02f, 0.25f, viewDir.z);
	if (alpha <= 0.001f) return sky;

	//Light from whichever body is above the horizon (matches EnvironmentSky's sun/moon light)
	float3 moonDir = normalize(float3(-sunDir.x, sunDir.y, -sunDir.z));
	float3 lightDir = sunDir.z >= 0 ? sunDir : moonDir;

	//Self shadow: more cloud between this point and the light darkens it, thick cores are darker.
	//Thickness uses a wider range than coverage so an overcast sky still has structure.
	float2 towardLight = lightDir.xy / max(length(lightDir.xy), 0.001f) * 0.35f;
	float thickness = saturate((noise - threshold) / 0.6f);
	float occlusion = saturate((CloudFbm(p + towardLight, 3) - threshold) / 0.6f);
	float shade = (1 - 0.6f * occlusion) * lerp(1, 0.6f, thickness);

	//Day: white at noon, warm toward sunrise/sunset. Night: dark blue-grey, faintly moonlit.
	float3 sunTint = lerp(float3(1.0f, 0.45f, 0.25f), float3(1.0f, 0.97f, 0.92f), smoothstep(0.0f, 0.4f, sunDir.z));
	float3 dayColor = float3(0.38f, 0.42f, 0.5f) + sunTint * shade * 0.65f;
	float moonUp = saturate(moonDir.z * 4);
	float3 nightColor = float3(0.02f, 0.025f, 0.04f) + float3(0.05f, 0.06f, 0.09f) * shade * moonUp;
	float3 cloud = lerp(nightColor, dayColor, daylight);

	//Silver lining: thin edges light up when looking toward the sun (or, weakly, the moon)
	float3 lining = pow(saturate(dot(viewDir, sunDir)), 8) * daylight * sunTint
	              + pow(saturate(dot(viewDir, moonDir)), 8) * (1 - daylight) * 0.1f * float3(0.6f, 0.7f, 1.0f);
	cloud += lining * (1 - density) * 0.8f;

	return lerp(sky, cloud, alpha);
}
