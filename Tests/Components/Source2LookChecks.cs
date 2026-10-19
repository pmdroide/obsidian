using System.Collections.ObjectModel;
using System.Reflection;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Lighting;
using Engine.Renderer.RenderModules;
using Microsoft.Xna.Framework;

/// <summary>
/// The Source 2 look work (Docs/Research/Source2Look): scene post processing and Post Process Volumes, the shadow atlas,
/// cascade splits, the light falloff, the BRDF LUT, detail probe volumes, the Reflection Probe / Probe Volume / AO Proxy
/// components, the new light settings and the LookDev scene. Everything here runs without a graphics device.
/// </summary>
internal static class Source2LookChecks
{
    public static void Run()
    {
        CheckPostSettings();
        CheckPostVolumes();
        CheckPostProfiles();
        CheckShadowAtlas();
        CheckCascadeSplits();
        CheckLightFalloff();
        CheckBrdfLut();
        CheckProbeVolumes();
        CheckComponents();
        ReflectionProbeChecks.Run();
        CheckLightSettings();
        CheckLookDevScene();
    }

    private static void CheckPostSettings()
    {
        var env = new EnvironmentSettings();
        env.Post.Exposure = 1.5f;
        env.Post.Tonemap = TonemapCurve.Aces;
        var clone = env.Clone();
        clone.Post.Exposure = 3;
        Check(env.Post.Exposure == 1.5f && clone.Post.Tonemap == TonemapCurve.Aces,
            "cloning the environment copies its post processing independently");

        var post = new PostProcessSettings { BloomScale = 5, Saturation = float.NaN };
        post.Normalize();
        Check(post.BloomScale == 1 && float.IsFinite(post.Saturation), "post settings are clamped and NaN-free after Normalize");

        string json = System.Text.Json.JsonSerializer.Serialize(env, SceneSerialization.JsonOptions);
        var back = System.Text.Json.JsonSerializer.Deserialize<EnvironmentSettings>(json, SceneSerialization.JsonOptions)!;
        Check(back.Post.Exposure == 1.5f && back.Post.Tonemap == TonemapCurve.Aces && json.Contains("\"Aces\""),
            "scene post processing saves with the environment (curves by name)");
        Check(!PostProcessSettings.Settings.Any(p => p.Name == nameof(PostProcessSettings.BlendLutPath)) ||
              !json.Contains(nameof(PostProcessSettings.BlendLutPath)),
            "the runtime LUT blend isn't saved");

        //Research R1-R5: the defaults are Half-Life: Alyx's values
        var hla = new PostProcessSettings();
        Check(hla.Tonemap == TonemapCurve.Source2 && hla.Exposure == 0 && hla.FilmicShoulder == 0.15f &&
              hla.FilmicLinearStrength == 0.5f && hla.FilmicLinearAngle == 0.1f && hla.FilmicToeStrength == 0.2f &&
              hla.FilmicToeNumerator == 0.02f && hla.FilmicToeDenominator == 0.3f && hla.FilmicWhitePoint == 4,
            "new scenes use Half-Life: Alyx's filmic curve with its neutral parameters");
        Check(hla.Metering == ExposureMetering.Source2 && hla.AutoExposureMin == -2 && hla.AutoExposureMax == 3 &&
              hla.AdaptationSpeedUp == 1.2f && hla.AdaptationSpeedDown == 0.3f && hla.CenterWeight == 0,
            "new scenes meter exposure like Half-Life: Alyx (median, 0.25 to 8, rate 0.3 / x4 down)");
        Check(hla.BloomShape == BloomThresholdShape.Source2 && hla.BloomStart == 1.976f && hla.BloomKnee == 2.3655f,
            "new scenes threshold bloom like Half-Life: Alyx's base profile");
        Check(GameSettings.g_cubemapnormalizationmax == 32 && GameSettings.g_cubemapnormalizationroughnessbegin == 0.1f &&
              new DirectionalLight(Color.White, 1, -Vector3.UnitZ).SplitLambda == 0.85f,
            "cubemap normalisation and cascade splits use Half-Life: Alyx's ConVar defaults");
        var broken = new PostProcessSettings { FilmicToeDenominator = 0, FilmicWhitePoint = float.NaN, Tonemap = (TonemapCurve)99 };
        broken.Normalize();
        Check(broken.FilmicToeDenominator > 0 && broken.FilmicWhitePoint == 4 && broken.Tonemap == TonemapCurve.Source2,
            "the Source 2 curve can't divide by zero after Normalize");
        string hlaJson = System.Text.Json.JsonSerializer.Serialize(hla, SceneSerialization.JsonOptions);
        var hlaBack = System.Text.Json.JsonSerializer.Deserialize<PostProcessSettings>(hlaJson, SceneSerialization.JsonOptions)!;
        Check(hlaJson.Contains("\"Source2\"") && hlaBack.BloomShape == BloomThresholdShape.Source2 &&
              hlaBack.Metering == ExposureMetering.Source2 && hlaBack.FilmicShoulder == 0.15f,
            "the Source 2 settings save by name and load back");
    }

