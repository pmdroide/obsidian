using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;

/// <summary>Unity-style serialized fields on Script Behaviours: rules, storage, Play, save/load and the Inspector.</summary>
internal static class ScriptFieldChecks
{
    public static void Run()
    {
        ScriptRegistry.Register<FieldsScript>(FieldsScript.Id, "Test Fields");
        ScriptRegistry.Register<OtherFieldsScript>(OtherFieldsScript.Id, "Test Other Fields");

        // Which fields are serialized
        var fields = ScriptFields.For(FieldsScript.Id);
        Check(fields.Select(f => f.Name).SequenceEqual(new[]
              {
                  "BaseSpeed", "Speed", "_count", "Label", "Mode", "_offset", "_tint", "Flat", "Precise", "Grounded", "_secret", "_renamed",
              }),
            "public and [SerializeField] fields are serialized in declaration order, base class first; private, " +
            "[NonSerialized], readonly, static, const, properties and unsupported types are not");
        Check(fields.Single(f => f.Name == "_count").DisplayName == "Count" && ScriptFields.Nicify("m_moveSpeed") == "Move Speed" &&
              ScriptFields.Nicify("kMaxHP") == "Max HP" && ScriptFields.Nicify("jumpHeight2") == "Jump Height 2",
            "Inspector labels are nicified like Unity's");
        var speed = fields.Single(f => f.Name == "Speed");
        Check(speed.HasRange && speed.Min == 0 && speed.Max == 10 && speed.Tooltip == "Metres per second" &&
              (float)speed.DefaultValue == 2.5f && fields.Single(f => f.Name == "_secret").HideInInspector &&
              fields.Single(f => f.Name == "Mode").EnumNames.SequenceEqual(new[] { "Walk", "Run", "Fly" }),
            "Range, Tooltip, HideInInspector, enum names and C# defaults are read from the script");

        // Encoding
        var tint = fields.Single(f => f.Name == "_tint");
        var offset = fields.Single(f => f.Name == "_offset");
        Check(ScriptFields.Encode(tint, new Color(255, 128, 0, 64)).GetString() == "#FF800040" &&
              ScriptFields.Encode(offset, new Vector3(1, 2, 3)).GetRawText() == "[1,2,3]" &&
              ScriptFields.Encode(fields.Single(f => f.Name == "Mode"), FieldsScript.Moves.Fly).GetString() == "Fly" &&
              ScriptFields.TryDecode(tint, ScriptFields.Encode(tint, Color.Orange), out var decoded) && (Color)decoded == Color.Orange,
            "values are stored as readable JSON (colours as #RRGGBBAA, vectors as arrays, enums by name) and round-trip");
        Check(Throws(() => ScriptFields.Encode(speed, float.NaN)) && Throws(() => ScriptFields.Encode(speed, "fast")) &&
              (float)ScriptFields.Coerce(speed, 3) == 3f,
            "incompatible values are rejected; compatible ones (an int for a float field) are converted");

        // Component storage and Play
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One);
        logic.BasicEntities.Add(entity);
        var component = new ScriptBehaviourComponent { ScriptId = FieldsScript.Id };
        entity.AddComponent(component);
        component.SetField("Speed", 7);
        component.SetField("_count", 3);
        component.SetField("Mode", "Run");
        component.SetField("_offset", new Vector3(1, 2, 3));
        component.SetField("_tint", Color.Red);
        component.SetField("_secret", "hidden");
        Check(component.GetField<float>("Speed") == 7f && component.GetField<string>("Label") == "hello" &&
              Throws(() => component.SetField("Missing", 1)) && Throws(() => component.SetField("Speed", "fast")) &&
              Throws(() => component.SetFieldJson("_count", JsonSerializer.SerializeToElement(1.5))),
            "SetField stores values, GetField falls back to the default, and unknown fields or bad values throw");

