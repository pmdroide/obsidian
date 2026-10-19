using System;
// ReSharper disable InconsistentNaming

namespace Engine.Recources
{
    public static class GameSettings
    {
        //Default & Display settings
        public static int g_screenwidth = 1280;
        public static int g_screenheight = 720;
        public static bool g_vsync = false;
        public static int g_fixedfps = 0;
        public static int u_showdisplayinfo = 3; //Debug stats (DebugScreen): 0 off, 1 FPS only, 2+ full. Anvil's viewport Stats button toggles 0/3.
        public static bool p_physics = true; //Master switch: simulate physics bodies outside editor mode (Play)
        public static Renderer.Renderer.RenderModes g_rendermode = Renderer.Renderer.RenderModes.Deferred;

        //Editor
        public static bool e_enableeditor = true;
        public static bool e_drawoutlines = true;
        public static bool e_drawboundingbox = true;
        //Ground grid (GridRenderModule): edit mode only. Anvil's viewport Grid button toggles e_showgrid.
        public static bool e_showgrid = true;
        public static float e_gridheight = 0; //world Z of the ground plane
        public static float e_gridcellsize = 1; //metres; major lines every 10 cells
        public static float e_gridfadedistance = 250; //metres

        //UI
        public static bool ui_enabled = true;

        //Renderer

        //debug
        public static bool d_drawlines = true;

        //Default Material
        public static bool d_defaultmaterial = false;
        public static float m_defaultroughness = 0.5f;
        
        //Settings
        public static float g_farplane = 1000;
        public static bool g_cpusort = true;
        public static bool g_cpuculling = true;
        //Screen-size culling (Source 2 look, phase 7): radius / distance below which meshes are skipped in the view and
        //in shadow maps (r_size_cull_threshold / _shadow); 0 = off. Occlusion culling with GPU queries (last frame's results).
        public static float g_sizecullthreshold = 0.0015f;
        public static float g_sizecullthresholdshadow = 0.003f;
        public static bool g_occlusionculling = false;
        public static bool g_batchbymaterial = false; //Note this must be activated before the application is started.

        //Profiler
        public static bool d_profiler = false;

        //Environment mapping
        public static bool g_environmentmapping = true;
        public static bool g_envmapupdateeveryframe = false;
        public static int g_envmapresolution = 1024;
        //Image based lighting (Source 2 look, phase 3): texels per face of the GGX-prefiltered global probe, and the
        //cubemap normalisation (vr_cubemap_normalization_max / _roughness_begin, HLA defaults 32 / 0.1, Source2Look R4):
        //reflections are scaled by the baked light at the pixel over that at the capture point; brightening is capped
        //at 1 up to RoughnessBegin and rises linearly to NormalizationMax at roughness 1, darkening is never limited
        public static int g_reflectionprobesize = 256;
        public static float g_cubemapnormalizationmax = 32;
        public static float g_cubemapnormalizationroughnessbegin = 0.1f;

        //Shadow Settings
        public static int g_shadowforcefiltering = 2; //1 = PCF, 2 3 better PCF  4 = Poisson, 5 = VSM;

        //Point / spot light shadow atlas (Source 2 look, phase 2.2): atlas size, lights redrawn per frame (time
        //slicing), lights whose sphere covers less than this angle (radius / distance) get no shadows, and shadows fade
        //out between these distances (m) from the camera to the light's sphere (sc_override_shadow_fade_min/max_dist)
        public static int g_shadowatlassize = 4096;
        public static int g_shadowupdatesperframe = 8;
        public static float g_shadowsizecull = 0.02f;
        public static float g_shadowfadestart = 60;
        public static float g_shadowfadeend = 80;

        //Deferred Decals
        public static bool g_drawdecals = true;

        //Forward pass
        public static bool g_forwardenable = true;

        //Temporal AntiAliasing
        public static bool g_taa = true;
        public static int g_taa_jittermode = 2;
        public static bool g_taa_tonemapped = true;
        //Source 2 look (phase 1.6): G-buffer motion vectors for moving objects, contrast adaptive sharpening of the
        //resolved image (0 = off), and FXAA on the final image while TAA is off (a still, MSAA-like image)
        public static bool g_taa_velocity = true;
        public static float g_taa_sharpness = 0.35f;
        public static bool g_fxaa = true;

        //Screen Space Ambient Occlusion

        public static bool g_ssao_blur = true;