    private static void CheckPostVolumes()
    {
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var scene = new PostProcessSettings { Exposure = 0.75f, Saturation = 1 };
        var box = new BasicEntity(assets.Cube, null, new Vector3(10, 0, 0), Matrix.Identity, new Vector3(2, 2, 2)) { IsEnabled = true };
        var volume = new PostProcessVolumeComponent { BlendDistance = 4 };
        volume.Profile.Exposure = 2;
        volume.Profile.Saturation = 0;
        volume.Overrides.Add(nameof(PostProcessSettings.Exposure));
        box.AddComponent(volume);
        var entities = new List<BasicEntity> { box };
        var result = new PostProcessSettings();

        PostProcessVolumes.Evaluate(scene, entities, new Vector3(10, 0, 0), result);
        Check(result.Exposure == 2 && result.Saturation == 1 && box.WorldTransform.Hidden,
            "inside a volume its overridden settings win, the rest stay the scene's, and the box mesh is hidden");
        PostProcessVolumes.Evaluate(scene, entities, new Vector3(14, 0, 0), result);
        Check(Math.Abs(result.Exposure - (0.75f + (2 - 0.75f) * 0.5f)) < 1e-4f, "half way through Blend Distance it is half blended");
        PostProcessVolumes.Evaluate(scene, entities, new Vector3(30, 0, 0), result);
        Check(result.Exposure == 0.75f, "far outside the volume the scene's settings apply");

        var top = new BasicEntity(assets.Cube, null, new Vector3(10, 0, 0), Matrix.Identity, Vector3.One) { IsEnabled = true };
        var topVolume = new PostProcessVolumeComponent { Infinite = true, Priority = 5, Weight = 1 };
        topVolume.Profile.Exposure = 4;
        topVolume.Overrides.Add("exposure");
        top.AddComponent(topVolume);
        entities.Insert(0, top);
        PostProcessVolumes.Evaluate(scene, entities, new Vector3(10, 0, 0), result);
        Check(result.Exposure == 4, "the higher priority volume blends last, whatever the list order; overrides match any case");
        PostProcessVolumes.Evaluate(scene, entities, new Vector3(500, 0, 0), result);
        Check(result.Exposure == 4, "an Infinite volume applies everywhere");
        topVolume.Weight = 0.5f;
        PostProcessVolumes.Evaluate(scene, entities, new Vector3(500, 0, 0), result);
        Check(Math.Abs(result.Exposure - (0.75f + 4) / 2) < 1e-4f, "Weight scales a volume's blend");
        topVolume.Enabled = false;
        PostProcessVolumes.Evaluate(scene, entities, new Vector3(500, 0, 0), result);
        Check(result.Exposure == 0.75f && !top.WorldTransform.Hidden, "a disabled volume does nothing and shows its mesh");

        var lut = new PostProcessSettings { LutPath = "A.png" };
        var other = new PostProcessSettings { LutPath = "B.png" };
        lut.BlendTowards(other, 0.25f, new[] { nameof(PostProcessSettings.LutPath) });
        Check(lut.LutPath == "A.png" && lut.BlendLutPath == "B.png" && Math.Abs(lut.LutBlend - 0.25f) < 1e-4f,
            "colour grading LUTs cross-fade between volumes instead of flipping");
    }

