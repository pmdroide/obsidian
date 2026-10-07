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

internal static class ScriptBehaviourChecks
{
    public static void Run()
    {
        ScriptRegistry.Register<CountingScript>("test-counting", "Test Counting");
        ScriptRegistry.Register<ThrowingScript>("test-throwing", "Test Throwing");
        ScriptRegistry.Register<RemovingScript>("test-removing", "Test Removing");
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One);
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Script Behaviour").AddCommand.Execute(null);
        Publish();
        var component = entity.GetComponent<ScriptBehaviourComponent>();
        var editor = owner.Components.OfType<ScriptBehaviourComponentViewModel>().Single();
        Check(entity.Components.Count == 1 && component.ScriptId == SpinExampleScript.ScriptId &&
            !component.IsRunning && owner.AddableComponents.Single(c => c.DisplayName == "Script Behaviour").AddCommand.CanExecute(null),
            "script behaviour defaults to the example, stays idle in Edit mode and allows additional attachments");
        editor.SelectedScript = editor.AvailableScripts.Single(s => s.Id == "test-counting");
        Check(component.ScriptId == SpinExampleScript.ScriptId, "script picker edits wait for the game thread");
        Publish();
        Check(component.ScriptId == "test-counting" && logic.ActiveScene.IsDirty, "script picker reaches its owning gameobject");
        editor.SelectedScript = null;
        Publish();
        Check(component.ScriptId == "test-counting", "transient empty script picker selection preserves the saved script");

        var clone = (BasicEntity)entity.Clone;
        var copied = clone.GetComponent<ScriptBehaviourComponent>();
        Check(copied.ScriptId == component.ScriptId && !ReferenceEquals(component, copied) && !copied.IsRunning,
            "cloning preserves script selection with independent runtime state");
        copied.ScriptId = SpinExampleScript.ScriptId;
        Check(component.ScriptId == "test-counting", "clone script edits leave the original unchanged");
        copied.ScriptId = component.ScriptId;
        logic.BasicEntities.Add(clone);

        string file = Path.Combine(Path.GetTempPath(), $"anvil-scripts-{Guid.NewGuid():N}.obsc");
        try
        {
            component.Enabled = false;
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var restored = SceneSerialization.LoadFromFile(file, assets).BasicEntities.First().GetComponent<ScriptBehaviourComponent>();
            Check(restored.ScriptId == component.ScriptId && !restored.Enabled && !restored.IsRunning,
                "scene save/load preserves script selection and Enabled without runtime state");
        }
        finally { File.Delete(file); component.Enabled = true; }

