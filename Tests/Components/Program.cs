using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Anvil.Models;
using Anvil.Services;
using Anvil.Views;
using Anvil.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using System.Runtime.CompilerServices;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using GameComponent = Engine.Components.GameComponent;

// Integration checks without a graphics device or a test-framework dependency.
var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One)
    { IsEnabled = true, Name = "Audio object" };
var logic = new MainSceneLogic();
logic.BasicEntities.Add(entity);
var bridge = new EditorBridge();
var sceneEditor = new EditorLogic { SelectedObject = entity, IsGizmoSuppressed = true };
Invoke(bridge, "Bind", logic, sceneEditor, assets);

// Native asset drag/drop must preserve selection even with a cached viewport press.
// Exercise the engine input filter and the actual editor selection path together.
bool editorEnabled = GameSettings.e_enableeditor;
GameSettings.e_enableeditor = true;
bridge.SetHostedByEditor(true);
MouseFrame(Mouse(20, ButtonState.Released));
MouseFrame(Mouse(-20, ButtonState.Pressed));
MouseFrame(Mouse(20, ButtonState.Pressed));
Check(!Input.WasLMBClicked() && !Input.IsLMBPressed(), "panel-origin drag crossing viewport does not become a viewport click");
UpdateSceneEditor();
Check(ReferenceEquals(sceneEditor.SelectedObject, entity), "panel drag keeps the selected gameobject");
MouseFrame(Mouse(20, ButtonState.Released));

var mainVm = new MainWindowViewModel();
typeof(MainWindowViewModel).GetField("_bridge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(mainVm, bridge);
mainVm.SetAssetDragActive(true);
Check(bridge.IsHostDragDropActive, "asset drag guard reaches the engine synchronously");
Input.mouseLastState = Mouse(20, ButtonState.Released);
Input.mouseState = Mouse(20, ButtonState.Pressed);
UpdateSceneEditor();
Publish();
Check(bridge.SelectedId == entity.Id, "native asset drag keeps selection with a stale pressed mouse frame");
MouseFrame(Mouse(20, ButtonState.Pressed));
Check(!Input.IsLMBPressed(), "native asset drag suppresses viewport mouse input");
mainVm.SetAssetDragActive(false);
MouseFrame(Mouse(20, ButtonState.Pressed));
Check(!Input.WasLMBClicked() && !Input.IsLMBPressed(), "ending or cancelling asset drag suppresses held button until release");
UpdateSceneEditor();
Check(ReferenceEquals(sceneEditor.SelectedObject, entity), "inspector selection survives drag completion");
MouseFrame(Mouse(20, ButtonState.Released));
MouseFrame(Mouse(20, ButtonState.Pressed));
Check(Input.WasLMBClicked(), "fresh viewport clicks work after drag release");
UpdateSceneEditor();
Check(sceneEditor.SelectedObject == null, "intentional empty viewport click can still deselect");
MouseFrame(Mouse(-20, ButtonState.Pressed));
Check(Input.IsLMBPressed(), "viewport-origin camera or gizmo drag continues over panels");
MouseFrame(Mouse(20, ButtonState.Released));
sceneEditor.SelectedObject = entity;
bridge.SetHostedByEditor(false);
GameSettings.e_enableeditor = editorEnabled;

bridge.EnqueueAddComponent(entity.Id, AudioComponent.TypeId);
bridge.EnqueueAddComponent(entity.Id, AudioComponent.TypeId);
Publish();
Check(entity.Components.Count == 1 && logic.ActiveScene.IsDirty, "add is unique and marks scene dirty");
var audio = (AudioComponent)entity.Components.Single();

var objects = new ObservableCollection<SceneObjectViewModel>();
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
var owner = objects.Single();
var editor = (AudioComponentViewModel)owner.Components.Single();
Check(owner.CanAddComponents && !owner.AddableComponents.Single(c => c.DisplayName == "Audio").AddCommand.CanExecute(null), "picker disables duplicate component");
editor.AssignClip("Textures/image.png");
Check(!editor.HasClip, "non-audio assets are rejected");
// Exercise the view's drop handlers with the exact in-process format used by Assets.
// The handlers do not require an initialized window or the embedded graphics viewport.
var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
var audioFormat = (DataFormat<string>)typeof(MainWindow).GetField("AudioAssetFormat", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
var dropTarget = new Border { DataContext = editor };
var transfer = new DataTransfer();
transfer.Add(DataTransferItem.Create(audioFormat, "Audio/loop3d.wav"));
var drag = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
    DragDrop.DropEvent, transfer, dropTarget, new Avalonia.Point(), KeyModifiers.None)!;
Invoke(window, "AudioClip_DragOver", dropTarget, drag);
Check(drag.DragEffects == DragDropEffects.Copy && drag.Handled, "audio field accepts the Assets drag format");
Invoke(window, "AudioClip_Drop", dropTarget, drag);
Check(editor.ClipPath == "Audio/loop3d.wav", "dropping audio assigns the gameobject's component clip");
var invalidTransfer = new DataTransfer();
invalidTransfer.Add(DataTransferItem.Create(audioFormat, "Textures/image.png"));
var invalidDrag = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
    DragDrop.DropEvent, invalidTransfer, dropTarget, new Avalonia.Point(), KeyModifiers.None)!;
