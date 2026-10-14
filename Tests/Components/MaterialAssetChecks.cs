using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Anvil.Models;
using Anvil.Services;
using Anvil.ViewModels;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Material assets (.mat) without a graphics device: creating them, linking gameobjects, every linked gameobject
/// following file edits (Anvil's material Inspector, a linked Inspector, outside edits), unlinking, scene save/load and
/// Anvil's view models. <see cref="RunGraphics"/> checks that capturing a gameobject finds the textures it draws.
/// </summary>
internal static class MaterialAssetChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "obsidian-material-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MaterialAsset.SourceRootOverride = root;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        try { RunIn(root); }
        finally
        {
            MaterialAsset.SourceRootOverride = null;
            GameFlow.SceneLogic = previousLogic;
            try { Directory.Delete(root, recursive: true); } catch { /* temp folder */ }
        }
    }

    private static void RunIn(string root)
    {
        // ---- Files ----
        var brickSettings = new MaterialComponent
        {
            Red = 0.5f, Roughness = 0.7f, TilingU = 4, MaterialType = MaterialEffect.MaterialTypes.WetSurface,
            BaseColorTexture = "GameObjects/Textures/Ground/Bricks_BaseColor.jpg", NormalTexture = "",
            MaterialPath = "Materials/Ignored.mat", Enabled = false,
        };
        var brick = MaterialAsset.Create(null, "Brick", brickSettings);
        string file = Path.Combine(root, "Materials", "Brick.mat");
        Check(brick?.Path == "Materials/Brick.mat" && File.Exists(file), "Create writes Content/Materials/<name>.mat");
        var json = JsonNode.Parse(File.ReadAllText(file))!;
        Check(json["Version"]!.GetValue<int>() == MaterialAsset.CurrentVersion &&
              json["Material"]!["Red"]!.GetValue<float>() == 0.5f &&
              json["Material"]!["BaseColorTexture"]!.GetValue<string>() == "GameObjects/Textures/Ground/Bricks_BaseColor.jpg" &&
              json["Material"]!["NormalTexture"]!.GetValue<string>() == "" && json["Material"]!["RoughnessTexture"] == null &&
              json["Material"]!["MaterialPath"] == null && json["Material"]!["Enabled"]!.GetValue<bool>(),
            "the file holds the surface and texture slots (None as \"\", From model left out), never a link or Enabled");
        Check(MaterialAsset.UniquePath(null, "Brick") == "Materials/Brick_2.mat" &&
              MaterialAsset.UniquePath("Props", "a/b") == "Props/a_b.mat", "new materials get free, safe file names");
        Check(MaterialAsset.Load("Brick") == brick && MaterialAsset.Load("Materials/Brick") == brick &&
              MaterialAsset.Load("materials\\brick.mat")?.Path == "Materials/Brick.mat" && MaterialAsset.Load("Nope") == null,
            "materials load by name or path and stay cached until the file changes");
        MaterialAsset.Create("Props", "Metal", new MaterialComponent { Metallic = 1 });
        Check(MaterialAsset.List().SequenceEqual(new[] { "Materials/Brick.mat", "Props/Metal.mat" }) &&
              MaterialAsset.Exists("Props/Metal.mat") && !MaterialAsset.Exists("Metal"),
            "List finds materials in any Content folder, Materials first");

        // ---- Linking gameobjects ----
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };
        var logic = new MainSceneLogic();
        typeof(MainSceneLogic).GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(logic, assets);
        GameFlow.SceneLogic = logic;
        var wall = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Wall" };
        var floor = new BasicEntity(assets.Cube, null, Vector3.UnitX, Matrix.Identity, Vector3.One) { Name = "Floor" };
        floor.AddComponent(new MaterialComponent { Red = 1, Green = 0.1f, Enabled = false });
        var plain = new BasicEntity(assets.IsoSphere, null, Vector3.UnitY, Matrix.Identity, Vector3.One) { Name = "Plain" };
        plain.AddComponent(new MaterialComponent { Red = 0.3f });
        logic.BasicEntities.AddRange(new[] { wall, floor, plain });
        var entities = logic.BasicEntities;

        Check(MaterialAsset.Assign(wall, "Brick") && wall.GetComponent<MaterialComponent>() is
              { MaterialPath: "Materials/Brick.mat", Red: 0.5f, TilingU: 4, NormalTexture: "", Enabled: true } &&
              wall.GetComponent<MaterialComponent>()!.BaseColorTexture == "GameObjects/Textures/Ground/Bricks_BaseColor.jpg",
            "assigning a material adds a Material component showing the asset's surface");
        Check(MaterialAsset.Assign(floor, "Materials/Brick.mat") && floor.GetComponent<MaterialComponent>() is
              { MaterialPath: "Materials/Brick.mat", Red: 0.5f, Green: 1, Enabled: false } && floor.Components.Count == 1,
            "assigning to a gameobject with a Material component links that one (its Enabled stays)");
        Check(!MaterialAsset.Assign(plain, "Missing") && plain.GetComponent<MaterialComponent>()!.MaterialPath == null,
            "assigning a material that doesn't exist changes nothing");
        var scripted = new MaterialComponent { MaterialPath = "Brick" };
        var lamp = new BasicEntity(assets.Cube, null, Vector3.UnitZ, Matrix.Identity, Vector3.One) { Name = "Lamp" };
        lamp.AddComponent(scripted);
        logic.BasicEntities.Add(lamp);
        Check(scripted.MaterialPath == "Materials/Brick.mat" && scripted.Red == 0.5f,
            "a component added already linked (scripts, scene loads) takes the asset's surface");

        // ---- Following the asset ----
        var edited = MaterialAsset.Load("Brick")!.CreateSettings();
        edited.Red = 0.2f;
        edited.RoughnessTexture = "GameObjects/Textures/Ground/Bricks_Roughness.jpg";
        MaterialAsset.Write("Materials/Brick.mat", edited);
        MaterialAsset.SyncScene(entities);
        Check(wall.GetComponent<MaterialComponent>()!.Red == 0.2f && floor.GetComponent<MaterialComponent>()!.Red == 0.2f &&
              wall.GetComponent<MaterialComponent>()!.RoughnessTexture == "GameObjects/Textures/Ground/Bricks_Roughness.jpg" &&
              plain.GetComponent<MaterialComponent>()!.Red == 0.3f,
            "writing a material updates every linked gameobject on the next frame, and only those");

        wall.GetComponent<MaterialComponent>()!.EmissiveStrength = 3; // a script highlighting it at runtime
        for (int i = 0; i < 40; i++) MaterialAsset.SyncScene(entities);
        Check(wall.GetComponent<MaterialComponent>()!.EmissiveStrength == 3,
            "an unchanged asset never overwrites runtime changes to a linked component");

        var outside = JsonNode.Parse(File.ReadAllText(file))!;
        outside["Material"]!["Red"] = 0.9f;
        File.WriteAllText(file, outside.ToJsonString());
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
        for (int i = 0; i < 31; i++) MaterialAsset.SyncScene(entities);
        Check(wall.GetComponent<MaterialComponent>()!.Red == 0.9f && floor.GetComponent<MaterialComponent>()!.Red == 0.9f,
            "edits made outside Anvil (text editor, git) reach the linked gameobjects within half a second");

        var wallMaterial = wall.GetComponent<MaterialComponent>()!;
        wallMaterial.Metallic = 0.6f;
        MaterialAsset.SaveFrom(wallMaterial);
        MaterialAsset.SyncScene(entities);
        Check(MaterialAsset.Load("Brick")!.CreateSettings().Metallic == 0.6f && floor.GetComponent<MaterialComponent>()!.Metallic == 0.6f,
            "editing a linked component (Anvil's Inspector) saves the asset and the other gameobjects follow");
        Check(MaterialAsset.SaveFrom(plain.GetComponent<MaterialComponent>()!) == null && !File.Exists(Path.Combine(root, "Materials", "Ignored.mat")),
            "an unlinked component never writes a material file");

        floor.GetComponent<MaterialComponent>()!.MaterialPath = null;
        edited = MaterialAsset.Load("Brick")!.CreateSettings();
        edited.Red = 0.1f;
        MaterialAsset.Write("Materials/Brick.mat", edited);
        MaterialAsset.SyncScene(entities);
        Check(floor.GetComponent<MaterialComponent>()!.Red == 0.9f && wall.GetComponent<MaterialComponent>()!.Red == 0.1f,
            "an unlinked component keeps its values and stops following the asset");

        // ---- Scenes ----
        var restored = (MaterialComponent)ComponentRegistry.Restore(ComponentRegistry.Capture(wall.GetComponent<MaterialComponent>()!))!;
        Check(restored.MaterialPath == "Materials/Brick.mat" && restored.Red == 0.1f && restored.Metallic == 0.6f,
            "scenes save the link with the last values (shown when the file is missing)");
        var copy = (MaterialComponent)ComponentRegistry.Copy(wall.GetComponent<MaterialComponent>()!);
        Check(copy.MaterialPath == "Materials/Brick.mat", "duplicated gameobjects share the material");
        File.Delete(file);
        for (int i = 0; i < 31; i++) MaterialAsset.SyncScene(entities);
        Check(wall.GetComponent<MaterialComponent>()!.Red == 0.1f && !MaterialAsset.Exists("Materials/Brick.mat"),
            "a deleted material leaves its gameobjects as they were");
        MaterialAsset.Write("Materials/Brick.mat", edited);

        // ---- Capture ----
        var captured = MaterialAsset.Capture(plain);
        Check(captured.Red == 0.3f && captured.MaterialPath == null && captured.BaseColorTexture == "" && captured.NormalTexture == "",
            "capturing a gameobject takes its Material component's values; maps its model lacks become None");
        var fresh = MaterialAsset.CreateFrom(plain);
        Check(fresh?.Path == "Materials/Plain.mat" && plain.GetComponent<MaterialComponent>()!.MaterialPath == "Materials/Plain.mat",
            "Create Material from a gameobject saves Content/Materials/<name>.mat and links it");

        // ---- Anvil ----
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        void Drain() =>
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
        var objects = new ObservableCollection<SceneObjectViewModel>();
        for (int i = 0; i < 6; i++) Drain();
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var wallVm = objects.Single(o => o.EngineId == wall.Id);
        var info = wallVm.Material!;
        Check(info.IsLinked && info.MaterialPath == "Materials/Brick.mat" && info.MaterialAssetLabel == "Brick" &&
              !info.IsMaterialAssetMissing && info.Roughness == 0.7f,
            "the Inspector's Material component shows the linked asset");
        info.Roughness = 0.35;
        Drain();
        MaterialAsset.SyncScene(entities);
        Check(MaterialAsset.Load("Brick")!.CreateSettings().Roughness == 0.35f && lamp.GetComponent<MaterialComponent>()!.Roughness == 0.35f &&
              wall.GetComponent<MaterialComponent>()!.Roughness == 0.35f,
            "a linked component's Inspector edits write the material asset");
        info.UnlinkCommand.Execute(null);
        Drain();
        Check(wall.GetComponent<MaterialComponent>()!.MaterialPath == null && wall.GetComponent<MaterialComponent>()!.Roughness == 0.35f,
            "Unlink keeps the values as the gameobject's own");
        info.AssignAsset("Props/Metal.mat");
        Drain();
        Check(wall.GetComponent<MaterialComponent>() is { MaterialPath: "Props/Metal.mat", Metallic: 1 },
            "picking or dropping a material on the component links it");

        var vm = new MainWindowViewModel();
        vm.AttachBridge(bridge);
        var lampEntity = new BasicEntity(assets.Cube, null, Vector3.One, Matrix.Identity, Vector3.One) { Name = "Bare" };
        logic.BasicEntities.Add(lampEntity);
        for (int i = 0; i < 6; i++) Drain();
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        vm.AssignMaterialAsset(objects.Single(o => o.EngineId == lampEntity.Id), "Materials/Brick.mat");
        Drain();
        Check(lampEntity.GetComponent<MaterialComponent>() is { MaterialPath: "Materials/Brick.mat", Roughness: 0.35f } && logic.ActiveScene.IsDirty,
            "dropping a material on a Hierarchy gameobject adds a linked Material component");

        var assetVm = new MaterialAssetViewModel("Materials/Brick.mat");
        Check(assetVm.Name == "Brick" && assetVm.Roughness == 0.35f && assetVm.TextureSlots[0].Path == "GameObjects/Textures/Ground/Bricks_BaseColor.jpg" &&
              assetVm.TextureSlots[1].Label == "None" && !assetVm.IsLinked,
            "clicking a material in Assets shows its settings and texture slots in the Inspector");
        assetVm.Metallic = 0.25;
        assetVm.TextureSlots[3].Assign("GameObjects/Textures/UVChecker_BaseColor.png");
        assetVm.Save();
        MaterialAsset.SyncScene(entities);
        Check(MaterialAsset.Load("Brick")!.CreateSettings() is { Metallic: 0.25f, MetallicTexture: "GameObjects/Textures/UVChecker_BaseColor.png" } &&
              lampEntity.GetComponent<MaterialComponent>()!.Metallic == 0.25f && lamp.GetComponent<MaterialComponent>()!.Metallic == 0.25f,
            "editing the material asset saves the file and every gameobject using it follows");
        Check(assetVm.ReloadIfChanged() && !new Func<bool>(() => { File.Delete(Path.Combine(root, "Props", "Metal.mat")); return new MaterialAssetViewModel("Props/Metal.mat").ReloadIfChanged(); })(),
            "the material Inspector notices when its file is deleted");
    }

    /// <summary>Capturing a textured gameobject records the Content files of the textures it draws.</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var albedo = content.Load<Texture2D>("GameObjects/Tree/bark_albedo");
        var normal = content.Load<Texture2D>("GameObjects/Tree/bark_normal");
        Check(MaterialAsset.TexturePathOf(albedo) == "GameObjects/Tree/bark_albedo.png" && MaterialAsset.TexturePathOf(null) == "",
            "a content-loaded texture maps back to its Content source file");
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        using var basicEffect = new BasicEffect(graphics);
        var shown = new MaterialEffect(basicEffect);
        shown.Initialize(Color.White, 0.5f, 0f, albedo, normal);
        var entity = new BasicEntity(new ModelDefinition(null, bounds), shown, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Bark" };
        var captured = MaterialAsset.Capture(entity);
        Check(captured.BaseColorTexture == "GameObjects/Tree/bark_albedo.png" && captured.NormalTexture == "GameObjects/Tree/bark_normal.png" &&
              captured.MaskTexture == "",
            "Create Material from a gameobject keeps the textures it shows");
        var check = new MaterialEffect(basicEffect);
        captured.ApplyTo(check, content);
        Check(check.AlbedoMap == albedo && check.NormalMap == normal, "a captured material's textures load back");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