        var play = new PlayModeController(logic);
        var frame = new GameTime(TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.25));
        play.UpdateScripts(frame);
        Check(CountingScript.Instances.Count == 0, "Edit mode never creates or updates behaviours");
        play.Play();
        var first = CountingScript.Instances[0];
        var second = CountingScript.Instances[1];
        play.Play();
        play.UpdateScripts(frame);
        Check(first.Starts == 1 && first.Updates == 1 && first.LastDelta == 0.25f &&
            first.GameObject == entity && second.GameObject == clone && !ReferenceEquals(first, second),
            "Start runs once before Update with the correct owner, delta time and separate instances");
        var liveRecord = ComponentRegistry.Capture(component).Data;
        Check(!liveRecord.TryGetProperty("IsRunning", out _) && !liveRecord.TryGetProperty("LastError", out _),
            "script snapshots exclude transient runtime state");
        var liveCopy = (ScriptBehaviourComponent)ComponentRegistry.Copy(component);
        Check(!liveCopy.IsRunning, "cloning a running behaviour never copies its live script instance");

        editor.Enabled = false;
        Publish();
        play.UpdateScripts(frame);
        Check(first.Stops == 1 && first.Updates == 1 && !component.IsRunning, "disabling a behaviour stops it and prevents updates");
        editor.Enabled = true;
        Publish();
        play.UpdateScripts(frame);
        var resumed = CountingScript.Instances.Last();
        Check(resumed.GameObject == entity && resumed.Starts == 1 && resumed.Updates == 1,
            "re-enabling in Play starts a fresh instance before updating");
        entity.IsEnabled = false;
        play.UpdateScripts(frame);
        Check(resumed.Stops == 1 && !component.IsRunning, "disabled gameobjects stop their behaviours");
        entity.IsEnabled = true;
        play.UpdateScripts(frame);
        var restarted = CountingScript.Instances.Last();

        editor.SelectedScript = editor.AvailableScripts.Single(s => s.Id == SpinExampleScript.ScriptId);
        Publish();
        play.UpdateScripts(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Check(restarted.Stops == 1 && Math.Abs(entity.RotationMatrix.M12 - MathF.Sin(MathHelper.PiOver4)) < 0.0001f,
            "changing scripts in Play stops the old one and the example rotates 45 degrees in one second");
        play.Stop();

        Check(!component.IsRunning && entity.RotationMatrix == Matrix.Identity && second.Stops == 1,
            "Stop releases behaviours and restores the editor transform");
        play.Play();
        play.UpdateScripts(frame);
        Check(component.IsRunning, "a second Play session creates fresh behaviours");
        editor.RemoveCommand.Execute(null);
        Publish();
        Check(entity.GetComponent<ScriptBehaviourComponent>() == null && !component.IsRunning &&
            !owner.Components.OfType<ScriptBehaviourComponentViewModel>().Any(), "removing a running component stops its behaviour and removes the inspector editor");
        owner.AddableComponents.Single(c => c.DisplayName == "Script Behaviour").AddCommand.Execute(null);
        Publish();
        component = entity.GetComponent<ScriptBehaviourComponent>();
        component.ScriptId = "test-counting";
        play.UpdateScripts(frame);
        Check(component.IsRunning && CountingScript.Instances.Last().Starts == 1,
            "components attached during Play start before their first update");

        component.ScriptId = "missing-script";
        play.UpdateScripts(frame);
        string missingError = component.LastError;
        play.UpdateScripts(frame);
        Check(!component.IsRunning && missingError.Contains("not registered") && component.LastError == missingError,
            "unknown saved script IDs fail safely and remain available for correction");
        component.ScriptId = "test-throwing";
        play.UpdateScripts(frame);
        play.UpdateScripts(frame);
        Check(!component.IsRunning && component.LastError.Contains("Update failed") && ThrowingScript.Updates == 1 && copied.IsRunning,
            "a script exception is contained and stops repeated updates without stopping other objects");
        component.ScriptId = "test-removing";
        play.UpdateScripts(frame);
        Check(entity.GetComponent<ScriptBehaviourComponent>() == null && RemovingScript.Stops == 1,
            "scripts can remove their own component during Update without invalidating the frame iteration");
        play.Stop();

        CheckMultipleScripts(assets);
        CheckBuiltInHelpers(assets);
        CheckScriptAsset(bridge);

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckScriptAsset(EditorBridge bridge)
    {
        var vm = new MainWindowViewModel();
        typeof(MainWindowViewModel).GetField("_bridge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, bridge);
        typeof(MainWindowViewModel).GetMethod("RefreshAssetTree", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);
        var scripts = vm.AssetTree.Single(n => n.Name == "Scripts");
        var example = scripts.Children.Single(n => n.Name == "SpinExampleScript.cs");
        Check(example.Kind == AssetKind.Script && example.RelativePath == "Scripts/SpinExampleScript.cs" &&
            MainWindowViewModel.IsTextAsset(example) && File.Exists(Path.Combine(bridge.ContentSourceRoot, example.RelativePath)),
            "the real compiled example appears in Assets/Scripts and uses the existing double-click text editor route");
        Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Content", "Scripts", "SpinExampleScript.cs")),
            "script source assets are copied into build output");
    }

    private static void CheckMultipleScripts(Assets assets)
    {
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One);
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic { SelectedObject = entity }, assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        var add = owner.AddableComponents.Single(c => c.DisplayName == "Script Behaviour").AddCommand;
        add.Execute(null);
        add.Execute(null);
        add.Execute(null);
        Publish();
        var components = entity.GetComponents<ScriptBehaviourComponent>().ToArray();
        var editors = owner.Components.OfType<ScriptBehaviourComponentViewModel>().ToArray();
        Check(components.Length == 3 && editors.Length == 3 && add.CanExecute(null) &&
            components.Select(c => c.InstanceId).Distinct().Count() == 3 &&
            editors.Select(c => c.InstanceId).SequenceEqual(components.Select(c => c.InstanceId)),
            "one gameobject supports several script components with distinct inspector attachment identities");
        Check(!entity.AddComponent(components[0]), "the same component instance cannot be attached twice");
        editors[1].SelectedScript = editors[1].AvailableScripts.Single(s => s.Id == "test-counting");
        editors[2].Enabled = false;
        Publish();
        Check(components[0].ScriptId == SpinExampleScript.ScriptId && components[1].ScriptId == "test-counting" &&
            components[0].Enabled && components[1].Enabled && !components[2].Enabled,
            "editing a script or Enabled setting targets only that attachment on its gameobject");
        editors[0].SelectedScript = editors[0].AvailableScripts.Single(s => s.Id == "test-counting");
        Publish();

        var clone = (BasicEntity)entity.Clone;
        var copies = clone.GetComponents<ScriptBehaviourComponent>().ToArray();
        Check(copies.Length == 3 && copies.Select(c => c.ScriptId).SequenceEqual(components.Select(c => c.ScriptId)) &&
            copies.Select(c => c.Enabled).SequenceEqual(components.Select(c => c.Enabled)) &&
            !copies.Select(c => c.InstanceId).Intersect(components.Select(c => c.InstanceId)).Any(),
            "cloning copies every script attachment and assigns fresh identities");
        copies[1].ScriptId = "test-throwing";
        Check(components[1].ScriptId == "test-counting", "multiple-script clone edits remain independent");

        string file = Path.Combine(Path.GetTempPath(), $"anvil-multiple-scripts-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var loaded = SceneSerialization.LoadFromFile(file, assets).BasicEntities.Single().GetComponents<ScriptBehaviourComponent>().ToArray();
            Check(loaded.Length == 3 && loaded.Select(c => c.InstanceId).SequenceEqual(components.Select(c => c.InstanceId)) &&
                loaded.Select(c => c.ScriptId).SequenceEqual(components.Select(c => c.ScriptId)) && !loaded[2].Enabled,
                "all script attachments and their identities survive scene save/load");
            var json = JsonNode.Parse(File.ReadAllText(file))!;
            foreach (var record in json["Entities"]![0]!["Components"]!.AsArray()) record!.AsObject().Remove("InstanceId");
            File.WriteAllText(file, json.ToJsonString());
            loaded = SceneSerialization.LoadFromFile(file, assets).BasicEntities.Single().GetComponents<ScriptBehaviourComponent>().ToArray();
            Check(loaded.Length == 3 && loaded.All(c => c.InstanceId != Guid.Empty) &&
                loaded.Select(c => c.InstanceId).Distinct().Count() == 3,
                "older scene records without attachment IDs receive independent identities");
        }
        finally { File.Delete(file); }

        var play = new PlayModeController(logic);
        int instanceCount = CountingScript.Instances.Count;
        var frame = new GameTime(TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.25));
        play.Play();
        play.UpdateScripts(frame);
        var first = CountingScript.Instances[instanceCount];
        var second = CountingScript.Instances[instanceCount + 1];
        Check(first.GameObject == entity && second.GameObject == entity && !ReferenceEquals(first, second) &&
            first.Starts == 1 && second.Starts == 1 && first.Updates == 1 && second.Updates == 1 && !components[2].IsRunning,
            "two copies of a script on the same gameobject start and update independently");
        editors[1].Enabled = false;
        Publish();
        play.UpdateScripts(frame);
        Check(first.Updates == 2 && first.Stops == 0 && second.Updates == 1 && second.Stops == 1,
            "disabling the second script leaves the first script running");
        int failureCount = ThrowingScript.Updates;
        editors[2].SelectedScript = editors[2].AvailableScripts.Single(s => s.Id == "test-throwing");
        editors[2].Enabled = true;
        Publish();
        play.UpdateScripts(frame);
        play.UpdateScripts(frame);
        Check(ThrowingScript.Updates == failureCount + 1 && !components[2].IsRunning &&
            components[0].IsRunning && first.Updates == 4,
            "a failing script does not stop other script attachments on the same gameobject");
        editors[1].Enabled = true;
        Publish();
        play.UpdateScripts(frame);
        var resumed = CountingScript.Instances.Last();
        BridgeReconciler.IsInspectorFocused = true;
        BridgeReconciler.SelectedEngineId = entity.Id;
        try
        {
            editors[1].RemoveCommand.Execute(null);
            // A pending event from the removed editor must not fall back to another attachment.
            editors[1].Enabled = false;
            Publish();
            play.UpdateScripts(frame);
            Check(entity.GetComponents<ScriptBehaviourComponent>().Count() == 2 && resumed.Stops == 1 &&
                components[0].IsRunning && components[0].Enabled && first.Stops == 0 &&
                ReferenceEquals(owner.Components[0], editors[0]) && ReferenceEquals(owner.Components[1], editors[2]),
                "removing the middle script preserves neighbouring editors and ignores stale edits, even with inspector focus");
        }
        finally
        {
            BridgeReconciler.IsInspectorFocused = false;
            BridgeReconciler.SelectedEngineId = null;
            play.Stop();
        }
        Check(first.Stops == 1 && !components[0].IsRunning, "Stop releases remaining script attachments exactly once");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckBuiltInHelpers(Assets assets)
    {
        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.001f;

        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Helper" };
        var script = new HelperScript();
        script.Attach(entity);
        script.Tick(0.5f);

        script.Rotate(Vector3.UnitZ, 90);
        Check(Near(script.Right, Vector3.UnitY) && Near(script.Forward, -Vector3.UnitX) && Near(script.Up, Vector3.UnitZ),
            "Rotate turns around a world axis in degrees; Right/Forward/Up are the local +X/+Y/+Z axes");
        script.Translate(new Vector3(0, 2, 0), local: true);
        Check(Near(entity.Position, new Vector3(-2, 0, 0)), "Translate with local moves along the gameobject's own axes");
        script.Scale = new Vector3(2);
        Check(Near(script.TransformPoint(Vector3.UnitX), new Vector3(-2, 2, 0)) && Near(script.TransformDirection(Vector3.UnitX), Vector3.UnitY),
            "TransformPoint applies scale, rotation and position; TransformDirection only rotates");
        script.LookAt(script.Position + new Vector3(1, 1, 0));
        Check(Near(script.Forward, Vector3.Normalize(new Vector3(1, 1, 0))) && Near(script.Up, Vector3.UnitZ),
            "LookAt points Forward at the target and keeps Up on world Z");
        script.LookAt(script.Position + Vector3.UnitZ);
        Check(Near(script.Forward, Vector3.UnitZ) && Math.Abs(Vector3.Dot(script.Up, script.Forward)) < 0.001f,
            "LookAt straight up still builds a valid rotation");
        script.SetRotation(0, 0, 90);
        Check(Near(script.Forward, -Vector3.UnitX), "SetRotation takes world-axis angles in degrees");

        Check(!script.HasRigidbody && script.Velocity == Vector3.Zero && script.Physics == null,
            "physics helpers report no rigidbody without a Physics component");
        script.Velocity = Vector3.One;
        script.AddForce(Vector3.One);
        script.AddImpulse(Vector3.One);
        script.AddTorque(Vector3.One);
        Check(script.Velocity == Vector3.Zero && script.AngularVelocity == Vector3.Zero, "physics helpers are no-ops without a body");
        Check(script.PlaySound("Audio/missing.wav") == null && script.PlaySound3D("Audio/missing.wav") == null &&
            !script.PlayAudio() && !script.IsAudioPlaying, "audio helpers are null-safe without an audio device or Audio component");

        using var physics = new PhysicsSystem(new Vector3(0, 0, -9.81f));
        var scene = new ScenePhysics(physics);
        var owners = (Dictionary<int, BasicEntity>)typeof(ScenePhysics)
            .GetField("_bodyOwners", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
        var staticOwners = (Dictionary<int, BasicEntity>)typeof(ScenePhysics)
            .GetField("_staticOwners", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
        var body = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Body" };
        body.DynamicBody = physics.AddDynamicBox(Vector3.Zero, 1, 1, 1, 2);
        body.PhysicsScene = scene;
        owners[body.DynamicBody.Value.Value] = body;
        var wall = new BasicEntity(assets.Cube, null, new Vector3(10, 0, 0), Matrix.Identity, Vector3.One) { Name = "Wall" };
        wall.StaticBody = physics.AddStatic(wall.Position, Quaternion.Identity, physics.CreateBoxShape(2, 2, 2));
        staticOwners[wall.StaticBody.Value.Value] = wall;
        var mover = new HelperScript();
        mover.Attach(body);
        mover.Tick(0.5f);

        Check(mover.Raycast(Vector3.Zero, Vector3.UnitX * 5, 50, out var hit) && hit.GameObject == wall &&
            Math.Abs(hit.Distance - 9) < 0.01f && Near(hit.Point, new Vector3(9, 0, 0)) && Near(hit.Normal, -Vector3.UnitX),
            "Raycast ignores its own body and reports the hit gameobject, distance, point and normal");
        Check(!mover.Raycast(Vector3.Zero, -Vector3.UnitX, 50) && !mover.Raycast(Vector3.Zero, Vector3.UnitX, 5),
            "Raycast misses when nothing lies within range");

        mover.Velocity = Vector3.UnitX;
        Check(mover.HasRigidbody && Near(mover.Velocity, Vector3.UnitX) && mover.AngularVelocity == Vector3.Zero,
            "Velocity sets linear motion without touching spin");
        mover.AddImpulse(new Vector3(2, 0, 0));
        Check(Near(mover.Velocity, new Vector3(2, 0, 0)), "AddImpulse changes velocity by impulse / mass");
        mover.AddForce(new Vector3(4, 0, 0));
        Check(Near(mover.Velocity, new Vector3(3, 0, 0)), "AddForce applies force x DeltaTime for this frame");
        mover.AngularVelocity = Vector3.UnitZ;
        Check(Near(mover.AngularVelocity, Vector3.UnitZ) && Near(mover.Velocity, new Vector3(3, 0, 0)),
            "AngularVelocity sets spin without touching linear motion");
        mover.AddTorque(Vector3.UnitZ);
        float spin = mover.AngularVelocity.Z;
        mover.AddForceAtPosition(new Vector3(0, 4, 0), new Vector3(0.5f, 0, 0));
        Check(spin > 1 && mover.AngularVelocity.Z > spin && mover.Velocity.Y > 0,
            "AddTorque and off-centre AddForceAtPosition make the body spin");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    public sealed class CountingScript : ScriptBehaviour
    {
        public static readonly List<CountingScript> Instances = new();
        // Runtime counters, not settings: keep them out of serialization (and out of default-value probing).
        [NonSerialized] public int Starts, Updates, Stops;
        [NonSerialized] public float LastDelta;
        public CountingScript() => Instances.Add(this);
        public override void Start() => Starts++;
        public override void Update()
        {
            if (Starts != 1) throw new Exception("Update called before Start");
            Updates++;
            LastDelta = DeltaTime;
        }
        public override void Stop() => Stops++;
    }

    public sealed class HelperScript : ScriptBehaviour { }

    public sealed class ThrowingScript : ScriptBehaviour
    {
        public static int Updates;
        public override void Update() { Updates++; throw new Exception("intentional fixture failure"); }
    }

    public sealed class RemovingScript : ScriptBehaviour
    {
        public static int Stops;
        public override void Update() => GameObject.RemoveComponent(GameObject.GetComponent<ScriptBehaviourComponent>());
        public override void Stop() => Stops++;
    }
}