        private static bool _g_ssao_draw = true;
        public static bool g_ssao_draw
        {
            get { return _g_ssao_draw; }
            set
            {
                _g_ssao_draw = value;
                //SSAO darkens the indirect light in the environment pass (Renderer reads this every frame)
                Shaders.DeferredCompose.Parameters["useSSAO"]?.SetValue(_g_ssao_draw);
            }
        }
        
        private static float _g_ssao_falloffmin = 0.001f;
        public static float g_ssao_falloffmin
        {
            get { return _g_ssao_falloffmin; }
            set
            {
                _g_ssao_falloffmin = value;
                Shaders.ScreenSpaceEffect_FalloffMin.SetValue(value);
            }
        }
        
        private static float _g_ssao_falloffmax = 0.03f;
        public static float g_ssao_falloffmax
        {
            get { return _g_ssao_falloffmax; }
            set
            {
                _g_ssao_falloffmax = value;
                Shaders.ScreenSpaceEffect_FalloffMax.SetValue(value);
            }
        }
        
        private static int _g_ssao_samples = 8;
        public static int g_ssao_samples
        {
            get { return _g_ssao_samples; }
            set
            {
                _g_ssao_samples = value;
                Shaders.ScreenSpaceEffect_Samples.SetValue(value);
            }
        }

        private static float _g_ssao_radius = 30;
        public static float g_ssao_radius
        {
            get { return _g_ssao_radius; }
            set
            {
                _g_ssao_radius = value;
                Shaders.ScreenSpaceEffect_SampleRadius.SetValue(value);
            }
        }

        private static float _g_ssao_strength = 0.5f;
        public static float g_ssao_strength
        {
            get { return _g_ssao_strength; }
            set
            {
                _g_ssao_strength = value;
                Shaders.ScreenSpaceEffect_Strength.SetValue(value);
            }
        }



        //Bloom (Source 2 look): exposed brightness where bloom starts, its knee / ramp width and the amount added.
        //Per scene in EnvironmentSettings.Post, copied here every frame by Renderer.ApplyScenePost.
        public static Logic.BloomThresholdShape g_BloomShape = Logic.BloomThresholdShape.Source2;
        public static float g_BloomStart = 1.976f;
        public static float g_BloomKnee = 2.3655f;
        public static float g_BloomScale = 0.35f;

        //Tonemapping / grading (EnvironmentSettings.Post, copied every frame by Renderer.ApplyScenePost)
        public static Logic.TonemapCurve g_TonemapCurve = Logic.TonemapCurve.Source2;
        //Source 2 filmic curve (TonemapParameters_t): shoulder, linear strength, linear angle, toe strength ...
        public static Microsoft.Xna.Framework.Vector4 g_FilmicCurve = new Microsoft.Xna.Framework.Vector4(0.15f, 0.5f, 0.1f, 0.2f);
        //... toe numerator, toe denominator, white point (stops)
        public static Microsoft.Xna.Framework.Vector3 g_FilmicToe = new Microsoft.Xna.Framework.Vector3(0.02f, 0.3f, 4);
        public static bool g_ExactSrgb = true;
        public static float g_Saturation = 1;
        public static float g_Contrast = 1;
        public static Microsoft.Xna.Framework.Vector3 g_ColorFilter = Microsoft.Xna.Framework.Vector3.One; //linear
        public static float g_Vignette = 1;
        public static string g_LutPath;
        //A second LUT faded in (Post Process Volumes between two LUTs); null = none
        public static string g_LutBlendPath;
        public static float g_LutBlend;
        public static float g_RetinaBurn = 0;
        public static float g_FogDensityScale = 1;

        // Emissive 

        //public static bool g_EmissiveDraw = true;
        //public static bool g_EmissiveDrawDiffuse = true;
        //public static bool g_EmissiveDrawSpecular = true;
        //public static bool g_EmissiveNoise = false;
        //public static float g_EmissiveDrawFOVFactor = 2;

        ////Whether or not materials' lighting scales with strength
        //public static bool g_EmissiveMaterialeSizeStrengthScaling = true;

        //private static int _g_EmissiveMaterialSamples = 8;
        //public static int g_EmissiveMaterialSamples
        //{
        //    get { return _g_EmissiveMaterialSamples; }
        //    set
        //    {
        //        _g_EmissiveMaterialSamples = value;
        //        Shaders.EmissiveEffect.Parameters["Samples"].SetValue(_g_EmissiveMaterialSamples);
        //    }
        //}