    private static void CheckPostProfiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "obsidian-post-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MaterialAsset.SourceRootOverride = root;
        try
        {
            Check(PostProfile.Normalize("Dark") == "PostProfiles/Dark.post" && PostProfile.Normalize("A\\B.post") == "A/B.post",
                "profile names resolve into PostProfiles with the .post extension");
            string rel = PostProfile.Write("Dark", new PostProcessSettings { Exposure = 0.2f, Tonemap = TonemapCurve.Hable });
            var loaded = PostProfile.Load(rel);
            Check(File.Exists(Path.Combine(root, "PostProfiles", "Dark.post")) && loaded is { Exposure: 0.2f, Tonemap: TonemapCurve.Hable },
                "a post profile writes into Content/PostProfiles and loads back");
            var volume = new PostProcessVolumeComponent { ProfilePath = "Dark" };
            volume.Profile.Exposure = 9;
            Check(volume.Values.Exposure == 0.2f, "a volume with a profile uses the file's values");
            volume.ProfilePath = "Missing";
            Check(volume.Values.Exposure == 9, "a missing profile falls back to the volume's own values");
            Check(PostProfile.Load("../outside") == null, "profiles can't leave Content");
        }
        finally
        {
            MaterialAsset.SourceRootOverride = null;
            try { Directory.Delete(root, recursive: true); } catch { /* temp folder */ }
        }
    }

    private static void CheckShadowAtlas()
    {
        using var atlas = new ShadowAtlas(null, 3000);
        long full = (long)atlas.Size * atlas.Size;
        Check(atlas.Size == 4096 && atlas.FreeTexels == full, "the shadow atlas rounds up to a power of two and starts empty");
        bool first = atlas.TryAllocate(2048, out var a);
        bool second = atlas.TryAllocate(1000, out var b);
        Check(first && second && a == Point.Zero && b == new Point(2048, 0),
            "tiles pack from the top-left, sizes round up to powers of two");
        var small = new List<Point>();
        while (atlas.TryAllocate(ShadowAtlas.MinTileSize, out var p)) small.Add(p);
        Check(atlas.FreeTexels == 0 && small.Distinct().Count() == small.Count &&
              small.Count == (full - 2048L * 2048 - 1024L * 1024) / (64 * 64),
            "small tiles fill every remaining texel without overlapping");
        Check(!atlas.TryAllocate(64, out _), "a full atlas refuses new tiles");
        foreach (var p in small) atlas.Free(p, 64);
        atlas.Free(b, 1024);
        bool merges = atlas.TryAllocate(2048, out var merged);
        Check(merges && merged.X + merged.Y > 0, "freed tiles merge back into bigger ones");
        atlas.Free(merged, 2048);
        atlas.Free(a, 2048);
        long freed = atlas.FreeTexels;
        bool all = atlas.TryAllocate(4096, out var whole);
        Check(freed == full && all && whole == Point.Zero,
            "freeing everything gives the whole atlas back");
    }

    private static void CheckCascadeSplits()
    {
        var ends = new float[4];
        ShadowMapRenderModule.SplitCascades(1, 101, 4, 0, ends);
        Check(ends.SequenceEqual(new[] { 26f, 51f, 76f, 101f }), "lambda 0 splits cascades evenly");
        ShadowMapRenderModule.SplitCascades(1, 1000, 3, 1, ends);
        Check(Math.Abs(ends[0] - 10) < 1e-3f && Math.Abs(ends[1] - 100) < 1e-2f && Math.Abs(ends[2] - 1000) < 0.1f,
            "lambda 1 splits them logarithmically");
        ShadowMapRenderModule.SplitCascades(0.1f, 150, 4, 0.75f, ends);
        Check(ends[0] > 0.1f && ends.Zip(ends.Skip(1)).All(p => p.First < p.Second) && Math.Abs(ends[3] - 150) < 1e-3f,
            "practical splits grow and the last cascade ends at the shadow distance");

        var sun = new DirectionalLight(Color.White, 10, -Vector3.UnitZ, castShadows: true, shadowResolution: 4096);
        sun.CascadeCount = 9;
        sun.ShadowDistance = float.NaN;
        Check(sun.CascadeCount == DirectionalLight.MaxCascades && sun.ShadowDistance == 150 && sun.CascadeResolution == 2048,
            "sun cascade settings are clamped and several cascades cap at 2048 texels each");
        sun.CascadeCount = 1;
        Check(sun.CascadeResolution == 4096, "a single cascade keeps the full shadow resolution");
    }

    private static void CheckLightFalloff()
    {
        Check(PointLight.Attenuation(0, 10) == 1 && PointLight.Attenuation(10, 10) == 0 && PointLight.Attenuation(12, 10) == 0,
            "point lights are full strength at the centre and reach zero at their radius");
        float previous = 2;
        bool falling = true;
        for (float d = 0; d < 10; d += 0.25f)
        {
            float value = PointLight.Attenuation(d, 10);
            falling &= value <= previous && value >= 0;
            previous = value;
        }
        Check(falling, "the falloff only goes down with distance");
        // window² · core² / (d² + core²), core = radius / 5: half strength (times the window) one core away
        float window = 1 - MathF.Pow(2 / 10f, 4);
        Check(Math.Abs(PointLight.Attenuation(2, 10) - window * window * 0.5f) < 1e-5f,
            "the falloff is the windowed inverse square around a core of a fifth of the radius");
        Check(PointLight.Attenuation(1, 10, sourceRadius: 3) > PointLight.Attenuation(1, 10),
            "a bigger light source spreads its light (less hot spot near it)");
    }

    private static void CheckBrdfLut()
    {
        const int size = 16;
        var lut = EnvironmentPrefilter.IntegrateBrdf(size, 256);
        Vector2 At(int x, int y) => lut[y * size + x];
        Check(lut.All(v => v.X >= 0 && v.Y >= 0 && v.X + v.Y <= 1.02f), "the split-sum LUT stays in [0, 1]");
        Check(At(size - 1, 0).X + At(size - 1, 0).Y > 0.95f, "a smooth surface seen head-on reflects all of F0 + bias");
        Check(At(size - 1, size - 1).X + At(size - 1, size - 1).Y < At(size - 1, 0).X + At(size - 1, 0).Y,
            "rough surfaces lose energy to shadowing/masking");
        Check(At(0, 0).Y > At(size - 1, 0).Y, "grazing angles add Fresnel");
        Check(EnvironmentPrefilter.MipCount(256) >= 6 && EnvironmentPrefilter.MipCount(4) == 1,
            "the prefiltered cube stops at 4 x 4");
    }

    private static void CheckProbeVolumes()
    {
        var main = ProbeVolumeData.Create(4, 4, 2, new Vector3(-10, -10, 0), new Vector3(10, 10, 5));
        var room = ProbeVolumeData.Create(3, 3, 3, new Vector3(0, 0, 0), new Vector3(4, 4, 3));
        room.Priority = 2; room.BlendMargin = 0.5f;
        var corner = ProbeVolumeData.Create(2, 2, 2, new Vector3(3, 3, 0), new Vector3(5, 5, 2));
        corner.Priority = 7;
        room.SHR[0] = new Vector4(1, 2, 3, 4);
        main.Detail.Add(room);
        main.Detail.Add(corner);
        Check(main.VolumeAt(new Vector3(1, 1, 1)) == room && main.VolumeAt(new Vector3(3.5f, 3.5f, 1)) == corner &&
              main.VolumeAt(new Vector3(-5, -5, 1)) == main,
            "the highest priority detail volume answers inside it, the main volume elsewhere");
        var ordered = main.VolumesByPriority();
        Check(ordered.SequenceEqual(new[] { corner, room, main }), "volumes blend highest priority first, the main volume last");
        for (int i = 0; i < 5; i++) main.Detail.Add(ProbeVolumeData.Create(2, 2, 2, Vector3.Zero, Vector3.One));
        Check(main.VolumesByPriority().Count == ProbeVolumeData.MaxVolumes, "at most 4 volumes reach the shaders");
        main.Detail.RemoveRange(2, 5);

        string file = Path.Combine(Path.GetTempPath(), $"obsidian-probes-{Guid.NewGuid():N}.probes");
        try
        {
            main.Save(file);
            var loaded = ProbeVolumeData.Load(file);
            Check(loaded.Detail.Count == 2 && loaded.Detail[0].Priority == 2 && loaded.Detail[0].BlendMargin == 0.5f &&
                  loaded.Detail[0].CountX == 3 && loaded.Detail[0].SHR[0] == new Vector4(1, 2, 3, 4) &&
                  loaded.Detail[1].BoundsMax == new Vector3(5, 5, 2) && loaded.CountX == 4,
                "probe files (v3) save and load the detail volumes with priority and margin");
        }
        finally { File.Delete(file); }
    }

    private static void CheckComponents()
    {
        foreach (var (id, name) in new[] { (ReflectionProbeComponent.TypeId, "Reflection Probe"), (ProbeVolumeComponent.TypeId, "Probe Volume"),
                     (PostProcessVolumeComponent.TypeId, "Post Process Volume"), (AoProxyComponent.TypeId, "AO Proxy") })
            Check(ComponentRegistry.Find(id) != null && ComponentEditorRegistry.Supports(id), $"{name} is registered and offered in Add Component");

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var room = new BasicEntity(assets.Cube, null, new Vector3(2, 0, 1.5f), Matrix.Identity, new Vector3(4, 3, 1.5f))
            { Name = "Room", IsEnabled = true };
        var crate = new BasicEntity(assets.Cube, null, new Vector3(0, 0, 0.5f), Matrix.Identity, new Vector3(0.5f))
            { Name = "Crate", IsEnabled = true };
        logic.BasicEntities.Add(room);
        logic.BasicEntities.Add(crate);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var roomVm = objects.Single(o => o.Name == "Room");
        var crateVm = objects.Single(o => o.Name == "Crate");

        // Reflection probe
        roomVm.AddableComponents.Single(c => c.DisplayName == "Reflection Probe").AddCommand.Execute(null);
        Publish();
        var probe = room.GetComponent<ReflectionProbeComponent>();
        var probeVm = roomVm.Components.OfType<ReflectionProbeComponentViewModel>().Single();
        Check(probe is { BoxProjection: true, Resolution: 128 } && probeVm.Resolution == 128 && room.WorldTransform.Hidden,
            "a new Reflection Probe box-projects at 128 texels and hides its box mesh");
        int request = probe.CaptureRequest;
        probeVm.Resolution = 2000;
        probeVm.Priority = 3;
        probeVm.BoxProjection = false;
        probeVm.CaptureCommand.Execute(null);
        Publish();
        Check(probe.Resolution == 512 && probe.Priority == 3 && !probe.BoxProjection && probe.CaptureRequest > request,
            "probe Inspector edits reach the engine (resolution clamped) and Capture asks for a new capture");
        Check(ComponentRegistry.Restore(ComponentRegistry.Capture(probe)) is ReflectionProbeComponent { Resolution: 512, Priority: 3, BoxProjection: false },
            "Reflection Probe records round-trip");

        // Probe volume (same gameobject is fine: different types)
        roomVm.AddableComponents.Single(c => c.DisplayName == "Probe Volume").AddCommand.Execute(null);
        Publish();
        var detail = room.GetComponent<ProbeVolumeComponent>();
        var detailVm = roomVm.Components.OfType<ProbeVolumeComponentViewModel>().Single();
        detailVm.ProbeSpacing = 0.5;
        detailVm.BlendMargin = 0.25;
        Publish();
        var bounds = ProbeVolumeComponent.Bounds(room);
        Check(detail.ProbeSpacing == 0.5f && detail.BlendMargin == 0.25f &&
              Vector3.Distance(bounds.Min, new Vector3(-2, -3, 0)) < 1e-4f && Vector3.Distance(bounds.Max, new Vector3(6, 3, 3)) < 1e-4f,
            "a Probe Volume covers its gameobject's box and takes Inspector edits");

        // Post process volume: override rows
        roomVm.AddableComponents.Single(c => c.DisplayName == "Post Process Volume").AddCommand.Execute(null);
        Publish();
        var post = room.GetComponent<PostProcessVolumeComponent>();
        var postVm = roomVm.Components.OfType<PostProcessVolumeComponentViewModel>().Single();
        var exposureRow = postVm.Overrides.Single(r => r.Name == "Exposure");
        var curveRow = postVm.Overrides.Single(r => r.Name == "Tonemap");
        Check(post.Overrides.Count == 0 && !exposureRow.IsOverridden && exposureRow.IsNumber && curveRow.IsChoice &&
              postVm.Overrides.All(r => r.Name != nameof(PostProcessSettings.BlendLutPath)),
            "a new Post Process Volume overrides nothing and lists every saved post setting");
        // NumericUpDown is decimal-based: binding 1.1f hands back 1.1 exactly, which isn't an edit
        foreach (var row in postVm.Overrides.Where(r => r.IsNumber)) row.Number = (double)(decimal)row.Number;
        Publish();
        Check(post.Overrides.Count == 0 && postVm.Overrides.All(r => !r.IsOverridden),
            "the Inspector's rounded copies of the values (White Point 1.1, Key 0.18...) don't tick any row");
        Check(postVm.Overrides.Single(r => r.Setting == nameof(PostProcessSettings.AdaptationSpeedUp)).Name !=
              postVm.Overrides.Single(r => r.Setting == nameof(PostProcessSettings.AdaptationSpeedDown)).Name,
            "every row has its own short label");
        exposureRow.Number = 1.75;
        curveRow.Choice = (int)TonemapCurve.Reinhard;
        postVm.BlendDistance = 6;
        Publish();
        Check(post.HasOverride("Exposure") && post.Profile.Exposure == 1.75f && post.Profile.Tonemap == TonemapCurve.Reinhard &&
              post.BlendDistance == 6 && exposureRow.IsOverridden,
            "editing a row's value sets and overrides it");
        exposureRow.IsOverridden = false;
        Publish();
        Check(!post.HasOverride("Exposure") && post.HasOverride("Tonemap") && post.Profile.Exposure == 1.75f,
            "unticking a row stops overriding it and keeps the value");
        var copy = ((BasicEntity)room.Clone).GetComponent<PostProcessVolumeComponent>();
        copy.Profile.Exposure = 5;
        copy.Overrides.Add("Bloom");
        Check(post.Profile.Exposure == 1.75f && !post.HasOverride("Bloom"), "cloning copies the volume's profile and overrides");
        Check(ComponentRegistry.Restore(ComponentRegistry.Capture(post)) is PostProcessVolumeComponent restored &&
              restored.HasOverride("Tonemap") && restored.Profile.Tonemap == TonemapCurve.Reinhard && restored.BlendDistance == 6,
            "Post Process Volume records round-trip");

        // AO proxy
        crateVm.AddableComponents.Single(c => c.DisplayName == "AO Proxy").AddCommand.Execute(null);
        Publish();
        var proxy = crate.GetComponent<AoProxyComponent>();
        var proxyVm = crateVm.Components.OfType<AoProxyComponentViewModel>().Single();
        Check(proxy.Shape == AoProxyShape.Auto && !crate.WorldTransform.Hidden, "a new AO Proxy fits itself (Auto) and keeps the mesh visible");
        proxyVm.Shape = AoProxyShape.Capsule;
        proxyVm.Intensity = 0.6;
        proxyVm.SizeScale = 9;
        Publish();
        Check(proxy is { Shape: AoProxyShape.Capsule, Intensity: 0.6f, SizeScale: 4 },
            "AO Proxy Inspector edits reach the engine (size clamped)");
        Check(ComponentRegistry.Restore(ComponentRegistry.Capture(proxy)) is AoProxyComponent { Shape: AoProxyShape.Capsule, Intensity: 0.6f },
            "AO Proxy records round-trip (shape by name)");

        // Removing the volumes shows the box again
        probeVm.RemoveCommand.Execute(null);
        detailVm.RemoveCommand.Execute(null);
        postVm.RemoveCommand.Execute(null);
        Publish();
        Check(room.Components.Count == 0 && !room.WorldTransform.Hidden, "removing the volumes shows the box mesh again");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckLightSettings()
    {
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var tube = new PointLight(new Vector3(0, 0, 3), 8, Color.White, 2, false, false, 256, 0, false) { Name = "Tube" };
        var sun = new DirectionalLight(Color.White, 20, new Vector3(0.3f, 0.2f, -1), castShadows: true) { Name = "Sun" };
        logic.PointLights.Add(tube);
        logic.DirectionalLights.Add(sun);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var tubeVm = objects.Single(o => o.Name == "Tube").Light!;
        var sunVm = objects.Single(o => o.Name == "Sun").Light!;
        Check(sunVm.IsDirectional && sunVm.CascadeCount == DirectionalLight.MaxCascades && !tubeVm.IsDirectional,
            "the light Inspector shows cascade settings for the sun");
        tubeVm.SourceRadius = 0.1;
        tubeVm.SourceLength = 1.5;
        tubeVm.StaticShadows = true;
        sunVm.CascadeCount = 2;
        sunVm.ShadowDistance = 300;
        sunVm.SplitLambda = 0.5;
        Publish();
        Check(tube.SourceRadius == 0.1f && tube.SourceLength == 1.5f && tube.StaticShadows &&
              sun.CascadeCount == 2 && sun.ShadowDistance == 300 && sun.SplitLambda == 0.5f,
            "light source size and cascade edits reach the engine");

        var spot = new SpotLight(new Vector3(0, 0, 4), 10, Color.White, 5, -Vector3.UnitZ) { Name = "Flashlight", SourceRadius = 0.03f };
        typeof(SpotLight).GetField("_cookiePath", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(spot, SpotLight.FlashlightCookie);
        logic.PointLights.Add(spot);
        string file = Path.Combine(Path.GetTempPath(), $"obsidian-lights-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var loaded = SceneSerialization.LoadFromFile(file, assets);
            var loadedTube = loaded.PointLights.Single(l => l.Name == "Tube");
            var loadedSpot = loaded.PointLights.OfType<SpotLight>().Single();
            var loadedSun = loaded.DirectionalLights.Single();
            Check(loadedTube.SourceRadius == 0.1f && loadedTube.SourceLength == 1.5f && loadedSpot.SourceRadius == 0.03f &&
                  loadedSpot.CookiePath == SpotLight.FlashlightCookie &&
                  loadedSun.CascadeCount == 2 && loadedSun.ShadowDistance == 300 && loadedSun.SplitLambda == 0.5f,
                "light source sizes, spot cookies and sun cascades save and load");
        }
        finally { File.Delete(file); }

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckLookDevScene()
    {
        var bridge = new EditorBridge();
        string path = Path.Combine(bridge.ContentSourceRoot, "Scenes", "LookDev.obsc");
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets();
        foreach (var field in typeof(Assets).GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.FieldType == typeof(ModelDefinition)))
            field.SetValue(assets, new ModelDefinition(null, bounds));
        var scene = SceneSerialization.LoadFromFile(path, assets);
        var entities = scene.BasicEntities;
        Check(entities.Count == 56 && entities.Count(e => e.GetComponent<AoProxyComponent>() != null) >= 10 &&
              entities.Any(e => e.GetComponent<ReflectionProbeComponent>() != null) &&
              entities.Any(e => e.GetComponent<ProbeVolumeComponent>() != null),
            "the LookDev scene loads its spheres, room, reflection probe, detail probe volume and AO proxies");
        var mirror = entities.SingleOrDefault(e => e.Name == "Mirror Glass Pane");
        var body = entities.SingleOrDefault(e => e.Name == "Player Body");
        Check(mirror?.GetComponent<PlanarReflectionComponent>() != null && body?.GetComponent<AnimatorComponent>() != null &&
              scene.MainCamera.Components.OfType<ScriptBehaviourComponent>().Any(c => c.ScriptId == "camera-body"),
            "it has the mirror glass with a Planar Reflection and a Player Body the camera carries");
        Check(scene.DirectionalLights.Single().CascadeCount == 4 &&
              scene.PointLights.OfType<SpotLight>().Any(s => s.CookiePath == SpotLight.FlashlightCookie) &&
              scene.PointLights.Any(l => l.SourceLength > 0),
            "it has a 4-cascade sun, a cookie flashlight and a tube light");
        Check(scene.BakedProbes is { Detail.Count: >= 1 } && scene.BakedProbes.Detail[0].ProbeCount > 8,
            "it ships baked probes with the room's detail volume");
    }

    /// <summary>The new and rewritten shaders compile into the content build with the techniques and parameters the renderer sets.</summary>
    public static void RunGraphics(Microsoft.Xna.Framework.Graphics.GraphicsDevice graphics, Microsoft.Xna.Framework.Content.ContentManager content)
    {
        ForwardLightingChecks.RunGraphics(graphics, content);
        ReflectionProbeChecks.RunGraphics(graphics);
        Microsoft.Xna.Framework.Graphics.Effect Load(string path) => content.Load<Microsoft.Xna.Framework.Graphics.Effect>(path);
        bool Has(Microsoft.Xna.Framework.Graphics.Effect effect, params string[] names) =>
            names.All(n => effect.Parameters[n] != null || effect.Techniques[n] != null);

        Check(Has(Load("Shaders/Deferred/DeferredDirectionalLight"), "Shadowed", "Unshadowed", "CascadeViewProjection", "CascadeEnds", "CascadeCount"),
            "the sun shader samples cascaded shadows (screen-space shadow techniques removed)");
        Check(Has(Load("Shaders/Deferred/DeferredPointLight"), "lightSourceRadius", "lightSourceLength", "CookieTexture", "ShadowTiles"),
            "the point light shader has sphere/tube sources, cookies and shadow atlas tiles");
        Check(Has(Load("Shaders/Deferred/DeferredEnvironmentMap"), "ProbeData", "ReflectionCubeMap", "ProbeCube0", "BrdfLut", "AoProxyA"),
            "the environment shader blends probe volumes, local reflection probes, the BRDF LUT and AO proxies");
        Check(Has(Load("Shaders/Deferred/Froxel"), "CascadeViewProjection", "ProbeData"), "the fog shader samples the cascades and packed probes");
        Check(Has(Load("Shaders/Deferred/Lightmap"), "Lightmap", "IrradianceMap", "DirectionMap", "MaskMap", "DepthMap", "NormalMap", "WorldViewProj") &&
              Has(Load("Shaders/Deferred/DeferredEnvironmentMap"), "LightmapMap", "UseLightmaps", "LightmapIntensity") &&
              Has(Load("Shaders/Deferred/DeferredPointLight"), "ShadowMaskMap", "ShadowMaskChannel") &&
              Has(Load("Shaders/Deferred/DeferredDirectionalLight"), "ShadowMaskMap", "ShadowMaskChannel"),
            "the lightmap pass compiles, and the environment and light shaders read its lightmap and shadow mask");
        Check(Has(Load("Shaders/Forward/Forward"), "LightSourceRadius", "PointShadowAtlas", "LightShadowTiles", "LightShadowSettings",
            "CookieAtlas", "LightCookieRect", "LightCookieRight", "LightCookieUp"), "the forward shader lights glass with sized sources, local shadows and cookies");
        Check(Has(Load("Shaders/Forward/Forward"), "ReflectionPass", "ClipPlane", "UsePlanarReflection", "PlanarReflectionMap", "PlanarSky", "SkyCubeMap"),
            "the forward shader draws and reads planar reflections");
        Check(Has(Load("Shaders/GBufferSetup/Gbuffer"), "PreviousWorldViewProj", "AoMap"), "the G-buffer writes velocity and material AO");
        Check(Has(Load("Shaders/TemporalAntiAliasing/TemporalAntiAliasing"), "VelocityMap", "Sharpness"), "TAA reprojects with velocity and sharpens");
        Check(Has(Load("Shaders/PostProcessing/PostProcessing"), "TonemapCurve"), "the final pass picks a tonemap curve");
        Check(Has(Load("Shaders/PostProcessing/AutoExposure"), "Histogram"), "auto exposure builds a histogram");
        Check(Has(Load("Shaders/PostProcessing/ColorGrading"), "CombineLUT"), "colour grading combines two LUTs");
        Check(Has(Load("Shaders/PostProcessing/Fxaa"), "Fxaa") && Has(Load("Shaders/PostProcessing/RetinaBurn"), "Burn") &&
              Has(Load("Shaders/Deferred/EnvironmentPrefilter"), "Prefilter") && Has(Load("Shaders/Editor/DebugView"), "Color", "Alpha", "Cascades"),
            "the FXAA, retina burn, cubemap prefilter and debug view shaders load");

        using var atlas = new ShadowAtlas(graphics, 1024);
        Check(atlas.Target is { Width: 1024, Height: 1024 }, "the shadow atlas owns one square depth target");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
