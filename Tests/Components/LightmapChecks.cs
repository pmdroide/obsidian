using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Anvil.Models;
using Anvil.Services;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Lighting;
using Microsoft.Xna.Framework;

/// <summary>
/// Lightmaps (Source 2 look, phase 4.2): the UV2 unwrap, the CPU baker on a hand-built scene (indirect light, AO, the
/// static lights' shadow mask and their channels), the .lightmaps sidecar, the Lightmap Static / Baked Shadows flags in
/// the scene file and through the editor bridge, and the baked LookDev scene. Runs without a graphics device.
/// </summary>
internal static class LightmapChecks
{
    public static void Run()
    {
        CheckUnwrap();
        CheckBake();
        CheckSceneFlags();
        CheckLookDevScene();
    }

    // ---------------- UV2 ----------------

    private static void CheckUnwrap()
    {
        LightmapUnwrap cube = LightmapUnwrap.Build(new[] { Cube() });
        Check(!cube.Authored && cube.Charts.Count == 6 && cube.Parts[0].Positions.Length == 24 && cube.Parts[0].Indices.Length == 36,
            "a cube unwraps into one chart per face, its vertices split at the chart borders");
        Check(cube.Charts.All(c => Math.Abs((c.Max - c.Min).X - 2) < 1e-4f && Math.Abs((c.Max - c.Min).Y - 2) < 1e-4f),
            "each chart is the face projected flat (2 x 2 model units)");

        // Two ramps facing up joined at x = 2: the second climbs back over the first, so their projections overlap
        var ramp = new LightmapUnwrap.SourcePart
        {
            Positions = new[]
            {
                new Vector3(0, 0, 0), new Vector3(2, 0, 0.5f), new Vector3(2, 1, 0.5f), new Vector3(0, 1, 0),
                new Vector3(0, 0, 1), new Vector3(0, 1, 1),
            },
            Indices = new[] { 0, 1, 2, 0, 2, 3, 1, 4, 5, 1, 5, 2 },
        };
        LightmapUnwrap folded = LightmapUnwrap.Build(new[] { ramp });
        Check(folded.Charts.Count == 2, "a surface folding back over itself in the projection is split into two charts");

        var authored = Cube();
        authored.Uv2 = authored.Positions.Select(p => new Vector2(p.X * 0.25f + 0.5f, p.Y * 0.25f + 0.5f)).ToArray();
        LightmapUnwrap kept = LightmapUnwrap.Build(new[] { authored });
        Check(kept.Authored && kept.Charts.Count == 1 && kept.Parts[0].Positions.Length == 24 &&
              kept.Parts[0].Coords[5] == authored.Uv2[5], "a model with TEXCOORD1 keeps its authored lightmap UVs");

        var glass = Cube();
        glass.Lightmapped = false;
        LightmapUnwrap mixed = LightmapUnwrap.Build(new[] { Cube(), glass });
        Check(mixed.Parts[0].Lightmapped && mixed.Parts[0].Indices.Length == 36 && !mixed.Parts[1].Lightmapped &&
              mixed.Parts[1].Indices.Length == 0 && mixed.Parts[1].SourceVertexCount == 24,
            "parts the G-buffer doesn't draw (glass) get no lightmap but keep their vertex count for matching");
    }

    // ---------------- Baker ----------------