        // PostProcessing

        private static float _chromaticAbberationStrength = 0f; //Off by default: even 0.01 leaves a red line on bright/dark edges
        public static float ChromaticAbberationStrength
        {
            get { return _chromaticAbberationStrength; }
            set
            {
                _chromaticAbberationStrength = value;
                Shaders.PostProcessingParameter_ChromaticAbberationStrength.SetValue(_chromaticAbberationStrength);

                //The vignette lives in the same technique; it stays on when the fringe is turned off
                Shaders.PostProcessing.CurrentTechnique = Shaders.PostProcessingTechnique_VignetteChroma;
            }
        }

        private static float _sCurveStrength = 0.05f;
        public static float SCurveStrength
        {
            get { return _sCurveStrength; }
            set
            {
                _sCurveStrength = value;
                Shaders.PostProcessingParameter_SCurveStrength.SetValue(_sCurveStrength);
            }
        }

        private static float _whitePoint = 1.1f;
        public static float WhitePoint
        {
            get { return _whitePoint; }
            set
            {
                _whitePoint = value;
                Shaders.PostProcessingParameter_WhitePoint.SetValue(_whitePoint);
            }
        }

        private static float _exposure = 0;
        public static float Exposure
        {
            get { return _exposure; }
            set
            {
                _exposure = value;
                Shaders.PostProcessingParameter_PowExposure.SetValue((float) Math.Pow(2,_exposure));
            }
        }

        public static bool g_ColorGrading = true;

        // Eye adaptation / auto exposure. The adapted EV is added on top of Exposure (and the day/night offset).
        public static bool g_AutoExposure = true;
        public static float g_AutoExposureKey = 0.18f; //The average scene luminance is exposed to this value
        public static float g_AutoExposureMin = -2f; //EV clamp (HLA 0.25 .. 8)
        public static float g_AutoExposureMax = 3f;
        public static float g_AutoExposureSpeedDarkToLight = 1.2f; //Adaptation per second when the scene gets brighter
        public static float g_AutoExposureSpeedLightToDark = 0.3f; //... and when it gets darker (HLA rate 0.3, x4 down)
        public static float g_AutoExposureCenterWeight = 0f; //0 = whole frame (HLA), 1 = favour the screen center
        //Source's tonemap controller: expose the brightest g_BrightPixelPercent % of the frame to g_BrightPixelTarget
        public static Logic.ExposureMetering g_ExposureMetering = Logic.ExposureMetering.Source2;
        public static float g_BrightPixelPercent = 2f;
        public static float g_BrightPixelTarget = 0.18f;
        public static float g_MinAverageLuminance = 0.03f;

        // SSR

        private static bool _g_SSReflection = true;

        public static bool g_SSReflection
        {
            get { return _g_SSReflection;}
            set
            {
                _g_SSReflection = value;
            }
        }

        private static bool _g_SSReflection_FireflyReduction = true;

        public static bool g_SSReflection_FireflyReduction
        {
            get { return _g_SSReflection_FireflyReduction; }
            set
            {
                _g_SSReflection_FireflyReduction = value;
            }
        }

        private static float _g_SSReflection_FireflyThreshold = 1.75f;

        public static float g_SSReflection_FireflyThreshold
        {
            get { return _g_SSReflection_FireflyThreshold; }
            set
            {
                _g_SSReflection_FireflyThreshold = value;
            }
        }


        private static bool _g_Linear = true;
        public static bool g_Linear
        {
            get { return _g_Linear; }
            set
            {
                _g_Linear = value;
                Shaders.DeferredCompose.CurrentTechnique = value
                    ? Shaders.DeferredComposeTechnique_Linear
                    : Shaders.DeferredComposeTechnique_NonLinear;
            }
        }

        private static bool _g_SSReflection_Taa = true;
        public static bool g_SSReflectionNoise = true;
        public static bool g_VolumetricLights = true;
        
        // Froxel (Clustered Volumetric Fog) settings
        public static bool g_FroxelsEnabled = true;
        public static bool g_FroxelFogEnabled = true;
        private static float _g_froxelDensity = 0.03f; // 0.001f is good for directional light only, 0.03f is good for multiple lights, 0.1f is very dense and expensive
        public static float g_FroxelDensity
        {
            get { return _g_froxelDensity; }
            set
            {
                _g_froxelDensity = Math.Clamp(value, 0.001f, 5.0f);
            }
        }

