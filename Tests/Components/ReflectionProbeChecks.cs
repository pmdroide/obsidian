using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Lighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

internal static class ReflectionProbeChecks
{
    public static void Run()
    {
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var scene = new Scene();
        BasicEntity first = AddProbe(scene, assets.Cube, "First", Vector3.Zero);
        BasicEntity second = AddProbe(scene, assets.Cube, "Second", new Vector3(10, 0, 2));
        second.RotationMatrix = Matrix.CreateFromYawPitchRoll(0.43f, 0.72f, -0.18f);
        var firstProbe = first.GetComponent<ReflectionProbeComponent>();
        var secondProbe = second.GetComponent<ReflectionProbeComponent>();
        scene.ReflectionCaptures[firstProbe.InstanceId] = Data(first, 1);
        scene.ReflectionCaptures[secondProbe.InstanceId] = Data(second, 2);
        string folder = Path.Combine(Path.GetTempPath(), "obsidian-reflections-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Scene.obsc");
        string copyPath = Path.Combine(folder, "Copy.obsc");
        string sidecar = Path.ChangeExtension(path, ReflectionProbeData.Extension);
        try
        {
            // Association must use stable IDs, independent of entity order.
            scene.BasicEntities.Reverse();
            SceneSerialization.SaveToFile(scene, path, assets);
            var file = SceneSerialization.ReadFile(path);
            Check(file.Document.ReflectionDataFile == "Scene.reflections" &&
                  file.Bytes == new FileInfo(path).Length + new FileInfo(sidecar).Length,
                "saving a scene writes and references its reflection sidecar, counted in load progress");
            var loaded = SceneSerialization.LoadFromFile(path, assets);
            foreach (var entity in loaded.BasicEntities)
            {
                var component = entity.GetComponent<ReflectionProbeComponent>();
                var restored = loaded.ReflectionCaptures[component.InstanceId];
                var original = scene.ReflectionCaptures[component.InstanceId];
                Check(restored.Matches(entity, component) && Equal(restored, original),
                    $"{entity.Name}'s box and HDR data survive scene loading for all six faces and every mip");
            }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool cancelled = false;
                try { ReflectionProbeData.Load(sidecar, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "loading reflection sidecars honors scene-load cancellation");
            }

            SceneSerialization.SaveToFile(loaded, copyPath, assets);
            Check(SceneSerialization.ReadFile(copyPath).Document.ReflectionDataFile == "Copy.reflections" &&
                  SceneSerialization.LoadFromFile(copyPath, assets).ReflectionCaptures.Count == 2,
                "Save As writes an independent reflection sidecar");

            secondProbe.Enabled = false;
            SceneSerialization.SaveToFile(scene, path, assets);
            Check(SceneSerialization.LoadFromFile(path, assets).ReflectionCaptures.Count == 2,
                "disabled probes retain their saved cubemaps");

            first.Position += Vector3.UnitX;
            Check(!scene.ReflectionCaptures[firstProbe.InstanceId].Matches(first, firstProbe),
                "moving a probe invalidates its saved capture");
            first.Position -= Vector3.UnitX;
            firstProbe.Resolution = 128;
            Check(!scene.ReflectionCaptures[firstProbe.InstanceId].Matches(first, firstProbe),
                "changing resolution invalidates a saved capture");
            firstProbe.Resolution = 64;
            firstProbe.RequestCapture();
            SceneSerialization.SaveToFile(scene, path, assets);
            Check(SceneSerialization.LoadFromFile(path, assets).ReflectionCaptures.Count == 1,
                "saving before a requested recapture cannot persist the stale cube");

            scene.BasicEntities.Remove(second);
            BasicEntity spawned = AddProbe(scene, assets.Cube, "Runtime probe", Vector3.Zero);
            spawned.IsRuntimeSpawned = true;
            scene.ReflectionCaptures[spawned.GetComponent<ReflectionProbeComponent>().InstanceId] = Data(spawned, 4);
            SceneSerialization.SaveToFile(scene, path, assets);
            Check(SceneSerialization.ReadFile(path).Document.ReflectionDataFile == null &&
                  SceneSerialization.LoadFromFile(path, assets).ReflectionCaptures.Count == 0,
                "removed probes, runtime-spawned probes and unreferenced old sidecars are ignored");

            // Read the still-valid Save As scene with damaged, missing and unsupported sidecars.
            string copySidecar = Path.ChangeExtension(copyPath, ReflectionProbeData.Extension);
            byte[] bytes = File.ReadAllBytes(copySidecar);
            File.WriteAllBytes(copySidecar, bytes[..^1]);
            Check(SceneSerialization.LoadFromFile(copyPath, assets).ReflectionCaptures.Count == 0,
                "truncated reflection sidecars allow the scene to load and probes to recapture");
            File.WriteAllBytes(copySidecar, new byte[12]);
            Check(SceneSerialization.LoadFromFile(copyPath, assets).ReflectionCaptures.Count == 0,
                "unsupported reflection sidecars allow the scene to load");
            File.Delete(copySidecar);
            Check(SceneSerialization.LoadFromFile(copyPath, assets).ReflectionCaptures.Count == 0,
                "missing reflection sidecars allow the scene to load");

            scene.Clear();
            Check(scene.ReflectionCaptures.Count == 0, "clearing the scene releases its CPU reflection data");
        }
        finally { Directory.Delete(folder, true); }
    }