        var play = new PlayModeController(logic);
        var frame = new GameTime(TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.25));
        play.Play();
        play.UpdateScripts(frame);
        var script = FieldsScript.Last!;
        Check(script.SpeedAtStart == 7f && script.CountAtStart == 3 && script.Mode == FieldsScript.Moves.Run &&
              script.Offset == new Vector3(1, 2, 3) && script.Tint == Color.Red && script.Secret == "hidden" && script.Label == "hello",
            "stored values are written into the script before Start; other fields keep their initializers");

        script.Label = "changed at runtime";
        component.SetField("Speed", 9f);
        play.UpdateScripts(frame);
        Check(script.Speed == 9f && script.Label == "changed at runtime",
            "an edit during Play reaches the running script without overwriting other runtime values");
        component.ResetField("Speed");
        play.UpdateScripts(frame);
        Check(script.Speed == 2.5f && !component.Fields.ContainsKey("Speed"),
            "Reset during Play gives the running script the default again");
        play.Stop();

        // Stale and renamed values
        component.Fields["_count"] = JsonSerializer.SerializeToElement("three");
        component.Fields["oldName"] = JsonSerializer.SerializeToElement(42);
        play.Play();
        play.UpdateScripts(frame);
        script = FieldsScript.Last!;
        Check(script.CountAtStart == 0 && script.Renamed == 42,
            "a stored value that no longer fits its field is skipped, and [FormerlySerializedAs] loads old names");
        play.Stop();
        component.SetField("_renamed", 5);
        component.SetField("_count", 3);
        Check(!component.Fields.ContainsKey("oldName") && component.Fields.ContainsKey("_renamed"),
            "editing a renamed field saves it under its new name");

        // Clone and save/load
        var copy = (ScriptBehaviourComponent)ComponentRegistry.Copy(component);
        copy.SetField("_count", 99);
        Check(component.GetField<int>("_count") == 3 && copy.GetField<int>("_count") == 99 && copy.GetField<Color>("_tint") == Color.Red,
            "cloned attachments copy their field values and edit them independently");

        string file = Path.Combine(Path.GetTempPath(), $"anvil-fields-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            string text = File.ReadAllText(file);
            var loaded = SceneSerialization.LoadFromFile(file, assets).BasicEntities.Single().GetComponent<ScriptBehaviourComponent>();
            Check(text.Contains("\"_tint\"") && text.Contains("#FF0000FF") &&
                  loaded.GetField<int>("_count") == 3 && loaded.GetField<Vector3>("_offset") == new Vector3(1, 2, 3) &&
                  loaded.GetField<FieldsScript.Moves>("Mode") == FieldsScript.Moves.Run && loaded.GetField<string>("_secret") == "hidden",
                "field values are saved in the scene file and load back");
        }
        finally { File.Delete(file); }

        // Inspector
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var editor = objects.Single(o => o.EngineId == entity.Id).Components.OfType<ScriptBehaviourComponentViewModel>().Single();
        var count = editor.Fields.OfType<ScriptNumberFieldViewModel>().Single(f => f.Name == "_count");
        var speedField = editor.Fields.OfType<ScriptRangeFieldViewModel>().Single(f => f.Name == "Speed");
        Check(editor.HasFields && editor.Fields.All(f => f.Name != "_secret") && count.Value == 3 && count.IsOverridden &&
              speedField.Value == 2.5 && !speedField.IsOverridden &&
              editor.Fields.OfType<ScriptEnumFieldViewModel>().Single().Value == "Run" &&
              editor.Fields.OfType<ScriptColorFieldViewModel>().Single().Value == Avalonia.Media.Colors.Red &&
              editor.Fields.OfType<ScriptVectorFieldViewModel>().Single(f => f.Name == "_offset") is { X: 1, Y: 2, Z: 3, HasZ: true } &&
              editor.Fields.OfType<ScriptVectorFieldViewModel>().Single(f => f.Name == "Flat") is { HasZ: false },
            "the Inspector lists the visible fields with stored values, defaults and overridden markers");

        speedField.SliderValue = 4;
        editor.Fields.OfType<ScriptBoolFieldViewModel>().Single().Value = true;
        editor.Fields.OfType<ScriptTextFieldViewModel>().Single().Value = "typed";
        editor.Fields.OfType<ScriptVectorFieldViewModel>().Single(f => f.Name == "_offset").Y = 5;
        editor.Fields.OfType<ScriptColorFieldViewModel>().Single().Value = Avalonia.Media.Colors.Blue;
        count.Value = 7.6;
        Publish();
        Check(component.GetField<float>("Speed") == 4f && component.GetField<bool>("Grounded") &&
              component.GetField<string>("Label") == "typed" && component.GetField<Vector3>("_offset") == new Vector3(1, 5, 3) &&
              component.GetField<Color>("_tint") == Color.Blue && component.GetField<int>("_count") == 8,
            "Inspector edits reach the engine component (ints are rounded)");

        count.ResetCommand.Execute(null);
        Publish();
        Check(!component.Fields.ContainsKey("_count") && count.Value == 0 && !count.IsOverridden,
            "the reset button removes the stored value and shows the default");

        count.Value = null;
        Publish();
        Check(!component.Fields.ContainsKey("_count"), "an emptied number box does not store anything");

        editor.SelectedScript = editor.AvailableScripts.Single(s => s.Id == OtherFieldsScript.Id);
        Check(editor.Fields.Select(f => f.Name).SequenceEqual(new[] { "Height" }),
            "picking another script shows its fields immediately");
        Publish();
        Check(component.ScriptId == OtherFieldsScript.Id && component.Fields.Count == 0,
            "switching scripts drops the previous script's values");

        // GameObject references
        ScriptRegistry.Register<ReferenceScript>(ReferenceScript.Id, "Test References");
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        GameFlow.SceneLogic = logic;
        try
        {
            var refFields = ScriptFields.For(ReferenceScript.Id);
            var targetInfo = refFields.Single(f => f.Name == "Target");
            Check(refFields.Select(f => f.Name).SequenceEqual(new[] { "Target", "_self" }) &&
                  refFields.All(f => f.Kind == ScriptFieldKind.GameObject) && (int)targetInfo.DefaultValue == 0,
                "public and [SerializeField] BasicEntity fields are serialized as gameobject references, defaulting to none");

            var target = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Target Cube" };
            var holder = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Holder" };
            logic.BasicEntities.Add(target);
            logic.BasicEntities.Add(holder);
            Check(ScriptFields.Encode(targetInfo, target).GetInt32() == target.Id &&
                  ScriptFields.Encode(targetInfo, null).ValueKind == JsonValueKind.Null &&
                  ScriptFields.TryDecode(targetInfo, JsonSerializer.SerializeToElement(target.Id), out var decodedId) && (int)decodedId == target.Id &&
                  !ScriptFields.TryDecode(targetInfo, JsonSerializer.SerializeToElement("Target Cube"), out _) &&
                  Throws(() => ScriptFields.Encode(targetInfo, "Target Cube")),
                "a gameobject is stored as its scene ID (null for none); names and other values are rejected");

            var refs = new ScriptBehaviourComponent { ScriptId = ReferenceScript.Id };
            holder.AddComponent(refs);
            refs.SetField("Target", target);
            refs.SetField("_self", holder);
            Check(refs.GetField<BasicEntity>("Target") == target && refs.GetField<BasicEntity>("_self") == holder,
                "SetField takes a gameobject and GetField returns the live one");

            play.Play();
            play.UpdateScripts(frame);
            var running = ReferenceScript.Last!;
            Check(running.TargetAtStart == target && running.Self == holder,
                "referenced gameobjects are in the script's fields before Start");
            refs.SetField("Target", null);
            play.UpdateScripts(frame);
            Check(running.Target == null, "clearing a reference during Play reaches the running script");
            play.Stop();

            refs.SetField("Target", target);
            var duplicate = (BasicEntity)holder.Clone;
            var duplicateRefs = duplicate.GetComponent<ScriptBehaviourComponent>();
            Check(ScriptFields.GetValue(refFields[1], duplicateRefs.Fields) is int selfId && selfId == duplicate.Id &&
                  ScriptFields.GetValue(targetInfo, duplicateRefs.Fields) is int targetId && targetId == target.Id &&
                  refs.GetField<BasicEntity>("_self") == holder,
                "duplicating a gameobject points its references to itself at the copy and keeps the others");

            file = Path.Combine(Path.GetTempPath(), $"anvil-refs-{Guid.NewGuid():N}.obsc");
            try
            {
                SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
                var loadedScene = SceneSerialization.LoadFromFile(file, assets);
                var loadedHolder = loadedScene.BasicEntities.Single(e => e.Name == "Holder");
                var loadedRefs = loadedHolder.GetComponent<ScriptBehaviourComponent>();
                int loadedTargetId = (int)ScriptFields.GetValue(targetInfo, loadedRefs.Fields);
                Check(loadedScene.BasicEntities.Single(e => e.Id == loadedTargetId).Name == "Target Cube" &&
                      (int)ScriptFields.GetValue(refFields[1], loadedRefs.Fields) == loadedHolder.Id,
                    "references are saved by ID and point at the same gameobjects after loading");
            }
            finally { File.Delete(file); }

            logic.EditorDelete(target.Id);
            Check(refs.GetField<BasicEntity>("Target") == null && refs.Fields.ContainsKey("Target"),
                "a reference to a deleted gameobject resolves to null but keeps its stored ID");
            logic.BasicEntities.Add(target);

            Publish();
            var refEditor = objects.Single(o => o.EngineId == holder.Id).Components.OfType<ScriptBehaviourComponentViewModel>().Single();
            var targetField = refEditor.Fields.OfType<ScriptGameObjectFieldViewModel>().Single(f => f.Name == "Target");
            Check(targetField.Value?.Id == target.Id && targetField.Value.Label == "Target Cube" && targetField.IsOverridden &&
                  targetField.Options.First() == ScriptGameObjectFieldViewModel.None &&
                  targetField.Options.Any(o => o.Id == holder.Id && o.Label == "Holder"),
                "the Inspector offers None plus the scene's gameobjects and shows the stored one");
            targetField.Value = targetField.Options.Single(o => o.Id == entity.Id);
            Publish();
            Check(refs.GetField<BasicEntity>("Target") == entity, "picking a gameobject in the Inspector reaches the engine");
            targetField.Value = ScriptGameObjectFieldViewModel.None;
            Publish();
            Check(refs.GetField<BasicEntity>("Target") == null && targetField.Value == ScriptGameObjectFieldViewModel.None,
                "picking None clears the reference");
            refs.SetField("Target", 987654);
            Publish();
            Check(targetField.Value?.Label == "Missing (#987654)", "a stored ID with no gameobject shows as Missing");

            // A scene's reference to its own copy of a persistent gameobject finds the carried one.
            var persistent = new PersistentGameObjects();
            var from = new Scene();
            var player = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Player", Persistent = true };
            from.BasicEntities.Add(player);
            var arriving = new Scene();
            var copyInNextScene = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Player", Persistent = true };
            arriving.BasicEntities.Add(copyInNextScene);
            persistent.TakeFrom(from, false, null);
            persistent.PrepareArrival(arriving);
            Check(persistent.ReplacementFor(copyInNextScene.Id) == player && !arriving.BasicEntities.Contains(copyInNextScene),
                "a reference to the arriving scene's left-out copy of a persistent gameobject resolves to the carried one");
        }
        finally { GameFlow.SceneLogic = previousLogic; }

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }

    public abstract class FieldsBase : ScriptBehaviour
    {
        public float BaseSpeed = 1f;
    }

    public sealed class FieldsScript : FieldsBase
    {
        public const string Id = "test-fields";
        public static FieldsScript? Last;
        public enum Moves { Walk, Run, Fly }

        [Range(0, 10), Tooltip("Metres per second")] public float Speed = 2.5f;
        [SerializeField] private int _count;
        public string Label = "hello";
        public Moves Mode;
        [SerializeField] private Vector3 _offset;
        [SerializeField] private Color _tint = Color.White;
        public Vector2 Flat;
        public double Precise = 0.5;
        public bool Grounded;
        [SerializeField, HideInInspector] private string _secret = "";
        [SerializeField, FormerlySerializedAs("oldName")] private int _renamed;

        private float _notSerialized = 1f;
        [NonSerialized] public int Skipped;
        public readonly int ReadOnlyValue = 1;
        public static int StaticValue;
        public const int ConstValue = 1;
        public int Property { get; set; }
        public List<int> Unsupported = new();

        public float SpeedAtStart { get; private set; }
        public int CountAtStart { get; private set; }
        public Vector3 Offset => _offset;
        public Color Tint => _tint;
        public string Secret => _secret;
        public int Renamed => _renamed;

        public override void Start()
        {
            Last = this;
            SpeedAtStart = Speed;
            CountAtStart = _count;
            _notSerialized += Skipped;
        }
    }

    public sealed class OtherFieldsScript : ScriptBehaviour
    {
        public const string Id = "test-other-fields";
        public float Height = 1f;
    }

    public sealed class ReferenceScript : ScriptBehaviour
    {
        public const string Id = "test-references";
        public static ReferenceScript? Last;
        public BasicEntity? Target;
        [SerializeField] private BasicEntity? _self;

        public BasicEntity? TargetAtStart { get; private set; }
        public BasicEntity? Self => _self;

        public override void Start()
        {
            Last = this;
            TargetAtStart = Target;
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