        // Split scatter: directional sun rays often want a much lower value than point-light volumetrics
        // (a bright directional with HG forward peak quickly blows out, while point lights need
        // higher scatter to be visible at all due to 1/r^2 attenuation).
        private static float _g_froxelDirectionalScatter = 0.1f;
        public static float g_FroxelDirectionalScatter
        {
            get { return _g_froxelDirectionalScatter; }
            set { _g_froxelDirectionalScatter = Math.Clamp(value, 0.0f, 4.0f); }
        }

        private static float _g_froxelPointScatter = 1.5f;
        public static float g_FroxelPointScatter
        {
            get { return _g_froxelPointScatter; }
            set { _g_froxelPointScatter = Math.Clamp(value, 0.0f, 16.0f); }
        }

        // Legacy convenience: sets BOTH scatter values together. Use the split props for fine control.
        public static float g_FroxelScatter
        {
            get { return _g_froxelDirectionalScatter; }
            set
            {
                _g_froxelDirectionalScatter = Math.Clamp(value, 0.0f, 4.0f);
                _g_froxelPointScatter = Math.Clamp(value, 0.0f, 16.0f);
            }
        }

        // Baked bounce light (probe volume) scattered by the fog, like the two above; 0 without baked probes.
        private static float _g_froxelIndirectScatter = 0.1f;
        public static float g_FroxelIndirectScatter
        {
            get { return _g_froxelIndirectScatter; }
            set { _g_froxelIndirectScatter = Math.Clamp(value, 0.0f, 4.0f); }
        }

        private static float _g_froxelAbsorption = 0.1f;
        public static float g_FroxelAbsorption
        {
            get { return _g_froxelAbsorption; }
            set
            {
                _g_froxelAbsorption = Math.Clamp(value, 0.0f, 1.0f);
            }
        }

        // 0 = sky completely unaffected by fog (skybox stays crisp). 1 = sky fully attenuated.
        // The defaults above are tuned so the sky reads through; raise this for moodier distance haze.
        private static float _g_froxelSkyFogStrength = 0.1f;
        public static float g_FroxelSkyFogStrength
        {
            get { return _g_froxelSkyFogStrength; }
            set { _g_froxelSkyFogStrength = Math.Clamp(value, 0.0f, 1.0f); }
        }

        // Fog in front of sky pixels up to this distance is kept at full strength (only the haze past
        // it uses SkyFogStrength), so shafts seen against the sky - looking towards the sun - stay visible.
        private static float _g_froxelSkyShaftDistance = 200f;
        public static float g_FroxelSkyShaftDistance
        {
            get { return _g_froxelSkyShaftDistance; }
            set { _g_froxelSkyShaftDistance = Math.Max(0f, value); }
        }

        // Distance ramp: fog density scales from 0 at FogDistanceStart to full at FogDistanceFull.
        // Lets near geometry stay crisp while distant objects fade into the haze.
        private static float _g_froxelFogDistanceStart = 30f;
        public static float g_FroxelFogDistanceStart
        {
            get { return _g_froxelFogDistanceStart; }
            set { _g_froxelFogDistanceStart = Math.Max(0f, value); }
        }

        // Fraction of the density kept before FogDistanceStart (0 = clear near the camera, 1 = no ramp).
        private static float _g_froxelNearDensity = 0f;
        public static float g_FroxelNearDensity
        {
            get { return _g_froxelNearDensity; }
            set { _g_froxelNearDensity = Math.Clamp(value, 0f, 1f); }
        }

        private static float _g_froxelFogDistanceFull = 400f;
        public static float g_FroxelFogDistanceFull
        {
            get { return _g_froxelFogDistanceFull; }
            set { _g_froxelFogDistanceFull = Math.Max(_g_froxelFogDistanceStart + 1f, value); }
        }

        // Henyey-Greenstein anisotropy. 0 = isotropic, > 0 = forward scattering (glow around the
        // sun when looking towards it), < 0 = back scattering.
        private static float _g_froxelAnisotropy = 0.45f;
        public static float g_FroxelAnisotropy
        {
            get { return _g_froxelAnisotropy; }
            set { _g_froxelAnisotropy = Math.Clamp(value, -0.95f, 0.95f); }
        }

