using System.Reflection;
using System.Text.Json.Nodes;
using Anvil.Models;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Scripting;
using Engine.Voxels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

/// <summary>
/// Voxel world in Inspector > Environment: settings and save/load, generation, the block grid and its raycasts,
/// greedy meshing (winding, merging, chunk borders), the painted textures, chunk colliders in BEPU, the
/// VoxelSystem lifecycle (async/sync builds, edits, Stop, switching off), the Inspector, and the VoxelTest
/// sample played with real physics. With --graphics it also uploads a world and draws a chunk on the GPU.
/// </summary>
internal static class VoxelChecks
{
    private const float Dt = 1 / 60f;

    public static void Run()
    {
        CheckSettings();
        CheckGeneration();
        CheckGridAndRaycasts();
        CheckMeshing();
        CheckTextures();
        CheckSystemAndColliders();
        CheckEditor();
        CheckSampleScene();
    }

    private static VoxelWorldSettings Small(VoxelTerrain terrain = VoxelTerrain.Flat, int seed = 42, int size = 64, int height = 32,
        float block = 1, float floor = 0, bool caves = false, float trees = 0, float sea = 0) =>
        new(terrain, seed, size, height, block, floor, caves, trees, sea, Collision: true);

    private static void CheckSettings()
    {
        var defaults = new EnvironmentSettings();
        Check(!defaults.Voxels && VoxelWorldSettings.From(defaults) == null && defaults.VoxelTerrain == VoxelTerrain.Hills &&
              defaults.VoxelWorldSize == 128 && defaults.VoxelWorldHeight == 64 && defaults.VoxelBlockSize == 1 &&
              defaults.VoxelSeaLevel == 0.3f && defaults.VoxelCollision,
            "scenes have no voxel world by default; turned on it is 128 x 128 x 64 one-metre blocks of hills");
        Check(Enum.GetNames<VoxelTerrain>().SequenceEqual(new[] { "Flat", "Hills", "Mountains", "Islands" }),
            "the voxel terrains are Flat, Hills, Mountains and Islands (saved by name)");
        Check(Enum.GetNames<VoxelBlock>().Length == VoxelBlocks.Count && (int)VoxelBlock.Air == 0 && (int)VoxelBlock.Lamp == 10 &&
              Enum.GetNames<VoxelSurface>().Length == VoxelBlocks.SurfaceCount && VoxelBlocks.Placeable.All(VoxelBlocks.IsSolid),
            "block numbers are stable (Air = 0) and every placeable block is solid");

        var messy = new EnvironmentSettings
        {
            VoxelTerrain = (VoxelTerrain)9, VoxelWorldSize = 1000, VoxelWorldHeight = 7, VoxelBlockSize = float.NaN,
            VoxelBaseHeight = float.PositiveInfinity, VoxelTrees = 3, VoxelSeaLevel = -1,
        };
        messy.Normalize();
        Check(messy.VoxelTerrain == VoxelTerrain.Hills && messy.VoxelWorldSize == 256 && messy.VoxelWorldHeight == 16 &&
              messy.VoxelBlockSize == 1 && messy.VoxelBaseHeight == -24 && messy.VoxelTrees == 1 && messy.VoxelSeaLevel == 0,
            "out-of-range voxel settings are clamped and unknown terrains fall back to Hills");
        Check(VoxelWorldSettings.ClampSize(50) == 64 && VoxelWorldSettings.ClampSize(16) == 32 &&
              VoxelWorldSettings.ClampHeight(70) == 64 && VoxelWorldSettings.ClampBlockSize(0) == 0.25f,
            "world sizes snap to whole chunks and heights to 16 blocks");
        var fromScript = new EnvironmentSettings { Voxels = true, VoxelWorldSize = 999, VoxelSeaLevel = 5 };
        Check(VoxelWorldSettings.From(fromScript) is { Size: 256, SeaLevel: 0.9f },
            "settings a script left unclamped are clamped when the world is built");

        var assets = new Assets();
        var logic = new MainSceneLogic();
        logic.ActiveScene.Environment = new EnvironmentSettings
        {
            Voxels = true, VoxelTerrain = VoxelTerrain.Mountains, VoxelSeed = 77, VoxelWorldSize = 96, VoxelWorldHeight = 48,
            VoxelBlockSize = 0.5f, VoxelBaseHeight = -10, VoxelCaves = false, VoxelTrees = 0.2f, VoxelSeaLevel = 0, VoxelCollision = false,
        };
        string file = Path.Combine(Path.GetTempPath(), "obsidian-voxels-" + Guid.NewGuid().ToString("N") + ".obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            Check(json["Environment"]!["VoxelTerrain"]!.GetValue<string>() == "Mountains", "the voxel terrain is saved by name");
            var loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(VoxelWorldSettings.From(loaded) == VoxelWorldSettings.From(logic.ActiveScene.Environment),
                "voxel settings survive scene save/load");
            var environment = json["Environment"]!.AsObject();
            foreach (string key in environment.Select(p => p.Key).Where(k => k.StartsWith("Voxel")).ToList())
                environment.Remove(key);
            File.WriteAllText(file, json.ToJsonString());
            loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(!loaded.Voxels && loaded.VoxelSeed == 1337 && loaded.VoxelTerrain == VoxelTerrain.Hills,
                "scenes saved before voxel worlds load without one and with the default settings");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static bool SameBlocks(VoxelWorld a, VoxelWorld b)
    {
        for (int z = 0; z < a.SizeZ; z++)
        for (int y = 0; y < a.SizeY; y++)
        for (int x = 0; x < a.SizeX; x++)
            if (a.GetBlock(x, y, z) != b.GetBlock(x, y, z)) return false;
        return true;
    }

    private static void CheckGeneration()
    {
        var hills = Small(VoxelTerrain.Hills, caves: true, trees: 1, sea: 0.3f);
        VoxelWorld a = VoxelGenerator.Generate(hills), b = VoxelGenerator.Generate(hills);
        Check(SameBlocks(a, b), "the same settings always generate the same world");
        Check(!SameBlocks(a, VoxelGenerator.Generate(hills with { Seed = 43 })), "another seed generates another world");
        Check(a.SizeX == 64 && a.SizeY == 64 && a.SizeZ == 32 && a.ChunkCount == 4 &&
              a.Origin == new Vector3(-32, -32, 0) && a.SeaLevel == 10,
            "a world is centred on the origin, sits on its floor height and is split into 32-block chunks");

        bool floorSolid = true;
        for (int y = 0; y < a.SizeY; y++)
        for (int x = 0; x < a.SizeX; x++)
            floorSolid &= a.IsSolid(x, y, 0);
        Check(floorSolid, "the bottom layer is solid everywhere, so nothing falls out of the world");
        Check(a.Count(VoxelBlock.Grass) > 0 && a.Count(VoxelBlock.Dirt) > 0 && a.Count(VoxelBlock.Stone) > 0 &&
              a.Count(VoxelBlock.Wood) > 0 && a.Count(VoxelBlock.Leaves) > 0,
            "hills have grass over dirt over stone, and trees");
        Check(VoxelGenerator.Generate(hills with { Trees = 0 }).Count(VoxelBlock.Wood) == 0, "Trees 0 plants none");

        int Air(VoxelWorld w) => w.Count(VoxelBlock.Air);
        Check(Air(VoxelGenerator.Generate(hills with { Trees = 0 })) > Air(VoxelGenerator.Generate(hills with { Trees = 0, Caves = false })),
            "caves carve tunnels under the surface");

        VoxelWorld flat = VoxelGenerator.Generate(Small());
        int level = flat.SurfaceHeight(0, 0);
        bool even = true;
        for (int y = 0; y < flat.SizeY; y++)
        for (int x = 0; x < flat.SizeX; x++)
            even &= flat.SurfaceHeight(x, y) == level && flat.GetBlock(x, y, level) == VoxelBlock.Grass;
        Check(even && level > 0, "flat terrain is one grass level everywhere");

        VoxelWorld mountains = VoxelGenerator.Generate(Small(VoxelTerrain.Mountains, size: 128, height: 64));
        Check(mountains.Count(VoxelBlock.Snow) > 0, "mountains reach the snow line");

        VoxelWorld islands = VoxelGenerator.Generate(Small(VoxelTerrain.Islands, size: 128, height: 32, sea: 0.4f));
        int corner = islands.SurfaceHeight(0, 0);
        int highest = Enumerable.Range(0, 128 * 128).Max(i => islands.SurfaceHeight(i % 128, i / 128));
        Check(corner < islands.SeaLevel && highest >= islands.SeaLevel && islands.Count(VoxelBlock.Sand) > 0,
            "islands rise from the sea with sandy shores; the world's edge is under water");
    }

    private static void CheckGridAndRaycasts()
    {
        VoxelWorld world = VoxelGenerator.Generate(Small(floor: -8));
        int top = world.SurfaceHeight(10, 10);
        float surfaceZ = world.Origin.Z + (top + 1) * world.BlockSize;
        Vector3 above = world.BlockCenter(new VoxelCoord(10, 10, top)) + new Vector3(0, 0, 10);

        Check(world.Raycast(above, -Vector3.UnitZ, 50, out VoxelHit hit) && hit.Block == new VoxelCoord(10, 10, top) &&
              hit.Normal == Vector3.UnitZ && hit.Adjacent == new VoxelCoord(10, 10, top + 1) && hit.Type == VoxelBlock.Grass &&
              Math.Abs(hit.Point.Z - surfaceZ) < 1e-4f && Math.Abs(hit.Distance - (above.Z - surfaceZ)) < 1e-4f,
            "a ray down hits the top block's upper face; the cell above it is where a block would go");
        Check(!world.Raycast(above, Vector3.UnitZ, 50, out _) && !world.Raycast(above, -Vector3.UnitZ, 5, out _),
            "rays pointing away or too short miss");

        Vector3 outside = new Vector3(world.Origin.X - 5, world.BlockCenter(new VoxelCoord(0, 20, 1)).Y, world.BlockCenter(new VoxelCoord(0, 20, 1)).Z);
        Check(world.Raycast(outside, Vector3.UnitX, 20, out hit) && hit.Block == new VoxelCoord(0, 20, 1) && hit.Normal == -Vector3.UnitX &&
              Math.Abs(hit.Distance - 5) < 1e-4f,
            "a ray from outside the world hits the side wall it enters through");
        Check(world.Raycast(world.BlockCenter(new VoxelCoord(3, 3, 2)), Vector3.UnitY, 10, out hit) && hit.Distance == 0 &&
              hit.Block == new VoxelCoord(3, 3, 2), "a ray starting inside a block hits it at once");

        Check(world.WorldToBlock(world.BlockCenter(new VoxelCoord(7, 8, 9))) == new VoxelCoord(7, 8, 9) &&
              world.TryGetSurface(above, out Vector3 surface) && Math.Abs(surface.Z - surfaceZ) < 1e-4f,
            "world positions map to blocks and back, and the surface under a point is found");

        int version = world.Version;
        Check(!world.SetBlock(5, 5, 0, VoxelBlock.Air) && !world.SetBlock(-1, 5, 5, VoxelBlock.Stone) &&
              !world.SetBlock(10, 10, top, VoxelBlock.Grass) && world.Version == version && world.EditCount == 0,
            "the bottom layer, cells outside the world and no-op edits are refused");
        Check(world.SetBlock(10, 10, top, VoxelBlock.Air) && world.Version == version + 1 && world.EditCount == 1 &&
              world.Raycast(above, -Vector3.UnitZ, 50, out hit) && hit.Block == new VoxelCoord(10, 10, top - 1),
            "digging a block lets the ray through to the one below");

        var dirty = new List<int>();
        world.TakeDirtyChunks(dirty);
        Check(dirty.SequenceEqual(new[] { world.ChunkIndex(0, 0, 0) }), "an edit marks its chunk for remeshing");
        world.SetBlock(31, 5, 5, VoxelBlock.Brick);
        world.TakeDirtyChunks(dirty);
        Check(dirty.OrderBy(i => i).SequenceEqual(new[] { world.ChunkIndex(0, 0, 0), world.ChunkIndex(1, 0, 0) }) &&
              world.DirtyChunkCount == 0,
            "an edit on a chunk border also remeshes the neighbour, and taking the list clears it");

        int removed = world.FillSphere(world.BlockCenter(new VoxelCoord(20, 20, 1)), 3, VoxelBlock.Air);
        Check(removed > 20 && world.IsSolid(20, 20, 0) && !world.IsSolid(20, 20, 1), "a sphere fill digs a crater but keeps the bottom layer");
        Check(world.FillBox(new VoxelCoord(40, 40, top + 1), new VoxelCoord(42, 41, top + 3), VoxelBlock.Planks) == 18,
            "a box fill builds every cell between its corners");

        VoxelWorld half = VoxelGenerator.Generate(Small(block: 0.5f));
        Check(half.Origin == new Vector3(-16, -16, 0) &&
              half.Raycast(new Vector3(0.1f, 0.1f, 30), -Vector3.UnitZ, 50, out hit) &&
              Math.Abs(hit.Point.Z - (half.SurfaceHeight(32, 32) + 1) * 0.5f) < 1e-4f,
            "half-metre blocks make a world half the size, and raycasts work in metres");
    }

    private static VoxelWorld Empty(int size = 64) => new VoxelWorld(Small(size: size));

    private static bool WoundClockwiseFromOutside(Vector3[] positions, int[] indices, Func<int, Vector3> outward)
    {
        for (int t = 0; t < indices.Length; t += 3)
        {
            Vector3 a = positions[indices[t]], b = positions[indices[t + 1]], c = positions[indices[t + 2]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward(t)) >= 0) return false;
        }
        return true;
    }

    private static void CheckMeshing()
    {
        VoxelWorld world = Empty();
        world.SetBlock(5, 5, 5, VoxelBlock.Stone);
        VoxelChunkMesh mesh = VoxelMesher.Build(world, 0);
        Check(mesh.TriangleCount == 12 && mesh.ColliderIndices.Length == 36 && mesh.Parts.Length == 1 &&
              mesh.Parts[0].Surface == VoxelSurface.Stone && mesh.Vertices.Length == 24,
            "a lone block is six faces of its surface, for drawing and for collision");
        Vector3[] positions = mesh.Vertices.Select(v => v.Position).ToArray();
        Check(WoundClockwiseFromOutside(positions, mesh.Indices, t => mesh.Vertices[mesh.Indices[t]].Normal) &&
              WoundClockwiseFromOutside(mesh.ColliderVertices, mesh.ColliderIndices,
                  t => (mesh.ColliderVertices[mesh.ColliderIndices[t]] + mesh.ColliderVertices[mesh.ColliderIndices[t + 1]] +
                        mesh.ColliderVertices[mesh.ColliderIndices[t + 2]]) / 3 - world.BlockCenter(new VoxelCoord(5, 5, 5))),
            "triangles wind clockwise seen from outside, like the engine's models (culling and one-sided BEPU)");
        Check(mesh.Vertices.All(v => Math.Abs(Vector3.Dot(v.Tangent, v.Normal)) < 1e-5f && Math.Abs(Vector3.Dot(v.Binormal, v.Normal)) < 1e-5f),
            "every vertex has a tangent frame across its face for the normal maps");
        VoxelVertex side = mesh.Vertices.First(v => v.Normal == Vector3.UnitX && v.Position.Z == world.Origin.Z + 6);
        Check(Math.Abs(side.TextureCoordinate.Y + 6) < 1e-5f && side.Binormal == -Vector3.UnitZ,
            "side faces run their texture top-up (V = -Z), so a grass fringe sits at the top");

        world.SetBlock(6, 5, 5, VoxelBlock.Stone);
        mesh = VoxelMesher.Build(world, 0);
        Check(mesh.TriangleCount == 12 && mesh.ColliderIndices.Length == 36,
            "two blocks of a kind merge into one box: the hidden faces between them are dropped");
        world.SetBlock(6, 5, 5, VoxelBlock.Brick);
        mesh = VoxelMesher.Build(world, 0);
        Check(mesh.TriangleCount == 20 && mesh.ColliderIndices.Length == 36 && mesh.Parts.Length == 2,
            "different blocks keep their own faces for drawing, but collision merges them");

        world = Empty();
        world.SetBlock(5, 5, 5, VoxelBlock.Grass);
        mesh = VoxelMesher.Build(world, 0);
        Check(mesh.Parts.Select(p => p.Surface).OrderBy(s => s)
                  .SequenceEqual(new[] { VoxelSurface.GrassTop, VoxelSurface.GrassSide, VoxelSurface.Dirt }) &&
              mesh.Parts.Single(p => p.Surface == VoxelSurface.GrassSide).PrimitiveCount == 8,
            "grass shows grass on top, a grass fringe on its sides and dirt underneath");

        world = Empty();
        world.SetBlock(31, 5, 5, VoxelBlock.Stone);
        world.SetBlock(32, 5, 5, VoxelBlock.Stone);
        Check(VoxelMesher.Build(world, world.ChunkIndex(0, 0, 0)).TriangleCount == 10 &&
              VoxelMesher.Build(world, world.ChunkIndex(1, 0, 0)).TriangleCount == 10,
            "faces between blocks in neighbouring chunks are hidden in both chunks");

        VoxelWorld flat = VoxelGenerator.Generate(Small(size: 32, height: 16));
        mesh = VoxelMesher.Build(flat, 0);
        Check(mesh.Vertices.All(v => v.Normal != -Vector3.UnitZ) && mesh.ColliderIndices.Length / 3 <= 12 &&
              mesh.Bounds.Contains(flat.Bounds) != ContainmentType.Disjoint,
            "the world's underside is never drawn, and a flat chunk collides as a handful of large faces");
    }

    private static void CheckTextures()
    {
        foreach (VoxelSurface surface in Enum.GetValues<VoxelSurface>())
        {
            var albedo = new Color[VoxelTextures.Size * VoxelTextures.Size];
            var normal = new Color[albedo.Length];
            VoxelTextures.Paint(surface, albedo, normal);
            var again = new Color[albedo.Length];
            VoxelTextures.Paint(surface, again, new Color[albedo.Length]);
            float upright = normal.Average(c => c.B / 255f * 2 - 1);
            Check(albedo.SequenceEqual(again) && albedo.Distinct().Count() > 8 && upright > 0.6f,
                $"{surface} is painted the same every time, with detail, and a normal map that faces out");
        }
        var grassSide = new Color[VoxelTextures.Size * VoxelTextures.Size];
        VoxelTextures.Paint(VoxelSurface.GrassSide, grassSide, new Color[grassSide.Length]);
        double Green(int row) => Enumerable.Range(0, VoxelTextures.Size).Average(x => grassSide[x + row * VoxelTextures.Size].G - grassSide[x + row * VoxelTextures.Size].R);
        Check(Green(1) > 30 && Green(VoxelTextures.Size - 2) < 0, "the grass side is green along its top and dirt below");
        Check(VoxelSurfaceStyle.For(VoxelSurface.Lamp).Type == MaterialEffect.MaterialTypes.Emissive && VoxelSurfaceStyle.For(VoxelSurface.Lamp).Glow > 0 &&
              VoxelSurfaceStyle.For(VoxelSurface.GrassTop).Type == MaterialEffect.MaterialTypes.WetSurface,
            "lamps glow, and ground surfaces use the Wet Surface type so rain soaks them");
    }

    private static void CheckSystemAndColliders()
    {
        using var physics = new PhysicsSystem(new Vector3(0, 0, -9.81f));
        var system = new VoxelSystem(null, null, physics);
        var scene = new Scene();
        scene.Environment = new EnvironmentSettings
        {
            Voxels = true, VoxelTerrain = VoxelTerrain.Flat, VoxelWorldSize = 64, VoxelWorldHeight = 32,
            VoxelBaseHeight = -10, VoxelCaves = false, VoxelTrees = 0, VoxelSeaLevel = 0,
        };

        system.Update(scene, playing: true);
        VoxelWorld world = system.World;
        Check(world != null && system.Generation == 1 && !system.IsGenerating && system.VisibleChunkCount == 4 &&
              system.ColliderTriangleCount > 0 && system.TriangleCount > 0,
            "during Play the world is generated, meshed and given colliders at once");
        int top = world!.SurfaceHeight(40, 40);
        float surfaceZ = world.Origin.Z + (top + 1);
        Vector3 above = world.BlockCenter(new VoxelCoord(40, 40, top)) + new Vector3(0, 0, 20);
        Check(physics.RayCast(above, -Vector3.UnitZ, 100, null, null, out float distance, out Vector3 normal, out _, out var hitStatic) &&
              hitStatic != null && Math.Abs(above.Z - distance - surfaceZ) < 1e-3f && normal.Z > 0.99f,
            "physics raycasts hit the chunk colliders' top faces (BEPU triangles are one-sided)");
        Check(!physics.RayCast(new Vector3(above.X, above.Y, surfaceZ - 0.5f), Vector3.UnitZ, 1, null, null, out _, out _, out _, out _),
            "from inside the ground the faces are see-through, as one-sided triangles should be");

        BepuPhysics.BodyHandle box = physics.AddDynamicBox(above, 1, 1, 1, 10);
        for (int i = 0; i < 180; i++) physics.Step(Dt);
        physics.GetBodyPose(box, out Vector3 rest, out _);
        Check(Math.Abs(rest.Z - (surfaceZ + 0.5f)) < 0.05f, $"a dropped box comes to rest on the blocks ({rest.Z:0.00} vs {surfaceZ + 0.5f:0.00})");
        physics.RemoveDynamic(box);

        world.SetBlock(40, 40, top, VoxelBlock.Air);
        Check(world.DirtyChunkCount == 1, "a script edit waits for the next update");
        system.Update(scene, playing: true);
        Check(world.DirtyChunkCount == 0 &&
              physics.RayCast(above, -Vector3.UnitZ, 100, null, null, out distance, out _, out _, out _) &&
              Math.Abs(above.Z - distance - (surfaceZ - 1)) < 1e-3f,
            "the next update remeshes the chunk and rebuilds its collider: the ray falls into the hole");

        system.OnPlayStopped();
        system.Update(scene, playing: true);
        Check(system.World != world && system.World.EditCount == 0 && system.Generation == 2 &&
              system.World.IsSolid(40, 40, top), "Stop regenerates a world that Play changed");

        scene.Environment.VoxelSeed = 5;
        scene.Environment.VoxelTerrain = VoxelTerrain.Hills;
        system.Update(scene, playing: false);
        Check(system.IsGenerating && system.World != null && system.World.Settings.Terrain == VoxelTerrain.Flat,
            "while editing, a settings change builds on a worker thread and the old world stays meanwhile");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (system.IsGenerating && timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            Thread.Sleep(5);
            system.Update(scene, playing: false);
        }
        Check(!system.IsGenerating && system.World!.Settings.Terrain == VoxelTerrain.Hills && system.World.Settings.Seed == 5 &&
              system.Generation == 3, "the new world replaces the old one when it is ready");

        scene.Environment.VoxelSeed = 6;
        system.Update(scene, playing: false);
        scene.Environment.VoxelSeed = 7;
        system.Update(scene, playing: true);
        Check(system.World!.Settings.Seed == 7 && !system.IsGenerating, "Play waits for (or replaces) a world still being built");

        scene.Environment.VoxelCollision = false;
        system.Update(scene, playing: true);
        Check(system.World.Settings.Collision == false &&
              !physics.RayCast(above, -Vector3.UnitZ, 100, null, null, out _, out _, out _, out _),
            "Collision off builds the world without colliders");

        scene.Environment.VoxelCollision = true;
        system.Update(scene, playing: true);
        scene.Environment.Voxels = false;
        system.Update(scene, playing: true);
        Check(system.World == null && system.TriangleCount == 0 &&
              !physics.RayCast(above, -Vector3.UnitZ, 100, null, null, out _, out _, out _, out _),
            "switching the voxel world off removes it and its colliders");

        scene.Environment.Voxels = true;
        system.Update(scene, playing: true);
        system.Update(new Scene(), playing: true);
        Check(system.World == null && !physics.RayCast(above, -Vector3.UnitZ, 100, null, null, out _, out _, out _, out _),
            "another scene without a voxel world drops the old one");
        system.Dispose();

        // Through MainSceneLogic: scripts get the world as soon as Play starts.
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        try
        {
            var logic = new MainSceneLogic { Voxels = new VoxelSystem(null, null, null) };
            logic.PlayMode = new PlayModeController(logic);
            GameFlow.SceneLogic = logic;
            Check(logic.VoxelWorld == null, "a scene without a voxel world gives scripts none");
            logic.ActiveScene.Environment.Voxels = true;
            logic.ActiveScene.Environment.VoxelWorldSize = 32;
            logic.ActiveScene.Environment.VoxelWorldHeight = 16;
            Check(logic.VoxelWorld == null, "while editing, asking for the world does not build it");
            logic.PlayMode.Play();
            Check(logic.VoxelWorld is { SizeX: 32 }, "in Play, ScriptBehaviour.Voxels builds a missing world on demand");
            logic.PlayMode.Stop();
        }
        finally
        {
            GameFlow.SceneLogic = previousLogic;
        }
    }

    private static void CheckEditor()
    {
        var logic = new MainSceneLogic();
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), new Assets() });
        var vm = new EnvironmentViewModel();
        vm.AttachBridge(bridge);
        Check(vm.VoxelTerrains.SequenceEqual(new[] { "Flat", "Hills", "Mountains", "Islands" }) && !vm.VoxelsEnabled &&
              vm.SelectedVoxelTerrain == 1, "the Environment Inspector lists the voxel terrains, with the world off");

        vm.VoxelsEnabled = true;
        vm.SelectedVoxelTerrain = 3;
        vm.VoxelSeed = 99;
        vm.VoxelWorldSize = 192;
        vm.VoxelWorldHeight = 96;
        vm.VoxelBlockSize = 0.5;
        vm.VoxelBaseHeight = -40;
        vm.VoxelCaves = false;
        vm.VoxelTrees = 0.8;
        vm.VoxelSeaLevel = 0.45;
        vm.VoxelCollision = false;
        Check(!logic.ActiveScene.Environment.Voxels, "voxel edits wait for the game thread");
        Drain(bridge);
        var s = logic.ActiveScene.Environment;
        Check(s.Voxels && s.VoxelTerrain == VoxelTerrain.Islands && s.VoxelSeed == 99 && s.VoxelWorldSize == 192 &&
              s.VoxelWorldHeight == 96 && s.VoxelBlockSize == 0.5f && s.VoxelBaseHeight == -40 && !s.VoxelCaves &&
              Math.Abs(s.VoxelTrees - 0.8f) < 1e-6f && Math.Abs(s.VoxelSeaLevel - 0.45f) < 1e-6f && !s.VoxelCollision &&
              logic.ActiveScene.IsDirty,
            "Inspector voxel edits reach the scene and mark it dirty");
        Check(vm.VoxelSummary.StartsWith("192 × 192 × 96 blocks") && vm.VoxelSummary.Contains("96 × 96 × 48 m"),
            "the Inspector sums up the world's size in blocks and metres");

        vm.SelectedVoxelTerrain = -1;
        vm.VoxelWorldSize = 200;
        Drain(bridge);
        Check(logic.ActiveScene.Environment.VoxelTerrain == VoxelTerrain.Islands && logic.ActiveScene.Environment.VoxelWorldSize == 192,
            "a cleared ComboBox selection is ignored and sizes snap to whole chunks");
        decimal seed = vm.VoxelSeed;
        vm.NewVoxelSeedCommand.Execute(null);
        Drain(bridge);
        Check(vm.VoxelSeed != seed && logic.ActiveScene.Environment.VoxelSeed == (int)vm.VoxelSeed, "New seed picks and applies a random seed");

        bridge.EnqueueMutateEnvironment(e => { e.VoxelTerrain = VoxelTerrain.Mountains; e.Voxels = false; });
        Drain(bridge);
        typeof(EnvironmentViewModel).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        Check(vm.SelectedVoxelTerrain == 2 && !vm.VoxelsEnabled && logic.ActiveScene.Environment.VoxelTerrain == VoxelTerrain.Mountains,
            "the Inspector shows voxel settings changed elsewhere without pushing them back");
    }

    private static void CheckSampleScene()
    {
        var assets = new Assets
        {
            Capsule = new ModelDefinition(null, new BoundingBox(new Vector3(-0.5f, -0.5f, -1), new Vector3(0.5f, 0.5f, 1))),
            Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
            IsoSphere = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
        };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "VoxelTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        BasicEntity player = scene.BasicEntities.Single(e => e.Name == "Player");
        Check(player.PhysicsType == PhysicsBodyType.Dynamic &&
              player.GetComponents<ScriptBehaviourComponent>().Select(c => c.ScriptId).SequenceEqual(new[] { VoxelTestScript.ScriptId }),
            "the voxel sample has a dynamic Player capsule running the Voxel Test script");
        Check(VoxelWorldSettings.From(scene.Environment) == new VoxelWorldSettings(VoxelTerrain.Hills, 1337, 128, 64, 1, -24, true, 0.5f, 0.3f, true) &&
              scene.Environment.DayNightCycle && scene.DirectionalLights.Count == 1,
            "the voxel sample turns on a 128 x 128 x 64 world of hills with caves, trees and a sea, under the day/night sky");
        SceneList.Load();
        Check(SceneList.Find("VoxelTest") >= 0, "the voxel sample is in the scene list");
        string ui = GameInfo.ResolveContentFile("UI/VoxelTest");
        Check(File.Exists(ui + ".xml") && File.Exists(ui + ".css"), "the voxel sample's HUD ships with the scene");
        Check(ScriptRegistry.Find(VoxelTestScript.ScriptId)?.DisplayName == "Voxel Test", "the Voxel Test script is registered");

        // ---- Play with real physics: land on the hills, dig, build, blast, then stop ----
        KeyboardState keyboard = Input.keyboardState;
        MouseState mouse = Input.mouseState, lastMouse = Input.mouseLastState;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        bool editor = GameSettings.e_enableeditor, physicsOn = GameSettings.p_physics;
        using var physicsSystem = new PhysicsSystem(new Vector3(0, 0, -9.81f));
        try
        {
            GameSettings.e_enableeditor = false;
            GameSettings.p_physics = true;
            var logic = new MainSceneLogic { Voxels = new VoxelSystem(null, null, physicsSystem) };
            typeof(MainSceneLogic).GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(logic, assets);
            typeof(MainSceneLogic).GetField("_scenePhysics", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(logic, new ScenePhysics(physicsSystem) { FallbackGeometry = CollisionChecks.World.BoxGeometry });
            logic.SceneManager.Assets = assets;
            logic.PlayMode = new PlayModeController(logic);
            GameFlow.SceneLogic = logic;
            logic.SceneManager.SetActiveScene(scene);

            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));
            void Frames(int count, params Keys[] held)
            {
                Input.keyboardState = new KeyboardState(held);
                for (int i = 0; i < count; i++)
                {
                    GameInput.Update(Dt);
                    logic.PlayMode.UpdateScripts(frame);
                    logic.UpdatePhysics(Dt);
                    Input.mouseLastState = Input.mouseState;
                }
                Input.keyboardState = new KeyboardState();
            }
            void Click(bool left)
            {
                var released = new MouseState(500, 500, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
                Input.mouseLastState = Input.mouseState = released;
                Input.mouseState = left
                    ? new MouseState(500, 500, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released)
                    : new MouseState(500, 500, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
                Frames(1);
                Input.mouseState = released;
                Frames(1);
            }

            Click(left: false); // settle the mouse state before Play
            logic.PlayMode.Play();
            var script = (VoxelTestScript)typeof(ScriptBehaviourComponent).GetField("_instance", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(player.GetComponent<ScriptBehaviourComponent>())!;
            VoxelWorld world = logic.Voxels.World;
            Check(world != null && world.Settings.Seed == 1337, "Play builds the sample's world before the first step");
            Check(VoxelTestScript.SurfaceUnder(world, Vector3.Zero, out Vector3 ground) && Math.Abs(player.Position.Z - (ground.Z + 1.05f)) < 0.01f,
                "the player starts standing on the highest block under its feet at the world's centre");

            Frames(90);
            Check(Math.Abs(player.Position.Z - (ground.Z + 1)) < 0.1f,
                $"the capsule settles on the voxel ground ({player.Position.Z:0.00} vs {ground.Z + 1:0.00})");
            Check(scene.BasicEntities.Any(e => e.Name == "Block Cursor" && e.IsRuntimeSpawned), "a block cursor is spawned for Play");

            // Look further down (Down arrow): the crosshair lands on the ground just ahead.
            Frames(15, Keys.Down);
            Check(script.Target is { } target && target.Distance < script.Reach, "the crosshair finds a block within reach");
            VoxelCoord aimed = script.Target!.Value.Block;
            Click(left: true);
            Check(world!.GetBlock(aimed) == VoxelBlock.Air && script.Dug == 1, "left click digs the block under the crosshair");

            Frames(1, Keys.D9);
            Check(script.SelectedBlock == VoxelBlock.Brick, "9 picks brick from the hotbar");
            VoxelCoord cell = script.Target!.Value.Adjacent;
            Frames(1, Keys.F);
            Check(world.GetBlock(cell) == VoxelBlock.Brick && script.Placed == 1, "F builds the picked block against the face in view");

            Frames(31, Keys.W);
            Check(Math.Abs(player.Position.X) + Math.Abs(player.Position.Y) > 0.2f, "W walks over the blocks");

            int before = world.Count(VoxelBlock.Air);
            Frames(1, Keys.B);
            Check(world.Count(VoxelBlock.Air) > before + 20, "B blasts a crater");

            Frames(1, Keys.G);
            Frames(60);
            Check(scene.BasicEntities.Any(e => e.Name == "Ball 1" && e.DynamicBody != null), "G throws a physics ball onto the blocks");

            int generation = logic.Voxels.Generation;
            Frames(1, Keys.N);
            Check(logic.Voxels.Generation == generation + 1 && scene.Environment.VoxelSeed != 1337 &&
                  logic.Voxels.World.Settings.Seed == scene.Environment.VoxelSeed,
                "N generates a new world from a random seed straight away");

            logic.PlayMode.Stop();
            Check(scene.Environment.VoxelSeed == 1337 && !scene.BasicEntities.Any(e => e.IsRuntimeSpawned),
                "Stop puts back the seed and removes the cursor and balls");
            logic.Voxels.Update(scene, playing: true);
            Check(logic.Voxels.World.Settings.Seed == 1337 && logic.Voxels.World.EditCount == 0,
                "and the generated world comes back");
        }
        finally
        {
            Input.keyboardState = keyboard;
            Input.mouseState = mouse;
            Input.mouseLastState = lastMouse;
            GameInput.Update(0);
            GameFlow.SceneLogic = previousLogic;
            GameSettings.e_enableeditor = editor;
            GameSettings.p_physics = physicsOn;
        }
    }

    /// <summary>Uploads a world into a mesh library and draws a chunk: the top faces must survive back-face culling.</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var previousContent = Globals.content;
        Globals.content = content;
        try
        {
            var library = new MeshMaterialLibrary(graphics);
            using var physics = new PhysicsSystem(new Vector3(0, 0, -9.81f));
            using var system = new VoxelSystem(graphics, library, physics);
            var scene = new Scene();
            scene.Environment = new EnvironmentSettings
            {
                Voxels = true, VoxelTerrain = VoxelTerrain.Hills, VoxelWorldSize = 64, VoxelWorldHeight = 32, VoxelBaseHeight = 0,
                VoxelSeaLevel = 0.4f, VoxelTrees = 1,
            };
            system.Update(scene, playing: true);
            int registered = 0;
            for (int i = 0; i < library.Index; i++)
                for (int m = 0; m < library.MaterialLib[i].Index; m++) registered += library.MaterialLib[i].GetMeshLibrary()[m].Index;
            Check(system.World != null && registered == system.DrawPartCount + 1 && system.DrawPartCount >= system.VisibleChunkCount,
                "every chunk surface and the sea are registered in the mesh library");
            var materials = Enumerable.Range(0, library.Index).Select(i => library.MaterialLib[i].GetMaterial()).ToList();
            Check(materials.Any(m => m.Type == MaterialEffect.MaterialTypes.Water && m.IsTransparent && !m.HasShadow) &&
                  materials.Where(m => m.Type != MaterialEffect.MaterialTypes.Water).All(m => m.HasDiffuse && m.HasNormalMap),
                "chunk surfaces have painted albedo and normal maps; the sea uses the water material");

            // Draw the top of a flat world (solid down to its floor, so it has no undersides) from above,
            // with the engine's default culling and with the opposite one.
            VoxelWorld flat = VoxelGenerator.Generate(Small(size: 32, height: 16));
            VoxelChunkMesh mesh = VoxelMesher.Build(flat, 0);
            using var vertices = new VertexBuffer(graphics, VoxelVertex.Declaration, mesh.Vertices.Length, BufferUsage.WriteOnly);
            vertices.SetData(mesh.Vertices);
            using var indices = new IndexBuffer(graphics, IndexElementSize.ThirtyTwoBits, mesh.Indices.Length, BufferUsage.WriteOnly);
            indices.SetData(mesh.Indices);
            using var effect = new BasicEffect(graphics) { DiffuseColor = Vector3.One, LightingEnabled = false };
            effect.View = Matrix.CreateLookAt(new Vector3(0, -1, 30), Vector3.Zero, Vector3.UnitZ);
            effect.Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 100);
            using var target = new RenderTarget2D(graphics, 32, 32, false, SurfaceFormat.Color, DepthFormat.Depth24);

            int Lit(RasterizerState culling)
            {
                graphics.SetRenderTarget(target);
                graphics.Clear(Color.Black);
                graphics.RasterizerState = culling;
                graphics.DepthStencilState = DepthStencilState.Default;
                graphics.SetVertexBuffer(vertices);
                graphics.Indices = indices;
                effect.CurrentTechnique.Passes[0].Apply();
                graphics.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, mesh.TriangleCount);
                graphics.SetRenderTarget(null);
                var pixels = new Color[32 * 32];
                target.GetData(pixels);
                return pixels.Count(p => p.R > 128);
            }
            int front = Lit(RasterizerState.CullCounterClockwise), back = Lit(RasterizerState.CullClockwise);
            graphics.RasterizerState = RasterizerState.CullCounterClockwise;
            Check(front > 900 && back == 0,
                $"chunk faces are front-facing for the engine's culling ({front} lit pixels, {back} with the opposite culling)");
        }
        finally
        {
            Globals.content = previousContent;
        }
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