    private static void CheckBake()
    {
        // A 10 x 10 m floor (lightmapped), a wall on it and a box hovering over its middle; a sun from straight above
        var input = new LightingBakeInput();
        var a = new List<Vector3>(); var b = new List<Vector3>(); var c = new List<Vector3>();
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            a.Add(p0); b.Add(p1); c.Add(p2);
            a.Add(p0); b.Add(p2); c.Add(p3);
        }
        void Box(Vector3 min, Vector3 max)
        {
            Vector3 d = max - min;
            foreach (var (o, u, v) in new[]
                     {
                         (min, new Vector3(d.X, 0, 0), new Vector3(0, d.Y, 0)), (min + new Vector3(0, 0, d.Z), new Vector3(d.X, 0, 0), new Vector3(0, d.Y, 0)),
                         (min, new Vector3(d.X, 0, 0), new Vector3(0, 0, d.Z)), (min + new Vector3(0, d.Y, 0), new Vector3(d.X, 0, 0), new Vector3(0, 0, d.Z)),
                         (min, new Vector3(0, d.Y, 0), new Vector3(0, 0, d.Z)), (min + new Vector3(d.X, 0, 0), new Vector3(0, d.Y, 0), new Vector3(0, 0, d.Z)),
                     })
                Quad(o, o + u, o + u + v, o + v);
        }
        Quad(new Vector3(-5, -5, 0), new Vector3(5, -5, 0), new Vector3(5, 5, 0), new Vector3(-5, 5, 0));
        Box(new Vector3(-5, 4.5f, 0), new Vector3(5, 5, 3));      // wall along the north edge
        Box(new Vector3(-1, -1, 1), new Vector3(1, 1, 1.5f));     // hovering box: a sun shadow under it
        input.A = a.ToArray(); input.B = b.ToArray(); input.C = c.ToArray();
        int n = input.A.Length;
        input.Normal = new Vector3[n];
        Vector3 boxCentre = new Vector3(0, 0, 1.25f), wallCentre = new Vector3(0, 4.75f, 1.5f);
        for (int i = 0; i < n; i++)
        {
            Vector3 normal = Vector3.Normalize(Vector3.Cross(input.B[i] - input.A[i], input.C[i] - input.A[i]));
            Vector3 centre = (input.A[i] + input.B[i] + input.C[i]) / 3;
            // Outward: away from the solid the triangle belongs to (the floor faces up)
            Vector3 inside = i < 2 ? centre - Vector3.UnitZ : i < 14 ? wallCentre : boxCentre;
            if (Vector3.Dot(normal, centre - inside) < 0) normal = -normal;
            input.Normal[i] = normal;
        }
        input.Albedo = Enumerable.Repeat(new Vector3(0.5f), n).ToArray();
        input.Emission = new Vector3[n];
        input.GeometryBounds = new BoundingBox(new Vector3(-5, -5, 0), new Vector3(5, 5, 3));
        input.DirectionalLights.Add(new LightingBakeInput.DirectionalLightInput
        {
            ToLight = Vector3.UnitZ, Radiance = new Vector3(10), LightId = 77, BakedShadows = true,
        });
        // Two overlapping point lights need two channels; a third far away can share one with them
        input.PointLights.Add(new LightingBakeInput.PointLightInput { Position = new Vector3(-3, 0, 2), Radius = 4, Radiance = new Vector3(5), LightId = 1, BakedShadows = true, SpotCosOuter = -2, SpotCosInner = -1 });
        input.PointLights.Add(new LightingBakeInput.PointLightInput { Position = new Vector3(-1, 0, 2), Radius = 4, Radiance = new Vector3(4), LightId = 2, BakedShadows = true, SpotCosOuter = -2, SpotCosInner = -1 });
        input.PointLights.Add(new LightingBakeInput.PointLightInput { Position = new Vector3(40, 0, 2), Radius = 3, Radiance = new Vector3(1), LightId = 3, BakedShadows = true, SpotCosOuter = -2, SpotCosInner = -1 });
        input.PointLights.Add(new LightingBakeInput.PointLightInput { Position = new Vector3(0, 0, 2), Radius = 3, Radiance = new Vector3(1), LightId = 4, BakedShadows = false, SpotCosOuter = -2, SpotCosInner = -1 });