Invoke(window, "AudioClip_DragOver", dropTarget, invalidDrag);
Invoke(window, "AudioClip_Drop", dropTarget, invalidDrag);
Check(invalidDrag.DragEffects == DragDropEffects.None && editor.ClipPath == "Audio/loop3d.wav", "drop handler rejects non-audio files");
editor.Volume = 0.4;
editor.Loop = true;
editor.Spatial = false;
Publish();
Check(audio.ClipPath == "Audio/loop3d.wav" && audio.Loop && !audio.Spatial && Math.Abs(audio.Volume - 0.4f) < 0.0001,
    "inspector edits reach engine through queued component mutations");
Check(!bridge.Snapshot.Single().Components.Single().Data.TryGetProperty("IsPlaying", out _), "snapshots exclude runtime audio state");

BridgeReconciler.IsInspectorFocused = true;
BridgeReconciler.SelectedEngineId = entity.Id;
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
Check(ReferenceEquals(editor, owner.Components.Single()) && editor.ClipPath == audio.ClipPath,
    "focused reconciliation preserves the component editor and assigned clip");
editor.RemoveCommand.Execute(null);
Publish();
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
Check(owner.Components.Count == 0 && owner.AddableComponents.Single(c => c.DisplayName == "Audio").AddCommand.CanExecute(null),
    "remove reconciles while inspector is focused and re-enables picker");
owner.AddableComponents.Single(c => c.DisplayName == "Audio").AddCommand.Execute(null);
Publish();
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
Check(owner.Components.Count == 1, "add reconciles while inspector is focused");
BridgeReconciler.IsInspectorFocused = false;
BridgeReconciler.SelectedEngineId = null;

audio = (AudioComponent)entity.Components.Single();
audio.ClipPath = "Sounds/ambience.OGG";
audio.Volume = 0.3f;
audio.Loop = true;
audio.PlayOnStart = false;
audio.Spatial = false;
audio.Enabled = false;
var clone = (BasicEntity)entity.Clone;
Check(clone.Components.Count == 1 && !ReferenceEquals(clone.Components[0], audio), "cloning copies component data independently");
((AudioComponent)clone.Components[0]).ClipPath = "other.wav";
Check(audio.ClipPath == "Sounds/ambience.OGG", "clone edits do not affect original");

