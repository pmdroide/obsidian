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
using Engine.Physics;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;

/// <summary>
/// Prefabs without a graphics device: saving a gameobject as a .prefab, placing instances (editor and scripts),
/// overrides, Apply/Revert/Unpack, scenes following later prefab edits, Prefab script fields and Anvil's view models.
/// </summary>
internal static class PrefabChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "obsidian-prefab-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Prefab.SourceRootOverride = root;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        try { RunIn(root); }
        finally
        {
            Prefab.SourceRootOverride = null;
            GameFlow.SceneLogic = previousLogic;
            try { Directory.Delete(root, recursive: true); } catch { /* temp folder */ }
        }
    }

    private static void RunIn(string root)
    {
        if (ScriptRegistry.Find(PrefabTestScript.Id) == null)
            ScriptRegistry.Register<PrefabTestScript>(PrefabTestScript.Id, "Test Prefab Script");

        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };
        var logic = new MainSceneLogic();
        typeof(MainSceneLogic).GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(logic, assets);
        GameFlow.SceneLogic = logic;
        var editor = new EditorLogic();
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, editor, assets });
        void Drain() =>
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
        // Snapshots go out every few frames.
        void Publish()
        {
            for (int i = 0; i < 6; i++) Drain();
        }

        // ---- Create Prefab ----
        var crate = new BasicEntity(assets.Cube, null, new Vector3(10, 0, 0), Matrix.CreateRotationZ(0.5f), new Vector3(2))
            { Name = "Crate" };
        crate.AddComponent(new PhysicsComponent { BodyType = PhysicsBodyType.Dynamic, Mass = 5 });
        var crateScript = new ScriptBehaviourComponent { ScriptId = PrefabTestScript.Id };
        crate.AddComponent(crateScript);
        crateScript.SetField(nameof(PrefabTestScript.Speed), 3f);
        crateScript.SetField(nameof(PrefabTestScript.Self), crate.Id);
        logic.BasicEntities.Add(crate);

        string createdPath = null, createError = "not called";
        bridge.EnqueueCreatePrefab(crate.Id, null, (path, error) => { createdPath = path; createError = error; });
        Check(crate.PrefabPath == null, "Create Prefab waits for the game thread");
        Drain();
        string file = Path.Combine(root, "Prefabs", "Crate.prefab");
        Check(createdPath == "Prefabs/Crate.prefab" && createError == null && File.Exists(file) &&
              crate.PrefabPath == "Prefabs/Crate.prefab" && crate.IsPrefabInstance && logic.ActiveScene.IsDirty,
            "Create Prefab writes Content/Prefabs/<name>.prefab and links the gameobject to it");
        var json = JsonNode.Parse(File.ReadAllText(file))!;
        Check(json["Version"]!.GetValue<int>() == Prefab.CurrentVersion && json["GameObject"]!["ModelKey"]!.GetValue<string>() == "Cube" &&
              json["GameObject"]!["Position"]!.ToJsonString() == "[0,0,0]" && json["GameObject"]!["Prefab"] == null,
            "the prefab file holds the gameobject record at the origin, without a prefab link");
        Prefab prefab = Prefab.Load("Crate");
        Check(prefab != null && prefab.Name == "Crate" && prefab.ModelKey == "Cube" &&
              Prefab.Resolve("Prefabs/Crate") == prefab.Path && Prefab.Resolve("prefabs\\crate.prefab") == prefab.Path &&
              Prefab.Resolve("Missing") == null && Prefab.Load("Missing") == null,
            "prefabs load by name or Content path (extension and Prefabs folder optional)");
        Check(ReferenceEquals(Prefab.Load("Crate"), prefab), "a loaded prefab is cached until its file changes");

        var other = new BasicEntity(assets.IsoSphere, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Crate" };
        logic.BasicEntities.Add(other);
        bridge.EnqueueCreatePrefab(other.Id, "Props/Small", null);
        bridge.EnqueueCreatePrefab(other.Id, null, null);
        Drain();
        Check(File.Exists(Path.Combine(root, "Props", "Small", "Crate.prefab")) && other.PrefabPath == "Prefabs/Crate_2.prefab" &&
              File.Exists(Path.Combine(root, "Prefabs", "Crate_2.prefab")),
            "Create Prefab saves into the dropped-on folder and never overwrites another prefab (Crate_2)");
        Check(Prefab.List().SequenceEqual(new[] { "Prefabs/Crate.prefab", "Prefabs/Crate_2.prefab", "Props/Small/Crate.prefab" }),
            "Prefab.List finds every prefab in Content, Prefabs folder first");
        logic.EditorDelete(other.Id);

        // ---- Instantiate from the editor ----
        int? placedId = null;
        bridge.EnqueueInstantiatePrefab("Crate", new Vector3(0, 5, 0), id => placedId = id);
        Drain();
        BasicEntity b = logic.BasicEntities.Single(e => e.Id == placedId);
        var bScript = b.GetComponent<ScriptBehaviourComponent>();
        Check(b.Name == "Crate" && b.PrefabPath == prefab.Path && b.Position == new Vector3(0, 5, 0) && b.Scale == new Vector3(2) &&
              b.Physics is { BodyType: PhysicsBodyType.Dynamic, Mass: 5 } && !b.IsRuntimeSpawned &&
              Vector3.Distance(Vector3.Transform(Vector3.UnitX, b.RotationMatrix), Vector3.Transform(Vector3.UnitX, crate.RotationMatrix)) < 1e-4f,
            "placing a prefab copies its model, scale, rotation and components, at the spawn point");
        Check(bScript.GetField<float>(nameof(PrefabTestScript.Speed)) == 3f && bScript.InstanceId != crateScript.InstanceId,
            "the copy's script settings come along as its own attachment");
        Check(SelfId(bScript) == b.Id, "a script reference to the prefab's gameobject points at the copy");
        Check(ReferenceEquals(editor.SelectedObject, b), "a placed prefab is selected");
        Check(PrefabUtility.GetOverrides(b, assets).Count == 0 && PrefabUtility.GetOverrides(crate, assets).Count == 0,
            "fresh instances have no overrides");

        // ---- Overrides ----
        bridge.EnqueueMutateComponent(b.Id, PhysicsComponent.TypeId, c => ((PhysicsComponent)c).Mass = 9);
        Drain();
        var overrides = PrefabUtility.GetOverrides(b, assets);
        Check(overrides.SequenceEqual(new[] { "Components/physics#0/Mass" }) &&
              PrefabUtility.Describe(overrides[0]) == "Physics > Mass" &&
              PrefabUtility.Describe("Components/script-behaviour#1/Fields/Speed") == "Script Behaviour 2 > Speed" &&
              PrefabUtility.Describe("Material/DiffuseColor") == "Material > Diffuse Color",
            "a changed component setting is an override, labelled for the Inspector");
        b.Position += Vector3.UnitX;
        b.Name = "Crate (left)";
        Check(PrefabUtility.GetOverrides(b, assets).Count == 1, "name and position are the instance's own, never overrides");

        // ---- Apply ----
        crate.Scale = new Vector3(3);
        crateScript.SetField(nameof(PrefabTestScript.Speed), 4f);
        int applied = -2;
        string applyError = "not called";
        bridge.EnqueueApplyPrefab(crate.Id, (n, error) => { applied = n; applyError = error; });
        Drain();
        b = logic.BasicEntities.Single(e => e.Id == placedId);
        bScript = b.GetComponent<ScriptBehaviourComponent>();
        Check(applied == 1 && applyError == null && Prefab.Load("Crate")!.Record.Scale == new Vector3(3),
            "Apply writes the instance into the prefab and reports the other instances it changed");
        Check(b.Scale == new Vector3(3) && bScript.GetField<float>(nameof(PrefabTestScript.Speed)) == 4f &&
              b.Physics?.Mass == 9 && b.Name == "Crate (left)" && b.Position == new Vector3(1, 5, 0) && SelfId(bScript) == b.Id &&
              b.PrefabPath == prefab.Path,
            "other instances take the applied values but keep their overrides, name, position, self references and prefab link");
        Check(Prefab.Load("Crate")!.Record.Position == Vector3.Zero && Prefab.Load("Crate")!.Record.Name == "Crate",
            "Apply keeps the prefab at the origin under its own name");

        // ---- Revert ----
        bridge.EnqueueRevertPrefab(b.Id);
        Drain();
        b = logic.BasicEntities.Single(e => e.Id == placedId);
        Check(b.Physics?.Mass == 5 && PrefabUtility.GetOverrides(b, assets).Count == 0 && b.Name == "Crate (left)",
            "Revert drops the overrides and keeps the name and position");

        // ---- Scene save / load follows prefab edits ----
        bridge.EnqueueMutateComponent(b.Id, PhysicsComponent.TypeId, c => ((PhysicsComponent)c).Mass = 9);
        Drain();
        b = logic.BasicEntities.Single(e => e.Id == placedId);
        var gone = new BasicEntity(assets.Cube, null, new Vector3(-4, 0, 0), Matrix.Identity, new Vector3(0.5f))
            { Name = "Orphan", PrefabPath = "Prefabs/Gone.prefab" };
        logic.BasicEntities.Add(gone);
        var spawned = logic.InstantiatePrefab(prefab, Vector3.Zero, runtime: true);
        Check(spawned is { IsRuntimeSpawned: true, IsPrefabInstance: true }, "runtime copies are prefab instances flagged runtime-spawned");

        string scenePath = Path.Combine(root, "PrefabScene.obsc");
        SceneSerialization.SaveToFile(logic.ActiveScene, scenePath, assets);
        var sceneJson = JsonNode.Parse(File.ReadAllText(scenePath))!["Entities"]!.AsArray();
        JsonNode Saved(int id) => sceneJson.Single(e => e!["Id"]!.GetValue<int>() == id)!;
        Check(sceneJson.Count == 3 && Saved(b.Id)["Prefab"]!.GetValue<string>() == "Prefabs/Crate.prefab" &&
              Saved(b.Id)["PrefabOverrides"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(new[] { "Components/physics#0/Mass" }) &&
              Saved(crate.Id)["PrefabOverrides"]!.AsArray().Count == 0 &&
              Saved(gone.Id)["PrefabOverrides"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(new[] { PrefabUtility.AllOverridden }),
            "scenes save each instance's prefab and overrides (everything when the prefab is missing); runtime copies aren't saved");

        // The prefab changes on disk (another scene applied to it, or a teammate's commit).
        var doc = JsonNode.Parse(File.ReadAllText(file))!;
        doc["GameObject"]!["ModelKey"] = "IsoSphere";
        doc["GameObject"]!["Scale"] = new JsonArray(5, 5, 5);
        doc["GameObject"]!["Components"]![0]!["Data"]!["Mass"] = 7;
        File.WriteAllText(file, doc.ToJsonString());
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));

        Scene loaded = SceneSerialization.LoadFromFile(scenePath, assets);
        BasicEntity la = loaded.BasicEntities.Single(e => e.Id == crate.Id);
        BasicEntity lb = loaded.BasicEntities.Single(e => e.Id == b.Id);
        BasicEntity lg = loaded.BasicEntities.Single(e => e.Id == gone.Id);
        Check(ReferenceEquals(la.ModelDefinition, assets.IsoSphere) && la.Scale == new Vector3(5) && la.Physics?.Mass == 7 &&
              la.Position == crate.Position && la.Name == "Crate",
            "loading a scene gives instances the prefab's current model, scale and settings");
        Check(ReferenceEquals(lb.ModelDefinition, assets.IsoSphere) && lb.Scale == new Vector3(5) && lb.Physics?.Mass == 9 &&
              lb.Name == "Crate (left)" && lb.Position == b.Position && lb.PrefabPath == prefab.Path &&
              lb.GetComponent<ScriptBehaviourComponent>()!.InstanceId == b.GetComponent<ScriptBehaviourComponent>()!.InstanceId &&
              SelfId(lb.GetComponent<ScriptBehaviourComponent>()!) == lb.Id,
            "an instance keeps its overrides, transform, component IDs and self references when its prefab changed");
        Check(lg.Scale == new Vector3(0.5f) && lg.PrefabPath == "Prefabs/Gone.prefab", "an instance of a missing prefab loads its saved values");

        // ---- Unpack, edit-mode rules ----
        bridge.EnqueueUnpackPrefab(b.Id);
        Drain();
        b = logic.BasicEntities.Single(e => e.Id == placedId);
        Check(!b.IsPrefabInstance && b.Physics?.Mass == 9, "Unpack unlinks the instance and keeps its values");
        string unpackError = null;
        bridge.EnqueueApplyPrefab(b.Id, (_, error) => unpackError = error);
        Drain();
        Check(unpackError != null && unpackError.Contains("not a prefab instance"), "Apply refuses a gameobject that isn't an instance");
        logic.EditorDelete(spawned.Id);

        // ---- Script fields and Instantiate ----
        var info = ScriptFields.Find(PrefabTestScript.Id, nameof(PrefabTestScript.Bullet))!;
        Check(info.Kind == ScriptFieldKind.Prefab && info.DefaultValue == null &&
              ScriptFields.Encode(info, prefab).GetString() == prefab.Path &&
              ScriptFields.TryDecode(info, ScriptFields.Encode(info, "Prefabs\\Crate.prefab"), out var decoded) && (string)decoded == prefab.Path &&
              ReferenceEquals(ScriptFields.ToFieldValue(info, prefab.Path), Prefab.Load(prefab.Path)) &&
              ScriptFields.ToFieldValue(info, "Prefabs/Nope.prefab") == null,
            "a Prefab script field stores the prefab's path and gives the script the loaded prefab");
        var shooter = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Shooter" };
        var shooterScript = new ScriptBehaviourComponent { ScriptId = PrefabTestScript.Id };
        shooter.AddComponent(shooterScript);
        shooterScript.SetField(nameof(PrefabTestScript.Bullet), prefab);
        logic.BasicEntities.Add(shooter);
        int before = logic.BasicEntities.Count;
        shooterScript.OnStart(shooter);
        var instance = PrefabTestScript.LastSpawned;
        Check(instance != null && logic.BasicEntities.Count == before + 2 && instance.IsRuntimeSpawned && instance.Position == new Vector3(0, 0, 9) &&
              logic.BasicEntities.Count(e => e.IsRuntimeSpawned && e.PrefabPath == prefab.Path) == 2,
            "scripts Instantiate prefabs from an Inspector field or by name (never saved)");
        shooterScript.OnStop();
        Check(logic.BasicEntities.Count == before, "Instantiated copies go away when the script stops, like Spawn");

        // ---- Anvil ----
        var objects = new ObservableCollection<SceneObjectViewModel>();
        editor.SelectedObject = crate;
        Publish();
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var crateVm = objects.Single(o => o.EngineId == crate.Id);
        var plainVm = objects.Single(o => o.EngineId == b.Id);
        Check(crateVm.IsPrefabInstance && !crateVm.IsNotPrefabInstance && crateVm.PrefabName == "Crate" &&
              crateVm.PrefabOverrides != null && !plainVm.IsPrefabInstance && plainVm.IsNotPrefabInstance && plainVm.PrefabOverrides == null,
            "the Hierarchy/Inspector know which gameobjects are prefab instances; the selected one carries its overrides");
        // The prefab file changed under the selected instance (IsoSphere, scale 5): it now differs.
        Check(crateVm.HasPrefabOverrides && crateVm.PrefabStatus.EndsWith("overrides") && crateVm.PrefabOverridesTip.Contains("Model"),
            "the Inspector's prefab bar counts and lists the selected instance's overrides");
        gone.PrefabPath = "Prefabs/Gone.prefab";
        editor.SelectedObject = gone;
        Publish();
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        Check(objects.Single(o => o.EngineId == gone.Id) is { PrefabMissing: true, PrefabStatus: "Prefab file missing" },
            "a missing prefab file shows in the Inspector");

        var vm = new MainWindowViewModel();
        vm.AttachBridge(bridge);
        vm.CreatePrefabIn(plainVm, "Enemies");
        Drain();
        Check(b.PrefabPath == "Enemies/Crate (left).prefab" && File.Exists(Path.Combine(root, "Enemies", "Crate (left).prefab")),
            "dragging a Hierarchy gameobject into an Assets folder saves the prefab there");
        Check(MainWindowViewModel.FolderFor(null) == null &&
              MainWindowViewModel.FolderFor(new AssetNode { Kind = AssetKind.Folder, RelativePath = "Props" }) == "Props" &&
              MainWindowViewModel.FolderFor(new AssetNode { Kind = AssetKind.Prefab, RelativePath = "Props/Small/Crate.prefab" }) == "Props/Small",
            "a drop on a folder, on a file, or on empty space picks the prefab's folder");
        int count = logic.BasicEntities.Count;
        vm.InstantiatePrefabAsset("Props/Small/Crate.prefab");
        Drain();
        Check(logic.BasicEntities.Count == count + 1 && logic.BasicEntities[^1].PrefabPath == "Props/Small/Crate.prefab",
            "double-clicking (or dragging) a prefab asset places an instance");

        var scriptVm = new ScriptBehaviourComponentViewModel(crateVm);
        var fieldVm = (ScriptPrefabFieldViewModel)ScriptFieldViewModel.Create(scriptVm, info);
        fieldVm.Load(new Dictionary<string, System.Text.Json.JsonElement> { [info.Name] = ScriptFields.Encode(info, "Prefabs/Crate_2.prefab") });
        Check(fieldVm.Value?.Path == "Prefabs/Crate_2.prefab" && fieldVm.Options.Count == 5 && fieldVm.Options[0] == ScriptPrefabFieldViewModel.None &&
              fieldVm.Options.Any(o => o.Label == "Prefabs/Crate.prefab") && fieldVm.Options.Any(o => o.Label == "Props/Small/Crate.prefab") &&
              fieldVm.Options.Any(o => o.Label == "Crate (left)"),
            "the Inspector's Prefab field lists Content prefabs (full paths where names repeat), new ones included");
        fieldVm.Load(new Dictionary<string, System.Text.Json.JsonElement> { [info.Name] = ScriptFields.Encode(info, "Prefabs/Lost.prefab") });
        Check(fieldVm.Value?.Label == "Missing (Prefabs/Lost.prefab)", "a Prefab field whose file is gone shows Missing");
    }

    private static int SelfId(ScriptBehaviourComponent script) =>
        script.Fields.TryGetValue(nameof(PrefabTestScript.Self), out var json) && json.ValueKind == System.Text.Json.JsonValueKind.Number
            ? json.GetInt32() : 0;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    public sealed class PrefabTestScript : ScriptBehaviour
    {
        public const string Id = "test-prefab-script";
        public static BasicEntity? LastSpawned;
        public float Speed = 1f;
        public BasicEntity? Self;
        public Prefab? Bullet;

        public override void Start()
        {
            // Spawns only on the shooter (the one with a Bullet).
            if (Bullet == null) return;
            Instantiate(Bullet, new Vector3(0, 0, 9));
            LastSpawned = Instantiate("Crate", new Vector3(0, 0, 9));
        }
    }
}