        // Temporal history weight. Higher = smoother but laggier (and blocks visible when still).
        // Lower = more responsive but noisier. 0 disables temporal blending.
        private static float _g_froxelHistoryBlend = 0.7f;
        public static float g_FroxelHistoryBlend
        {
            get { return _g_froxelHistoryBlend; }
            set { _g_froxelHistoryBlend = Math.Clamp(value, 0.0f, 0.98f); }
        }
        
        public static bool e_CPURayMarch = false;
        public static bool g_ClearGBuffer = true;
        public static bool g_PostProcessing = true;

        public static bool g_SSReflectionTaa
        {
            get { return _g_SSReflection_Taa;}
            set
            {
                _g_SSReflection_Taa = value;
                Shaders.ScreenSpaceReflectionEffect.CurrentTechnique = value
                    ? Shaders.ScreenSpaceReflectionTechnique_Taa
                    : Shaders.ScreenSpaceReflectionTechnique_Default;

                if (value) g_SSReflectionNoise = true;
            }
        }

        // Screen Space Ambient Occlusion
        

        //5 and 5 are good, 3 and 3 are cheap
        private static int msamples = 3;
        public static int g_SSReflections_Samples
        {
            get { return msamples; }
            set
            {
                msamples = value;
                Shaders.ScreenSpaceReflectionEffect.Parameters["Samples"].SetValue(msamples);
            }
        }

        private static int ssamples = 3;
        public static int g_SSReflections_RefinementSamples
        {
            get { return ssamples; }
            set
            {
                ssamples = value;
                Shaders.ScreenSpaceReflectionEffect.Parameters["SecondarySamples"].SetValue(ssamples);
            }
        }

        private static float minThickness = 70;
        public static float g_SSReflections_MinThickness
        {
            get { return minThickness; }
            set
            {
                minThickness = value;
                Shaders.ScreenSpaceReflectionEffect.Parameters["MinimumThickness"].SetValue(minThickness);
            }
        }

        //private static float _g_TemporalAntiAliasingThreshold = 0.9f;
        public static int g_UseDepthStencilLightCulling = 1; //None, Depth, Depth+Stencil
        public static bool g_BloomEnable = true;

        public static float g_BloomRadius1 = 1.0f;
        public static float g_BloomRadius2 = 1.0f;
        public static float g_BloomRadius3 = 2.0f;
        public static float g_BloomRadius4 = 3.0f;
        public static float g_BloomRadius5 = 4.0f;

        //Each upsample blends a mip over the next one up, so these mainly set how wide the glow spreads.
        //At 1.0 the widest mips dominate and, with a threshold of 0, the whole frame is smeared and loses contrast.
        public static float g_BloomStrength1 = 0.25f;
        public static float g_BloomStrength2 = 0.5f;
        public static float g_BloomStrength3 = 0.5f;
        public static float g_BloomStrength4 = 0.5f;
        public static float g_BloomStrength5 = 0.5f;

        
        public static float ShadowBias = 0.005f;
        public static int sdf_threads = 4;
        public static bool sdf_cpu = false;
        public static bool sdf_draw = true;
        public static bool sdf_drawdistance = false;
        public static bool sdf_debug = false;
        public static bool sdf_subsurface = true;
        public static bool sdf_drawvolume = false;
        public static bool sdf_regenerate;
        public static bool d_drawnothing = false;
        public static bool e_saveBoundingBoxes = true;
        public static bool d_hotreloadshaders = true;

        public static void ApplySettings()
        {
            ApplySSAO();
            
            g_ssao_draw = true;
            g_PostProcessing = true;
            g_taa = true;
            g_environmentmapping = true;

            g_SSReflection = true;
            g_SSReflections_Samples = msamples;
            g_SSReflections_RefinementSamples = ssamples;
            g_SSReflection_FireflyReduction = _g_SSReflection_FireflyReduction;
            g_SSReflection_FireflyThreshold = _g_SSReflection_FireflyThreshold;

            g_Linear = _g_Linear;

            d_defaultmaterial = false;
            SCurveStrength = _sCurveStrength;
            Exposure = _exposure;
            ChromaticAbberationStrength = _chromaticAbberationStrength;
            
        }

        public static void ApplySSAO()
        {
            g_ssao_falloffmax = _g_ssao_falloffmax;
            g_ssao_falloffmin = _g_ssao_falloffmin;
            g_ssao_radius = _g_ssao_radius;
            g_ssao_samples = g_ssao_samples;
            g_ssao_strength = g_ssao_strength;
            g_ssao_draw = _g_ssao_draw;
        }
    }
}