string scenePath = Path.Combine(Path.GetTempPath(), $"anvil-components-{Guid.NewGuid():N}.obsc");
try
{
    SceneSerialization.SaveToFile(logic.ActiveScene, scenePath, assets);
    var loaded = SceneSerialization.LoadFromFile(scenePath, assets);
    var restored = (AudioComponent)loaded.BasicEntities.Single().Components.Single();
    Check(restored.ClipPath == audio.ClipPath && restored.Volume == audio.Volume && restored.Loop &&
        !restored.PlayOnStart && !restored.Spatial && !restored.Enabled, "scene save/load preserves all audio settings");
    var json = JsonNode.Parse(File.ReadAllText(scenePath))!;
    json["Entities"]![0]!.AsObject().Remove("Components");
    File.WriteAllText(scenePath, json.ToJsonString());
    Check(SceneSerialization.LoadFromFile(scenePath, assets).BasicEntities.Single().Components.Count == 0,
        "older scenes without components still load");
}
finally { File.Delete(scenePath); }

// Material edits share the generic component bridge, cloning, and scene persistence.
bridge.EnqueueAddComponent(entity.Id, MaterialComponent.TypeId);
bridge.EnqueueAddComponent(entity.Id, MaterialComponent.TypeId);
Publish();
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
var surface = entity.Components.OfType<MaterialComponent>().Single();
var surfaceEditor = owner.Components.OfType<MaterialInfo>().Single();
var baseSlot = surfaceEditor.TextureSlots.Single(s => s.Name == "Base Color");
var normalSlot = surfaceEditor.TextureSlots.Single(s => s.Name == "Normal");
baseSlot.Assign("Textures/base.png");
normalSlot.Assign("Textures/normal.PNG");
normalSlot.Assign("Audio/loop.wav");
surfaceEditor.TextureSlots.Single(s => s.Name == "Metallic").ClearCommand.Execute(null);
Publish();
Check(surface.BaseColorTexture == "Textures/base.png" && surface.NormalTexture == "Textures/normal.PNG" &&
    surface.MetallicTexture == "" && surface.RoughnessTexture == null,
    "texture assignment rejects non-images and preserves inherit versus clear");
normalSlot.ResetCommand.Execute(null);
Publish();
Check(surface.NormalTexture == null, "reset restores model texture inheritance");
// Texture drop handlers use the Assets panel's in-process image format.
var textureFormat = (DataFormat<string>)typeof(MainWindow).GetField("TextureAssetFormat", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
var textureTarget = new Button { DataContext = normalSlot };
var textureTransfer = new DataTransfer();
textureTransfer.Add(DataTransferItem.Create(textureFormat, "Textures/normal.png"));
var textureDrag = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
    DragDrop.DropEvent, textureTransfer, textureTarget, new Avalonia.Point(), KeyModifiers.None)!;
Invoke(window, "MaterialTexture_DragOver", textureTarget, textureDrag);
Invoke(window, "MaterialTexture_Drop", textureTarget, textureDrag);
Publish();
Check(textureDrag.DragEffects == DragDropEffects.Copy && surface.NormalTexture == "Textures/normal.png",
    "texture drop assigns the selected material slot through the component bridge");
Check(owner.HasMaterialComponent && entity.Components.Count == 2 && ReferenceEquals(owner.Material, surfaceEditor),
    "Material can coexist with Audio and cannot be added twice");
surfaceEditor.WaterExampleCommand.Execute(null);
surfaceEditor.Color = Avalonia.Media.Color.FromRgb(13, 89, 115);
surfaceEditor.Opacity = 0.42;
surfaceEditor.WaveSpeed = 1.7;
surfaceEditor.WaveScale = 0.6;
surfaceEditor.WaveStrength = 0.35;
Publish();
Check(surface.Shader == MaterialShader.Water && Math.Abs(surface.Red - 13 / 255f) < 0.0001 &&
    Math.Abs(surface.Opacity - 0.42f) < 0.0001 && Math.Abs(surface.WaveSpeed - 1.7f) < 0.0001,
    "water example and inspector controls reach the engine");
