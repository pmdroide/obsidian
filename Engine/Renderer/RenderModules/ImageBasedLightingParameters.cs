using System;
using System.Collections.Generic;
using Engine.Recources;
using Engine.Renderer.Lighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.RenderModules
{
    /// <summary>
    /// Sets the parameters of Shaders/Common/ProbeVolume.fx and Reflections.fx on an effect (the environment pass and the
    /// forward pass share them, Source 2 look phases 3.2-3.4). Missing parameters are skipped.
    /// </summary>
    public static class ImageBasedLightingParameters
    {
        private static readonly Matrix[] WorldToBox = new Matrix[ReflectionProbes.MaxPerView];
        private static readonly Matrix[] BoxToWorld = new Matrix[ReflectionProbes.MaxPerView];
        private static readonly Vector4[] Settings = new Vector4[ReflectionProbes.MaxPerView];
        private static readonly Vector4[] Extent = new Vector4[ReflectionProbes.MaxPerView];
        private static readonly Vector4[] CaptureBox = new Vector4[ReflectionProbes.MaxPerView];
        private static readonly Vector4[] SHR = new Vector4[ReflectionProbes.MaxPerView];
        private static readonly Vector4[] SHG = new Vector4[ReflectionProbes.MaxPerView];
        private static readonly Vector4[] SHB = new Vector4[ReflectionProbes.MaxPerView];
        private static readonly Vector4[] VolumeMins = new Vector4[ProbeVolumeData.MaxVolumes];
        private static readonly Vector4[] VolumeExtentRcps = new Vector4[ProbeVolumeData.MaxVolumes];
        private static readonly Vector4[] VolumeCells = new Vector4[ProbeVolumeData.MaxVolumes];

        /// <summary>The baked probe volume (null disables it) and its irradiance at the global probe's capture point.</summary>
        public static void ApplyProbeVolume(EffectParameterCollection p, LightingSystem lighting, float intensity, Vector3 capturePosition)
        {
            ProbeVolumeData data = lighting?.GpuData;
            if (data == null || lighting.ProbeData == null)
            {
                p["UseProbeVolume"]?.SetValue(false);
                return;
            }
            Vector3 extent = data.BoundsMax - data.BoundsMin;
            p["UseProbeVolume"]?.SetValue(true);
            p["ProbeVolumeIntensity"]?.SetValue(intensity);
            p["ProbeVolumeMin"]?.SetValue(data.BoundsMin);
            p["ProbeVolumeExtentRcp"]?.SetValue(new Vector3(1f / extent.X, 1f / extent.Y, 1f / extent.Z));
            p["ProbeVolumeCells"]?.SetValue(new Vector3(data.CountX - 1, data.CountY - 1, data.CountZ - 1));
            p["ProbeData"]?.SetValue(lighting.ProbeData);

            //Every volume, highest priority first (Source 2 look, phase 4.1)
            IReadOnlyList<(ProbeVolumeData Volume, int ZOffset)> volumes = lighting.PackedVolumes;
            int count = Math.Min(volumes.Count, ProbeVolumeData.MaxVolumes);
            for (int i = 0; i < count; i++)
            {
                ProbeVolumeData v = volumes[i].Volume;
                Vector3 e = v.BoundsMax - v.BoundsMin;
                bool main = ReferenceEquals(v, data);
                VolumeMins[i] = new Vector4(v.BoundsMin, main ? 0 : Math.Max(v.BlendMargin, 0.001f));
                VolumeExtentRcps[i] = new Vector4(1f / e.X, 1f / e.Y, 1f / e.Z, 0);
                VolumeCells[i] = new Vector4(v.CountX - 1, v.CountY - 1, v.CountZ - 1, volumes[i].ZOffset);
            }
            p["ProbeVolumeCount"]?.SetValue(count);
            p["ProbeVolumeMins"]?.SetValue(VolumeMins);
            p["ProbeVolumeExtentRcps"]?.SetValue(VolumeExtentRcps);
            p["ProbeVolumeCellCounts"]?.SetValue(VolumeCells);

            data.VolumeAt(capturePosition).SampleSH(capturePosition, out Vector4 r, out Vector4 g, out Vector4 b);
            p["CaptureSHR"]?.SetValue(r);
            p["CaptureSHG"]?.SetValue(g);
            p["CaptureSHB"]?.SetValue(b);
        }

        /// <summary>The GGX-prefiltered global probe and the split-sum BRDF lookup.</summary>
        public static void ApplyGlobal(EffectParameterCollection p, TextureCube prefiltered, Texture2D brdfLut)
        {
            p["ReflectionCubeMap"]?.SetValue(prefiltered);
            if (prefiltered != null) p["GlobalMaxMip"]?.SetValue((float)Math.Max(prefiltered.LevelCount - 1, 0));
            p["BrdfLut"]?.SetValue(brdfLut);
            p["UseBrdfLut"]?.SetValue(brdfLut != null);
        }

        /// <summary>
        /// The local Reflection Probes to blend (highest priority first). With <paramref name="bake"/> each is normalised
        /// by the baked irradiance at its capture point.
        /// </summary>
        public static void ApplyLocalProbes(EffectParameterCollection p, IReadOnlyList<ReflectionProbes.Probe> probes, ProbeVolumeData bake)
        {
            int count = Math.Min(probes?.Count ?? 0, ReflectionProbes.MaxPerView);
            for (int i = 0; i < count; i++)
            {
                ReflectionProbes.Probe probe = probes[i];
                Matrix box = probe.CapturedBox;
                BoxToWorld[i] = box;
                WorldToBox[i] = Matrix.Invert(box);
                Vector3 scale = probe.Owner.Scale;
                Extent[i] = new Vector4(Math.Abs(scale.X), Math.Abs(scale.Y), Math.Abs(scale.Z), 0);
                CaptureBox[i] = Vector4.Zero;
                bool normalized = bake != null;
                if (normalized)
                {
                    bake.VolumeAt(box.Translation).SampleSH(box.Translation, out Vector4 r, out Vector4 g, out Vector4 b);
                    SHR[i] = r; SHG[i] = g; SHB[i] = b;
                }
                Settings[i] = new Vector4(probe.Component.ClampedBlendDistance, probe.Component.BoxProjection ? 1 : 0,
                    Math.Max(probe.Cube.LevelCount - 1, 0), normalized ? 1 : 0);
                p["ProbeCube" + i]?.SetValue(probe.Cube);
            }
            p["ProbeCount"]?.SetValue(count);
            p["ProbeWorldToBox"]?.SetValue(WorldToBox);
            p["ProbeBoxToWorld"]?.SetValue(BoxToWorld);
            p["ProbeSettings"]?.SetValue(Settings);
            p["ProbeExtent"]?.SetValue(Extent);
            p["ProbeCaptureBox"]?.SetValue(CaptureBox);
            p["ProbeCaptureSHR"]?.SetValue(SHR);
            p["ProbeCaptureSHG"]?.SetValue(SHG);
            p["ProbeCaptureSHB"]?.SetValue(SHB);
            p["NormalizationMax"]?.SetValue(Math.Max(GameSettings.g_cubemapnormalizationmax, 0));
            p["NormalizationRoughnessBegin"]?.SetValue(Math.Clamp(GameSettings.g_cubemapnormalizationroughnessbegin, 0, 1));
        }
    }
}