    public static void RunGraphics(GraphicsDevice graphics)
    {
        var model = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One));
        var scene = new Scene();
        var entity = AddProbe(scene, model, "Saved probe", Vector3.Zero);
        var component = entity.GetComponent<ReflectionProbeComponent>();
        var original = Data(entity, 3);
        scene.ReflectionCaptures[component.InstanceId] = original;
        using (var probes = new ReflectionProbes())
        {
            probes.Update(scene.BasicEntities, scene, graphics);
            Check(probes.NextCapture(Vector3.Zero) == null && probes.Active.Single().Cube != null,
                "restoring a saved GPU cubemap skips capture on the first frame");
            var probe = probes.Active.Single();
            probes.StoreCapture(probe, scene);
            Check(Equal(original, scene.ReflectionCaptures[component.InstanceId]),
                "GPU upload and capture readback preserve HDR bytes for every face and mip");
            component.RequestCapture();
            Check(probes.NextCapture(Vector3.Zero) == probe, "Capture still refreshes a restored GPU probe");
        }
        using var reloaded = new ReflectionProbes();
        component.Enabled = false;
        reloaded.Update(scene.BasicEntities, scene, graphics);
        component.Enabled = true;
        // The outstanding request must not be lost by disabling the probe or recreating its GPU manager.
        reloaded.Update(scene.BasicEntities, scene, graphics);
        Check(reloaded.NextCapture(Vector3.Zero) != null,
            "a capture requested before GPU restoration is honored");
    }

    private static BasicEntity AddProbe(Scene scene, ModelDefinition model, string name, Vector3 position)
    {
        var entity = new BasicEntity(model, null, position, Matrix.Identity, new Vector3(3, 4, 5))
            { Name = name, IsEnabled = true };
        entity.AddComponent(new ReflectionProbeComponent { Resolution = 64 });
        scene.BasicEntities.Add(entity);
        return entity;
    }

    private static ReflectionProbeData Data(BasicEntity entity, float red)
    {
        var component = entity.GetComponent<ReflectionProbeComponent>();
        const int levels = 7; // 64 through 1
        var data = new ReflectionProbeData { Resolution = 64, Box = ReflectionProbeComponent.BoxWorld(entity),
            CaptureRequest = component.CaptureRequest, FaceMips = new byte[6 * levels][] };
        for (int face = 0; face < 6; face++)
        for (int mip = 0; mip < levels; mip++)
        {
            int width = Math.Max(1, 64 >> mip);
            byte[] pixel = BitConverter.GetBytes(new HalfVector4(new Vector4(red, face + 1, mip + 1, 12.5f)).PackedValue);
            byte[] bytes = new byte[width * width * 8];
            for (int offset = 0; offset < bytes.Length; offset += 8) pixel.CopyTo(bytes, offset);
            data.FaceMips[face * levels + mip] = bytes;
        }
        return data;
    }

    private static bool Equal(ReflectionProbeData a, ReflectionProbeData b) => a.Box == b.Box &&
        a.Resolution == b.Resolution && a.FaceMips.Length == b.FaceMips.Length &&
        a.FaceMips.Zip(b.FaceMips).All(pair => pair.First.SequenceEqual(pair.Second));

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