var surfaceClone = ((BasicEntity)entity.Clone).Components.OfType<MaterialComponent>().Single();
surfaceClone.WaveSpeed = 5;
surfaceClone.BaseColorTexture = "Textures/other.png";
Check(surface.WaveSpeed != surfaceClone.WaveSpeed, "cloned material settings are independent");
Check(surface.BaseColorTexture == "Textures/base.png", "cloned texture assignments are independent");
string materialScenePath = Path.Combine(Path.GetTempPath(), $"anvil-materials-{Guid.NewGuid():N}.obsc");
try
{
    SceneSerialization.SaveToFile(logic.ActiveScene, materialScenePath, assets);
    var restored = SceneSerialization.LoadFromFile(materialScenePath, assets).BasicEntities.Single()
        .Components.OfType<MaterialComponent>().Single();
    Check(restored.Shader == surface.Shader && restored.Red == surface.Red &&
        restored.Green == surface.Green && restored.Blue == surface.Blue &&
        restored.Roughness == surface.Roughness && restored.Metallic == surface.Metallic &&
        restored.EmissiveStrength == surface.EmissiveStrength && restored.CastShadows == surface.CastShadows &&
        restored.Opacity == surface.Opacity && restored.WaveScale == surface.WaveScale &&
        restored.WaveSpeed == surface.WaveSpeed && restored.WaveStrength == surface.WaveStrength &&
        restored.BaseColorTexture == surface.BaseColorTexture && restored.NormalTexture == surface.NormalTexture &&
        restored.RoughnessTexture == null && restored.MetallicTexture == "",
        "scene save/load preserves material and water parameters");
}
finally { File.Delete(materialScenePath); }
BridgeReconciler.IsInspectorFocused = true;
BridgeReconciler.SelectedEngineId = entity.Id;
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
Check(ReferenceEquals(surfaceEditor, owner.Components.OfType<MaterialInfo>().Single()),
    "focused reconciliation keeps the material editor stable");
surfaceEditor.Enabled = false;
Publish();
Check(!surface.Enabled, "material can be disabled through the inspector");
surfaceEditor.RemoveCommand.Execute(null);
Publish();
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
Check(!owner.HasMaterialComponent && owner.Material == null && entity.Components.OfType<MaterialComponent>().Count() == 0 &&
    owner.AddableComponents.Single(c => c.DisplayName == "Material").AddCommand.CanExecute(null),
    "removing material removes the single editor and restores the picker");
var legacySnapshot = new EditorObjectSnapshot(entity.Id, entity.Name, EditorObjectKind.BasicEntity,
    entity.Position, entity.RotationMatrix, entity.Scale, entity.IsEnabled, null,
    new MaterialSnapshot(Vector3.One, 0.7f, 0.3f, 0, false, 0),
    components: entity.Components.Select(ComponentRegistry.Capture).ToArray());
BridgeReconciler.Apply(new[] { legacySnapshot }, objects, bridge);
Check(owner.Material == null && owner.Components.OfType<MaterialInfo>().Count() == 0,
    "base material metadata does not recreate an automatic Material inspector");
var legacyWater = (MaterialComponent)ComponentRegistry.Restore(new ComponentRecord
{
    Type = MaterialComponent.TypeId,
    Data = System.Text.Json.JsonSerializer.SerializeToElement(new { Shader = 1, Red = 0.12f, WaveSpeed = 2.4f }),
});
Check(legacyWater.MaterialType == MaterialEffect.MaterialTypes.Water && legacyWater.Red == 0.12f && legacyWater.WaveSpeed == 2.4f,
    "earlier water component records remain compatible");
foreach (var type in Enum.GetValues<MaterialEffect.MaterialTypes>())
{
    var authored = new MaterialComponent { MaterialType = type, IsTransparent = true, Opacity = 0.3f };
    var restored = (MaterialComponent)ComponentRegistry.Copy(authored);
    Check(restored.MaterialType == type && restored.IsTransparent && restored.Opacity == 0.3f,
        $"existing material type {type} round-trips as a component");
}
BridgeReconciler.IsInspectorFocused = false;
BridgeReconciler.SelectedEngineId = null;