        // The floor as a Lightmap Static gameobject: a unit quad scaled 5x
        var floorPart = new LightmapUnwrap.SourcePart
        {
            Positions = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) },
            Normals = Enumerable.Repeat(Vector3.UnitZ, 4).ToArray(),
            Indices = new[] { 0, 1, 2, 0, 2, 3 },
        };
        input.LightmapInstances.Add(new LightingBakeInput.LightmapInstanceInput
        {
            EntityId = 5, Name = "Floor", ModelKey = floorPart, Parts = new List<LightmapUnwrap.SourcePart> { floorPart },
            World = Matrix.CreateScale(5, 5, 1), Scale = new Vector3(5, 5, 1), TexelsPerUnit = 4,
        });

        var settings = new LightingSettings { LightmapSamples = 96, LightmapShadowSamples = 8, LightmapTexelsPerUnit = 4, SunAngle = 1 };
        var bvh = new TriangleBvh(input.A, input.B, input.C);
        LightmapData data = LightmapBaker.Bake(input, settings, bvh, null, null, CancellationToken.None);
        Check(data != null && data.Instances.Count == 1 && data.Pages.Count == 1 && data.Instances[0].EntityId == 5 &&
              data.TexelCount >= 40 * 40, "the floor gets its own block of texels on one lightmap page (4 texels/m)");
        LightmapData.Page page = data.Pages[0];
        Check(page.Width % 4 == 0 && page.Height % 4 == 0 && data.Instances[0].Uv[0].All(uv => uv.X >= 0 && uv.X <= 1 && uv.Y >= 0 && uv.Y <= 1),
            "lightmap UVs lie on the page");

        Vector4 Texel(LightmapData.Page p, Vector3 world, Func<LightmapData.Page, int, Vector4> read)
        {
            // Floor world xy -> the quad's UVs (bilinear between its corners), then page texel
            Vector2[] uv = data.Instances[0].Uv[0];
            float s = (world.X + 5) / 10, t = (world.Y + 5) / 10;
            Vector2 at = uv[0] * (1 - s) * (1 - t) + uv[1] * s * (1 - t) + uv[2] * s * t + uv[3] * (1 - s) * t;
            int x = Math.Clamp((int)(at.X * p.Width), 0, p.Width - 1), y = Math.Clamp((int)(at.Y * p.Height), 0, p.Height - 1);
            return read(p, y * p.Width + x);
        }
        Vector4 Mask(LightmapData.Page p, int i) => new Color { PackedValue = p.Mask[i] }.ToVector4();
        Vector4 Irradiance(LightmapData.Page p, int i) => LightmapData.UnpackIrradiance(p.Irradiance[i]);

        int sun = data.Channels.Single(ch => ch.LightId == 77).Channel;
        Check(data.Channels.First(ch => ch.LightId == 77).Directional && sun == 0, "the sun takes the first shadow mask channel");
        int c1 = data.Channels.Single(ch => ch.LightId == 1).Channel, c2 = data.Channels.Single(ch => ch.LightId == 2).Channel;
        int c3 = data.Channels.Single(ch => ch.LightId == 3).Channel;
        Check(c1 != c2 && c1 != sun && c2 != sun && (c3 == c1 || c3 == c2) && data.Channels.All(ch => ch.LightId != 4) && data.LightsWithoutChannel == 0,
            "overlapping lights get different channels, a distant one shares, a light without Baked Shadows gets none");

        float under = Texel(page, new Vector3(0, 0, 0), Mask).X, open = Texel(page, new Vector3(-3.5f, -3.5f, 0), Mask).X;
        Check(under < 0.1f && open > 0.9f, $"the sun mask is dark under the hovering box ({under:0.00}) and lit in the open ({open:0.00})");
        Vector4 nearWall = Texel(page, new Vector3(-3.5f, 4.3f, 0), Irradiance), middle = Texel(page, new Vector3(-3.5f, -3.5f, 0), Irradiance);
        Check(nearWall.W < middle.W - 0.1f && middle.W > 0.9f,
            $"baked AO darkens the floor at the foot of the wall ({nearWall.W:0.00} vs {middle.W:0.00} in the open)");
        Check(middle.X > 0 && float.IsFinite(middle.X) && Texel(page, new Vector3(-3.5f, 3.8f, 0), Irradiance).X > 0,
            "texels gather sky and bounce light");

        // Channel lookups follow the lights; a moved light falls back to its shadow map
        Check(data.ChannelOf(1, new Vector3(-3, 0, 2), 4) == LightmapData.OneHot(c1) &&
              data.ChannelOf(1, new Vector3(-2, 0, 2), 4) == Vector4.Zero &&
              data.ChannelOf(77, Vector3.UnitZ) == LightmapData.OneHot(sun) &&
              data.ChannelOf(77, Vector3.Normalize(new Vector3(0.3f, 0, 1))) == Vector4.Zero,
            "a light keeps its mask channel only where it was baked");

        string file = Path.Combine(Path.GetTempPath(), $"obsidian-{Guid.NewGuid():N}{LightmapData.Extension}");
        try
        {
            data.Save(file);
            LightmapData loaded = LightmapData.Load(file);
            Check(loaded.Pages.Count == 1 && loaded.Pages[0].Width == page.Width && loaded.Pages[0].Irradiance.SequenceEqual(page.Irradiance) &&
                  loaded.Pages[0].Mask.SequenceEqual(page.Mask) && loaded.Pages[0].Direction.SequenceEqual(page.Direction) &&
                  loaded.Instances.Single().Uv[0].SequenceEqual(data.Instances[0].Uv[0]) &&
                  loaded.Meshes.Single().Parts.Single().Indices.SequenceEqual(data.Meshes[0].Parts[0].Indices) &&
                  loaded.Channels.Count == data.Channels.Count && loaded.FindInstance(5) != null && loaded.TexelCount == data.TexelCount,
                ".lightmaps files round-trip the pages, geometry, UVs and channels");
        }
        finally { File.Delete(file); }
    }

    // ---------------- Flags: scene file and editor ----------------

    private static void CheckSceneFlags()
    {
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var wall = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Wall", IsEnabled = true, LightmapStatic = true, LightmapScale = 2 };
        var prop = new BasicEntity(assets.Cube, null, Vector3.One, Matrix.Identity, Vector3.One) { Name = "Prop", IsEnabled = true };
        var copy = (BasicEntity)wall.Clone;
        Check(copy.LightmapStatic && copy.LightmapScale == 2 && prop.LightmapScale == 1, "clones keep Lightmap Static and Lightmap Scale");
        var moving = new PointLight(Vector3.Zero, 5, Color.White, 1, false, false, 256, 0, false) { BakedShadows = false };
        Check(!((PointLight)moving.Clone).BakedShadows && new DirectionalLight(Color.White, 1, -Vector3.UnitZ).BakedShadows,
            "lights bake their shadows by default, and clones keep Baked Shadows");

        var scene = new Scene { Name = "Lightmapped" };
        scene.BasicEntities.Add(wall);
        scene.BasicEntities.Add(prop);
        scene.PointLights.Add(moving);
        string path = Path.Combine(Path.GetTempPath(), $"lightmapped-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(scene, path, assets);
            var saved = JsonNode.Parse(File.ReadAllText(path))!;
            var records = saved["Entities"]!.AsArray();
            Check((bool?)records.Single(e => (string?)e!["Name"] == "Wall")!["LightmapStatic"] == true &&
                  (float?)records.Single(e => (string?)e!["Name"] == "Wall")!["LightmapScale"] == 2 &&
                  records.Single(e => (string?)e!["Name"] == "Prop")!["LightmapStatic"] == null &&
                  records.Single(e => (string?)e!["Name"] == "Prop")!["LightmapScale"] == null &&
                  saved["Lighting"]!["LightmapDataFile"] == null,
                "Lightmap Static/Scale are only written when set, and nothing baked writes no .lightmaps");
            var loaded = SceneSerialization.LoadFromFile(path, assets);
            Check(loaded.BasicEntities.Single(e => e.Name == "Wall") is { LightmapStatic: true, LightmapScale: 2 } &&
                  loaded.BasicEntities.Single(e => e.Name == "Prop") is { LightmapStatic: false, LightmapScale: 1 } &&
                  !loaded.PointLights.Single().BakedShadows,
                "the flags round-trip through the scene file");
        }
        finally { File.Delete(path); }

        // ---- Inspector through the bridge ----
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Pillar", IsEnabled = true };
        var light = new PointLight(new Vector3(0, 0, 3), 5, Color.White, 1, false, false, 256, 0, false);
        logic.BasicEntities.Add(entity);
        logic.ActiveScene.PointLights.Add(light);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Drain(bridge);
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var vm = objects.Single(o => o.EngineId == entity.Id);
        var lightVm = objects.Single(o => o.EngineId == light.Id);
        Check(!vm.LightmapStatic && vm.LightmapScale == 1 && lightVm.Light.BakedShadows, "the inspector shows the engine's lightmap flags");

        vm.LightmapStatic = true;
        vm.LightmapScale = 0.5;
        lightVm.Light.BakedShadows = false;
        Drain(bridge);
        Check(entity.LightmapStatic && entity.LightmapScale == 0.5f && !light.BakedShadows && logic.ActiveScene.IsDirty,
            "ticking Lightmap Static, setting its scale and Baked Shadows reach the engine (and mark the scene dirty)");
        entity.LightmapScale = 3;
        Drain(bridge);
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        Check(vm.LightmapScale == 3, "the inspector follows Lightmap Scale changed in the engine");
    }

    // ---------------- LookDev ----------------

    private static void CheckLookDevScene()
    {
        var bridge = new EditorBridge();
        string path = Path.Combine(bridge.ContentSourceRoot, "Scenes", "LookDev.obsc");
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets();
        foreach (var field in typeof(Assets).GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.FieldType == typeof(ModelDefinition)))
            field.SetValue(assets, new ModelDefinition(null, bounds));
        Scene scene = SceneSerialization.LoadFromFile(path, assets);
        var lightmapped = scene.BasicEntities.Where(e => e.LightmapStatic).ToList();
        Check(lightmapped.Count == 37 && lightmapped.Any(e => e.Name == "Room Lintel") && lightmapped.Single(e => e.Name == "Floor").LightmapScale == 0.5f &&
              !scene.BasicEntities.Single(e => e.Name == "Y Bot").LightmapStatic,
            "the LookDev scene's floor, checker, room and walls are Lightmap Static; the spheres and characters aren't");
        LightmapData baked = scene.BakedLightmaps;
        Check(baked != null && baked.Instances.Count == 37 && lightmapped.All(e => baked.FindInstance(e.Id) != null) &&
              baked.Channels.Count == 4 && baked.LightsWithoutChannel == 0,
            "it ships lightmaps for all of them, with a shadow mask channel for the sun and each of its 3 lights");
    }

    private static LightmapUnwrap.SourcePart Cube()
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        foreach (Vector3 n in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
        {
            Vector3 u = Math.Abs(n.Z) > 0.5f ? Vector3.UnitX : Vector3.UnitZ;
            Vector3 v = Vector3.Cross(n, u);
            int first = positions.Count;
            foreach (var (su, sv) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                positions.Add(n + u * su + v * sv);
                normals.Add(n);
            }
            indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        return new LightmapUnwrap.SourcePart { Positions = positions.ToArray(), Normals = normals.ToArray(), Indices = indices.ToArray() };
    }

    private static void Drain(EditorBridge bridge)
    {
        for (int i = 0; i < 6; i++)
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, null);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