// Spawned entities must be enabled, or audio and Play-mode components silently skip them.
var spawned = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One);
Check(spawned.IsEnabled && new BasicEntity(assets.Cube, null, Vector3.Zero, 0, 0, 0, Vector3.One).IsEnabled,
    "new gameobjects start enabled");
string enabledScenePath = Path.Combine(Path.GetTempPath(), $"anvil-enabled-{Guid.NewGuid():N}.obsc");
try
{
    SceneSerialization.SaveToFile(logic.ActiveScene, enabledScenePath, assets);
    var json = JsonNode.Parse(File.ReadAllText(enabledScenePath))!;
    json["Entities"]![0]!["IsEnabled"] = false;
    File.WriteAllText(enabledScenePath, json.ToJsonString());
    Check(!SceneSerialization.LoadFromFile(enabledScenePath, assets).BasicEntities.Single().IsEnabled,
        "current scenes keep deliberately disabled gameobjects");
    json["Version"] = 1;
    File.WriteAllText(enabledScenePath, json.ToJsonString());
    Check(SceneSerialization.LoadFromFile(enabledScenePath, assets).BasicEntities.Single().IsEnabled,
        "version 1 scenes enable gameobjects saved with the old stuck default");
}
finally { File.Delete(enabledScenePath); }
var audioEditor = owner.Components.OfType<AudioComponentViewModel>().Single();
audioEditor.MinDistance = 25;
audioEditor.MaxDistance = 400;
Publish();
Check(audio.MinDistance == 25 && audio.MaxDistance == 400 &&
    ((AudioComponent)ComponentRegistry.Copy(audio)).MinDistance == 25, "audio falloff distances reach the engine and persist");
Check(new AudioComponent().MinDistance == 10, "3D audio plays at full volume within 10 units by default");

// Physics is a regular component: bridge add/edit/remove, cloning, persistence, legacy records.
Check(entity.Physics == null && entity.PhysicsType == Engine.Physics.PhysicsBodyType.None, "entities start without physics");
bridge.EnqueueAddComponent(entity.Id, PhysicsComponent.TypeId);
bridge.EnqueueAddComponent(entity.Id, PhysicsComponent.TypeId);
Publish();
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
var physics = entity.GetComponent<PhysicsComponent>();
var physicsEditor = owner.Components.OfType<PhysicsComponentViewModel>().Single();
Check(entity.Components.OfType<PhysicsComponent>().Count() == 1 && entity.PhysicsType == Engine.Physics.PhysicsBodyType.Dynamic &&
    physicsEditor.IsDynamic && !owner.AddableComponents.Single(c => c.DisplayName == "Physics").AddCommand.CanExecute(null),
    "physics adds once as a dynamic body and disables its picker entry");
physicsEditor.BodyType = Engine.Physics.PhysicsBodyType.Static;
physicsEditor.Mass = 3.5;
Publish();
Check(physics.BodyType == Engine.Physics.PhysicsBodyType.Static && physics.Mass == 3.5f && entity.PhysicsType == Engine.Physics.PhysicsBodyType.Static,
    "physics inspector edits reach the engine component");
physicsEditor.Mass = 0;
Publish();
Check(physics.Mass == PhysicsComponent.MinMass, "physics mass is clamped above zero");
physicsEditor.Enabled = false;
Publish();
Check(entity.PhysicsType == Engine.Physics.PhysicsBodyType.None, "disabled physics component builds no body");
physicsEditor.Enabled = true;
physicsEditor.Mass = 2;
Publish();
var physicsClone = ((BasicEntity)entity.Clone).GetComponent<PhysicsComponent>();
physicsClone.Mass = 9;
Check(physics.Mass == 2 && physicsClone.BodyType == physics.BodyType, "cloned physics settings are independent");
string physicsScenePath = Path.Combine(Path.GetTempPath(), $"anvil-physics-{Guid.NewGuid():N}.obsc");
try
{
    SceneSerialization.SaveToFile(logic.ActiveScene, physicsScenePath, assets);
    var json = JsonNode.Parse(File.ReadAllText(physicsScenePath))!;
    var savedEntity = json["Entities"]![0]!.AsObject();
    Check(savedEntity["Physics"] == null && savedEntity["Components"]!.AsArray()
        .Any(c => (string?)c!["Type"] == PhysicsComponent.TypeId && (string?)c["Data"]!["BodyType"] == "Static"),
        "physics saves as a component record with a readable body type");
    var restored = SceneSerialization.LoadFromFile(physicsScenePath, assets).BasicEntities.Single().GetComponent<PhysicsComponent>();
    Check(restored.BodyType == Engine.Physics.PhysicsBodyType.Static && restored.Mass == 2 && restored.Enabled,
        "scene save/load preserves physics settings");

    var components = savedEntity["Components"]!.AsArray();
    components.Remove(components.First(c => (string?)c!["Type"] == PhysicsComponent.TypeId));
    savedEntity["Physics"] = new JsonObject { ["Type"] = "Dynamic", ["Mass"] = 2.5 };
    File.WriteAllText(physicsScenePath, json.ToJsonString());
    var migrated = SceneSerialization.LoadFromFile(physicsScenePath, assets).BasicEntities.Single().GetComponent<PhysicsComponent>();
    Check(migrated is { BodyType: Engine.Physics.PhysicsBodyType.Dynamic, Mass: 2.5f }, "older scene physics records load as a Physics component");
}
finally { File.Delete(physicsScenePath); }
physicsEditor.RemoveCommand.Execute(null);
Publish();
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
Check(entity.Physics == null && !owner.Components.OfType<PhysicsComponentViewModel>().Any() &&
    owner.AddableComponents.Single(c => c.DisplayName == "Physics").AddCommand.CanExecute(null),
    "removing physics removes the body settings and restores the picker");

// Loaded scenes keep their saved IDs. Viewport picking reads WorldTransform.Id, so it must
// match too; otherwise clicking one object selects (and adds components to) another.
var neighbour = new BasicEntity(assets.Cube, null, Vector3.One, Matrix.Identity, Vector3.One) { IsEnabled = true, Name = "Neighbour" };
logic.BasicEntities.Add(neighbour);
string idScenePath = Path.Combine(Path.GetTempPath(), $"anvil-ids-{Guid.NewGuid():N}.obsc");
try
{
    SceneSerialization.SaveToFile(logic.ActiveScene, idScenePath, assets);
    var loaded = SceneSerialization.LoadFromFile(idScenePath, assets).BasicEntities;
    Check(loaded.Select(e => e.Id).SequenceEqual(new[] { entity.Id, neighbour.Id }) &&
        loaded.All(e => e.WorldTransform.Id == e.Id), "loaded entities pick with their persisted IDs");
    var json = JsonNode.Parse(File.ReadAllText(idScenePath))!;
    json["Entities"]![1]!["Id"] = entity.Id;
    File.WriteAllText(idScenePath, json.ToJsonString());
    loaded = SceneSerialization.LoadFromFile(idScenePath, assets).BasicEntities;
    Check(loaded[0].Id == entity.Id && loaded[1].Id != entity.Id && loaded[1].WorldTransform.Id == loaded[1].Id,
        "duplicate saved IDs are reassigned instead of aliasing two objects");
}
finally { File.Delete(idScenePath); }
bridge.EnqueueAddComponent(neighbour.Id, PhysicsComponent.TypeId);
Publish();
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
var neighbourVm = objects.Single(o => o.EngineId == neighbour.Id);
Check(neighbour.Physics != null && entity.Physics == null &&
    neighbourVm.Components.OfType<PhysicsComponentViewModel>().Count() == 1 && !owner.Components.OfType<PhysicsComponentViewModel>().Any(),
    "a component added to one gameobject never appears on another");
var swapped = new EditorObjectSnapshot(neighbour.Id, "Light", EditorObjectKind.PointLight, Vector3.Zero,
    Matrix.Identity, Vector3.One, true, null, null);
BridgeReconciler.Apply(new[] { bridge.Snapshot.First(s => s.Id == entity.Id), swapped }, objects, bridge);
Check(!ReferenceEquals(neighbourVm, objects.Single(o => o.EngineId == neighbour.Id)) &&
    objects.Single(o => o.EngineId == neighbour.Id).Components.Count == 0,
    "an ID reused by a different kind of object gets fresh editors");
logic.BasicEntities.Remove(neighbour);
Publish();
BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);

ComponentRegistry.Register<LifecycleComponent>("test-lifecycle", "Test lifecycle");
var behaviour = new LifecycleComponent { Value = 42 };
entity.Components.Add(behaviour);
Check(((LifecycleComponent)ComponentRegistry.Copy(behaviour)).Value == 42, "additional registered types round-trip generically");
var playMode = new PlayModeController(logic);
playMode.Play();
playMode.UpdateScripts(new GameTime());
playMode.Stop();
Check(behaviour.Starts == 1 && behaviour.Updates == 1 && behaviour.Stops >= 1, "component lifecycle follows Play/Update/Stop");
behaviour.Enabled = false;
playMode.Play();
playMode.UpdateScripts(new GameTime());
playMode.Stop();
Check(behaviour.Starts == 1 && behaviour.Updates == 1, "disabled components do not start or update");

if (args.Contains("--audio"))
{
    using var manager = new AudioManager();
    manager.Initialize("Content");
    Check(manager.MasterVolume == 1 && !manager.IsMuted && mainVm.IsAudioEnabled,
        "engine and editor sound start enabled at full master volume");
    manager.MasterVolume = 0;
    mainVm.ToggleAudioCommand.Execute(null);
    Publish();
    FmodForFoxes.CoreSystem.Native.getMasterChannelGroup(out var master);
    master.getMute(out bool muted);
    Check(muted && !mainVm.IsAudioEnabled && manager.IsMuted, "editor speaker toggle mutes native engine output");
    mainVm.ToggleAudioCommand.Execute(null);
    Publish();
    master.getMute(out muted);
    master.getVolume(out float volume);
    Check(!muted && mainVm.IsAudioEnabled && !manager.IsMuted && volume == 0,
        "enabling editor audio unmutes output and preserves master volume");
    audio.ClipPath = "Audio/loop3d.wav";
    audio.Enabled = true;
    audio.PlayOnStart = true;
    audio.Spatial = false;
    bridge.EnqueuePlayAudio(entity.Id, true);
    Publish();
    Check(audio.IsPlaying, "FMOD plays assigned 2D audio through editor bridge");
    bridge.EnqueueMutateComponent(entity.Id, AudioComponent.TypeId, c => ((AudioComponent)c).Volume = 0.25f);
    Publish();
    Check(audio.IsPlaying && Math.Abs(Channel(audio).Volume - 0.25f) < 0.0001, "volume updates the playing channel without stopping it");
    bridge.EnqueueRemoveComponent(entity.Id, AudioComponent.TypeId);
    Publish();
    Check(!audio.IsPlaying, "removing component stops its 2D channel");
    entity.Components.Add(audio);
    audio.Spatial = true;
    playMode.Play();
    Check(audio.IsPlaying, "FMOD starts assigned 3D audio in Play mode");
    entity.Position = new Vector3(5, 0, 0);
    manager.UpdateListener(new Camera(Vector3.Zero, Vector3.Forward));
    manager.SystemUpdate();
    Check(Channel(audio).Position3D == entity.Position, "3D emitter follows its gameobject");
    playMode.Stop();
    Check(!audio.IsPlaying, "Stop ends component playback");

    // Same setup as the editor: an object spawned 5 units in front of the camera.
    manager.MasterVolume = 1; // set to 0 above; audibility includes the master bus
    var spawnedAudio = new AudioComponent { ClipPath = "Audio/loop3d.wav", Loop = true };
    spawned.Position = new Vector3(5, 0, 0);
    spawned.AddComponent(spawnedAudio);
    manager.UpdateListener(new Camera(Vector3.Zero, new Vector3(1, 0, -0.3f)));
    spawnedAudio.Play(spawned);
    for (int i = 0; i < 5; i++) { manager.UpdateListener(new Camera(Vector3.Zero, new Vector3(1, 0, -0.3f))); manager.SystemUpdate(); }
    Channel(spawnedAudio).Native.getAudibility(out float audibility);
    Check(spawnedAudio.IsPlaying && audibility > 0.99f, "a freshly spawned gameobject's 3D audio plays at full volume nearby");
    spawnedAudio.OnStop();

    string unique = "anvil-audio-" + Guid.NewGuid().ToString("N");
    string sourceRoot = Path.Combine(Path.GetTempPath(), unique);
    string clip = unique + "/live.wav";
    string source = Path.Combine(sourceRoot, clip);
    string target = Path.Combine(AppContext.BaseDirectory, "Content", clip);
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Content", "Audio", "loop3d.wav"), source);
        manager.EditorContentRoot = sourceRoot;
        audio.ClipPath = clip;
        audio.Play(entity);
        Check(audio.IsPlaying && File.Exists(target), "newly imported audio outside Audio folder plays without rebuilding");
        audio.Spatial = !audio.Spatial;
        audio.OnChanged(entity);
        Check(audio.IsPlaying, "switching imported audio between 2D and 3D reuses its runtime file");
        audio.Enabled = false;
        audio.OnChanged(entity);
        Check(!audio.IsPlaying, "disabling component stops playback");
    }
    finally
    {
        audio.OnStop();
        manager.EditorContentRoot = null;
        manager.Dispose();
        File.Delete(source);
        Directory.Delete(Path.GetDirectoryName(source)!);
        Directory.Delete(sourceRoot);
        // The FMOD wrapper can retain the runtime copy's file handle until process exit.
        // That generated fixture stays in the test project's ignored bin directory.
    }
}

ScriptBehaviourChecks.Run();
GameObjectMenuChecks.Run();
SampleSceneChecks.Run();
FreecamChecks.Run();
EnvironmentChecks.Run();
WaterChecks.Run();
SteamChecks.Run();
InputDeviceChecks.Run();
if (args.Contains("--input-native")) InputDeviceChecks.RunNative();
if (args.Contains("--steam-native")) SteamChecks.RunNative();
Console.WriteLine("All component checks passed.");
if (args.Contains("--graphics")) MaterialGraphicsChecks.Run();

void Publish()
{
    for (int i = 0; i < 6; i++) Invoke(bridge, "DrainAndPublish");
}

static MouseState Mouse(int x, ButtonState left) => new(x, 20, 0,
    left, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

static void MouseFrame(MouseState raw)
{
    Input.mouseLastState = Input.mouseState;
    Input.mouseState = (MouseState)typeof(Input).GetMethod("FilterHostMouse", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { raw })!;
}

void UpdateSceneEditor() => sceneEditor.Update(new GameTime(), logic.BasicEntities, logic.Decals,
    logic.PointLights, logic.DirectionalLights, null, logic.DebugEntities, new EditorLogic.EditorReceivedData(), null);

static object? Invoke(object target, string method, params object[] args) =>
    target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

static FmodForFoxes.Channel Channel(AudioComponent component) =>
    (FmodForFoxes.Channel)typeof(AudioComponent).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(component)!;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}

public sealed class LifecycleComponent : GameComponent
{
    public int Value { get; set; }
    [JsonIgnore] public int Starts { get; private set; }
    [JsonIgnore] public int Updates { get; private set; }
    [JsonIgnore] public int Stops { get; private set; }
    public override void OnStart(BasicEntity owner) => Starts++;
    public override void OnUpdate(BasicEntity owner, GameTime time) => Updates++;
    public override void OnStop() => Stops++;
}
