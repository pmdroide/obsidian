using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;

/// <summary>
/// Audio without FMOD running: Reverb Zone blending (fade, nesting, priority), wall muffling ray casts against
/// BEPU statics, the Doppler pitch readout, the Audio component's stereo / distance / reverb / muffling / Doppler
/// settings through the inspector and saving, and the AudioTest sample scene.
/// </summary>
internal static class AudioChecks
{
    private static readonly Assets TestAssets = new()
    {
        Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
        IsoSphere = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
    };

    public static void Run()
    {
        CheckReverbZones();
        CheckEditors();
        CheckRolloffCurves();
        CheckWallMuffling();
        CheckSoundMaterials();
        CheckDoppler();
        CheckSampleScene();
    }

    private static BasicEntity Zone(Vector3 position, Vector3 scale, ReverbZoneComponent zone)
    {
        var entity = new BasicEntity(TestAssets.Cube, null, position, Matrix.Identity, scale) { Name = zone.Preset.ToString(), IsEnabled = true };
        entity.AddComponent(zone);
        return entity;
    }

    private static void CheckReverbZones()
    {
        Check(ComponentRegistry.Find(ReverbZoneComponent.TypeId)?.ComponentType == typeof(ReverbZoneComponent) &&
              ComponentEditorRegistry.Supports(ReverbZoneComponent.TypeId),
            "Reverb Zone is registered and offered in Add Component");
        Check(ReverbZoneComponent.Evaluate(new List<BasicEntity>(), Vector3.Zero).WetLevel <= -80 &&
              ReverbZoneComponent.ActiveZones.Count == 0, "no zones: no reverb");

        FMOD.REVERB_PROPERTIES bathroom = FMOD.PRESET.BATHROOM();
        var room = Zone(new Vector3(10, 0, 2), new Vector3(2, 2, 2), new ReverbZoneComponent { Preset = ReverbPreset.Bathroom, BlendDistance = 1 });
        var list = new List<BasicEntity> { room };
        room.WorldTransform.Hidden = false; // as after a scene load, which skips the component hooks
        Check(ReverbZoneComponent.Evaluate(list, new Vector3(10, 0, 2)).Equals(bathroom) && room.WorldTransform.Hidden &&
              ReverbZoneComponent.ActiveZones.Single() == (room, 1f),
            "inside a zone the listener hears its preset, and the box mesh is hidden");
        FMOD.REVERB_PROPERTIES half = ReverbZoneComponent.Evaluate(list, new Vector3(12.5f, 0, 2));
        Check(half.DecayTime == bathroom.DecayTime && Math.Abs(half.WetLevel - (bathroom.WetLevel - 6.0206f)) < 0.01f,
            "half way through Blend Distance the room keeps its character at half the wet gain (-6 dB)");
        Check(ReverbZoneComponent.Evaluate(list, new Vector3(13.1f, 0, 2)).WetLevel <= -80, "beyond Blend Distance: no reverb");

        var hall = Zone(Vector3.Zero, new Vector3(10, 10, 5), new ReverbZoneComponent { Preset = ReverbPreset.ConcertHall });
        var booth = Zone(new Vector3(5, 5, 1), new Vector3(1, 1, 1), new ReverbZoneComponent { Preset = ReverbPreset.PaddedCell, BlendDistance = 0.5f });
        var nested = new List<BasicEntity> { booth, hall };
        Check(ReverbZoneComponent.Evaluate(nested, new Vector3(5, 5, 1)).Equals(FMOD.PRESET.PADDEDCELL()) &&
              ReverbZoneComponent.ActiveZones.Select(z => z.Zone).SequenceEqual(new[] { hall, booth }),
            "a smaller zone nested in a bigger one wins inside it (blended last)");
        Check(ReverbZoneComponent.Evaluate(nested, new Vector3(-5, -5, 1)).Equals(FMOD.PRESET.CONCERTHALL()),
            "the hall's preset away from the nested booth");
        FMOD.REVERB_PROPERTIES edge = ReverbZoneComponent.Evaluate(nested, new Vector3(6.25f, 5, 1));
        Check(edge.DecayTime > FMOD.PRESET.PADDEDCELL().DecayTime && edge.DecayTime < FMOD.PRESET.CONCERTHALL().DecayTime,
            "at the booth's door the reverb is a blend of hall and booth");
        hall.GetComponent<ReverbZoneComponent>().Priority = 1;
        Check(ReverbZoneComponent.Evaluate(nested, new Vector3(5, 5, 1)).Equals(FMOD.PRESET.CONCERTHALL()),
            "a higher Priority wins over a nested zone");
        hall.GetComponent<ReverbZoneComponent>().Priority = 0;

        var tuned = new ReverbZoneComponent { Preset = ReverbPreset.Cave, DecayScale = 2, WetLevel = 3 };
        Check(tuned.Properties.DecayTime == FMOD.PRESET.CAVE().DecayTime * 2 && tuned.Properties.WetLevel == FMOD.PRESET.CAVE().WetLevel + 3,
            "Decay Scale and Wet Level tune the preset");
        var everywhere = Zone(new Vector3(500, 0, 0), Vector3.One, new ReverbZoneComponent { Preset = ReverbPreset.Forest, Infinite = true });
        Check(ReverbZoneComponent.Evaluate(new List<BasicEntity> { everywhere, room }, new Vector3(10, 0, 2)).Equals(bathroom) &&
              ReverbZoneComponent.Evaluate(new List<BasicEntity> { everywhere, room }, new Vector3(-50, 0, 0)).Equals(FMOD.PRESET.FOREST()),
            "an Infinite zone applies everywhere, under the rooms");
        room.GetComponent<ReverbZoneComponent>().Enabled = false;
        Check(ReverbZoneComponent.Evaluate(list, new Vector3(10, 0, 2)).WetLevel <= -80 && !room.WorldTransform.Hidden,
            "a disabled zone does nothing and shows its mesh");

        var record = ComponentRegistry.Capture(new ReverbZoneComponent { Preset = ReverbPreset.SewerPipe, Priority = 2, DecayScale = 1.5f });
        Check(record.Data.GetProperty("Preset").GetString() == "SewerPipe" &&
              ComponentRegistry.Restore(record) is ReverbZoneComponent { Preset: ReverbPreset.SewerPipe, Priority: 2, DecayScale: 1.5f },
            "Reverb Zone records save the preset by name and round-trip");
    }

    private static void CheckEditors()
    {
        var logic = new MainSceneLogic();
        var speaker = new BasicEntity(TestAssets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Speaker", IsEnabled = true };
        var room = new BasicEntity(TestAssets.Cube, null, new Vector3(0, 10, 0), Matrix.Identity, new Vector3(3)) { Name = "Room", IsEnabled = true };
        logic.BasicEntities.Add(speaker);
        logic.BasicEntities.Add(room);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), TestAssets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        objects.Single(o => o.Name == "Speaker").AddableComponents.Single(c => c.DisplayName == "Audio").AddCommand.Execute(null);
        objects.Single(o => o.Name == "Room").AddableComponents.Single(c => c.DisplayName == "Reverb Zone").AddCommand.Execute(null);
        Publish();

        var audio = speaker.GetComponent<AudioComponent>();
        var audioEditor = objects.Single(o => o.Name == "Speaker").Components.OfType<AudioComponentViewModel>().Single();
        Check(audio is { Pitch: 1, Pan: 0, SpatialBlend: 1, Spread: 0, Rolloff: AudioRolloff.Inverse, ReverbSend: 1, WallMuffling: 1, DopplerLevel: 1 },
            "a new Audio component is centred, fully 3D, a point source, inverse rolloff, fully in the reverb, muffled by walls and with real Doppler");
        audioEditor.Pitch = 1.5;
        audioEditor.Pan = -0.75;
        audioEditor.SpatialBlend = 0.5;
        audioEditor.Spread = 90;
        audioEditor.Rolloff = AudioRolloff.LinearSquare;
        audioEditor.ReverbSend = 0.25;
        audioEditor.WallMuffling = 0.5;
        audioEditor.DopplerLevel = 2;
        Publish();
        Check(audio is { Pitch: 1.5f, Pan: -0.75f, SpatialBlend: 0.5f, Spread: 90, Rolloff: AudioRolloff.LinearSquare, ReverbSend: 0.25f,
                  WallMuffling: 0.5f, DopplerLevel: 2 },
            "Audio pitch, stereo pan, spatial blend, spread, rolloff, reverb send, wall muffling and Doppler reach the engine");
        var record = ComponentRegistry.Capture(audio);
        Check(record.Data.GetProperty("Rolloff").GetString() == "LinearSquare" &&
              ComponentRegistry.Restore(record) is AudioComponent { Pan: -0.75f, Rolloff: AudioRolloff.LinearSquare, ReverbSend: 0.25f },
            "the new Audio settings save (rolloff by name) and round-trip");
        var old = JsonSerializer.Deserialize<AudioComponent>("{\"ClipPath\":\"Audio/blip.wav\",\"MinDistance\":5}");
        Check(old is { Pitch: 1, SpatialBlend: 1, ReverbSend: 1, Rolloff: AudioRolloff.Inverse, MinDistance: 5, WallMuffling: 1, DopplerLevel: 1 },
            "older Audio records load with the old behaviour (inverse rolloff, full 3D)");

        var zone = room.GetComponent<ReverbZoneComponent>();
        var zoneEditor = objects.Single(o => o.Name == "Room").Components.OfType<ReverbZoneComponentViewModel>().Single();
        Check(zone is { Preset: ReverbPreset.Room, BlendDistance: 1 } && room.WorldTransform.Hidden && zoneEditor.Preset == ReverbPreset.Room,
            "a new Reverb Zone is a Room with a 1 m blend and hides its box mesh");
        zoneEditor.Preset = ReverbPreset.Hangar;
        zoneEditor.BlendDistance = 4;
        zoneEditor.Priority = 3;
        zoneEditor.DecayScale = 0.5;
        zoneEditor.WetLevel = -6;
        Publish();
        Check(zone is { Preset: ReverbPreset.Hangar, BlendDistance: 4, Priority: 3, DecayScale: 0.5f, WetLevel: -6 },
            "Reverb Zone inspector edits reach the engine");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckRolloffCurves()
    {
        float V(AudioRolloff r, float d) => AudioTestSceneScript.ExpectedVolume(r, d, 3, 25);
        Check(V(AudioRolloff.Linear, 2) == 1 && Math.Abs(V(AudioRolloff.Linear, 14) - 0.5f) < 1e-4f && V(AudioRolloff.Linear, 25) == 0 &&
              Math.Abs(V(AudioRolloff.LinearSquare, 14) - 0.25f) < 1e-4f && Math.Abs(V(AudioRolloff.Inverse, 6) - 0.5f) < 1e-4f &&
              V(AudioRolloff.Inverse, 100) == V(AudioRolloff.Inverse, 25) && V(AudioRolloff.InverseTapered, 25) == 0,
            "the test scene's volume readout follows FMOD's rolloff curves");
    }

    private static void CheckWallMuffling()
    {
        using var world = new CollisionChecks.World();
        // Listener at the origin, emitter 10 m along +Y; walls are thin boxes across the line.
        var near = world.Add("Near Wall", new Vector3(0, 3, 1.5f), new Vector3(3, 0.1f, 1.5f), PhysicsBodyType.Static, recorder: false);
        var far = world.Add("Far Wall", new Vector3(0, 7, 1.5f), new Vector3(3, 0.1f, 1.5f), PhysicsBodyType.Static, recorder: false);
        var crate = world.Add("Crate", new Vector3(12, 2, 1.5f), new Vector3(0.5f), PhysicsBodyType.Static, recorder: false);
        var ball = world.Add("Ball", new Vector3(-10, 5, 1.5f), new Vector3(0.5f), PhysicsBodyType.Dynamic, recorder: false);
        world.Step(1);
        Vector3 ear = new(0, 0, 1.5f);
        Check(AudioOcclusion.CountWalls(world.Scene, ear, new Vector3(0, 2, 1.5f)) == 0 &&
              AudioOcclusion.CountWalls(world.Scene, ear, new Vector3(0, 5, 1.5f)) == 1 &&
              AudioOcclusion.CountWalls(world.Scene, ear, new Vector3(0, 10, 1.5f)) == 2,
            "a ray counts each wall once (front and back face of a mesh wall are one wall)");
        Check(AudioOcclusion.CountWalls(world.Scene, ear, new Vector3(12, 2, 1.5f), crate) == 0 &&
              AudioOcclusion.CountWalls(world.Scene, ear, new Vector3(12, 2, 1.5f)) == 1,
            "a sound's own gameobject doesn't muffle it");
        Check(AudioOcclusion.CountWalls(world.Scene, ear, new Vector3(-10, 5, 1.5f)) == 0,
            "dynamic bodies don't muffle (only static colliders)");
        Check(AudioOcclusion.CountWalls(world.Scene, ear, new Vector3(0, 5, 6)) == 0,
            "a sound above the wall's top isn't muffled");
        float edge = AudioOcclusion.Walls(world.Scene, ear, new Vector3(5, 5, 1.5f));
        Check(edge > 0.2f && edge < 0.8f, $"at a wall's end the side rays blend: {edge:0.00} walls");
        Check(AudioOcclusion.OcclusionFor(0, 0.6f) == 0 && Math.Abs(AudioOcclusion.OcclusionFor(1, 0.6f) - 0.6f) < 1e-5f &&
              Math.Abs(AudioOcclusion.OcclusionFor(2, 0.6f) - 0.84f) < 1e-5f,
            "each wall blocks 60 % of what's left (one wall 0.6, two 0.84)");
        _ = near; _ = far; _ = ball;
    }

    private static void CheckSoundMaterials()
    {
        SoundMaterialProperties P(SoundMaterial m) => AudioOcclusion.PropertiesOf(m);
        Check(P(SoundMaterial.Stone).Block > P(SoundMaterial.Concrete).Block - 0.05f &&
              P(SoundMaterial.Concrete).Block > P(SoundMaterial.Brick).Block && P(SoundMaterial.Brick).Block > P(SoundMaterial.Metal).Block &&
              P(SoundMaterial.Metal).Block > P(SoundMaterial.Wood).Block && P(SoundMaterial.Wood).Block > P(SoundMaterial.Glass).Block &&
              P(SoundMaterial.Glass).Block > P(SoundMaterial.Fabric).Block && P(SoundMaterial.Fabric).Block > P(SoundMaterial.Foliage).Block,
            "materials block sound from stone/concrete down to brick, metal, wood, glass, fabric, foliage");
        Check(P(SoundMaterial.Metal).Dullness < P(SoundMaterial.Wood).Dullness && P(SoundMaterial.Glass).Dullness < P(SoundMaterial.Concrete).Dullness &&
              AudioOcclusion.PropertiesOf(SoundMaterial.Default, 0.3f).Block == 0.3f,
            "metal and glass stay brighter than wood and concrete; Default uses the scene-wide wall occlusion");

        using var world = new CollisionChecks.World();
        var concrete = world.Add("Concrete", new Vector3(0, 3, 1.5f), new Vector3(3, 0.1f, 1.5f), PhysicsBodyType.Static, recorder: false);
        var wood = world.Add("Wood", new Vector3(20, 3, 1.5f), new Vector3(3, 0.1f, 1.5f), PhysicsBodyType.Static, recorder: false);
        var custom = world.Add("Custom", new Vector3(20, 6, 1.5f), new Vector3(3, 0.1f, 1.5f), PhysicsBodyType.Static, recorder: false);
        concrete.GetComponent<PhysicsComponent>().SoundMaterial = SoundMaterial.Concrete;
        wood.GetComponent<PhysicsComponent>().SoundMaterial = SoundMaterial.Wood;
        var customPhysics = custom.GetComponent<PhysicsComponent>();
        customPhysics.SoundMaterial = SoundMaterial.Custom;
        customPhysics.SoundBlock = 0.2f;
        customPhysics.SoundDullness = 0.1f;
        world.Step(1);
        AcousticPath throughConcrete = AudioOcclusion.Trace(world.Scene, new Vector3(0, 0, 1.5f), new Vector3(0, 4.5f, 1.5f));
        AcousticPath throughWood = AudioOcclusion.Trace(world.Scene, new Vector3(20, 0, 1.5f), new Vector3(20, 4.5f, 1.5f));
        AcousticPath throughBoth = AudioOcclusion.Trace(world.Scene, new Vector3(20, 0, 1.5f), new Vector3(20, 8, 1.5f));
        Check(Math.Abs(throughConcrete.Occlusion - 0.9f) < 1e-4f && Math.Abs(throughConcrete.Dullness - 0.85f) < 1e-4f &&
              Math.Abs(throughWood.Occlusion - 0.45f) < 1e-4f && Math.Abs(throughWood.Dullness - 0.6f) < 1e-4f,
            "a concrete wall stops 90 % and dulls 85 %, a wooden one 45 % / 60 %");
        Check(Math.Abs(throughBoth.Walls - 2) < 1e-4f && Math.Abs(throughBoth.Occlusion - (1 - 0.55f * 0.8f)) < 1e-4f &&
              Math.Abs(throughBoth.Dullness - (1 - 0.4f * 0.9f)) < 1e-4f,
            "walls in a row multiply what gets through, and Custom uses the Physics component's own values");

        var record = ComponentRegistry.Capture(new PhysicsComponent { BodyType = PhysicsBodyType.Static, SoundMaterial = SoundMaterial.Brick });
        Check(record.Data.GetProperty("SoundMaterial").GetString() == "Brick" &&
              ComponentRegistry.Restore(record) is PhysicsComponent { SoundMaterial: SoundMaterial.Brick } &&
              JsonSerializer.Deserialize<PhysicsComponent>("{\"BodyType\":\"Static\"}") is { SoundMaterial: SoundMaterial.Default },
            "Sound Material saves by name; older Physics records are Default");
    }

    private static void CheckDoppler()
    {
        Vector3 listener = Vector3.Zero;
        float approaching = AudioTestSceneScript.ExpectedPitch(new Vector3(-50, 0, 0), new Vector3(34, 0, 0), listener, Vector3.Zero);
        float leaving = AudioTestSceneScript.ExpectedPitch(new Vector3(50, 0, 0), new Vector3(34, 0, 0), listener, Vector3.Zero);
        float abeam = AudioTestSceneScript.ExpectedPitch(new Vector3(0, 10, 0), new Vector3(34, 0, 0), listener, Vector3.Zero);
        float walking = AudioTestSceneScript.ExpectedPitch(new Vector3(50, 0, 0), Vector3.Zero, listener, new Vector3(34, 0, 0));
        Check(Math.Abs(approaching - 340f / 306f) < 1e-4f && Math.Abs(leaving - 340f / 374f) < 1e-4f && Math.Abs(abeam - 1) < 1e-4f &&
              Math.Abs(walking - 1.1f) < 1e-4f && AudioTestSceneScript.ExpectedPitch(new Vector3(-50, 0, 0), new Vector3(34, 0, 0), listener, Vector3.Zero, 0) == 1,
            "Doppler: a 34 m/s source is x1.111 coming and x0.909 going, x1 passing abeam; a moving listener shifts too; level 0 = none");
        Check(AudioManager.MaxDopplerSpeed < 160 * 60, "the racer's 160 m jump back to the start is a teleport, not motion");
    }

    private static void CheckSampleScene()
    {
        string content = new EditorBridge().ContentSourceRoot;
        var scene = SceneSerialization.LoadFromFile(Path.Combine(content, "Scenes", "AudioTest.obsc"), TestAssets);
        var zones = scene.BasicEntities.Where(e => e.GetComponent<ReverbZoneComponent>() != null).ToList();
        Check(zones.Select(z => z.GetComponent<ReverbZoneComponent>().Preset).OrderBy(p => p).SequenceEqual(
                  new[] { ReverbPreset.Bathroom, ReverbPreset.ConcertHall, ReverbPreset.SewerPipe, ReverbPreset.PaddedCell }.OrderBy(p => p)),
            "the audio sample has Bathroom, Concert Hall, Padded Booth and Sewer Tunnel reverb zones");
        BasicEntity boothZone = zones.Single(z => z.GetComponent<ReverbZoneComponent>().Preset == ReverbPreset.PaddedCell);
        Check(ReverbZoneComponent.Evaluate(scene.BasicEntities, boothZone.Position).Equals(FMOD.PRESET.PADDEDCELL()) &&
              ReverbZoneComponent.ActiveZones.Count == 2 &&
              ReverbZoneComponent.Evaluate(scene.BasicEntities, scene.MainCamera.Position).WetLevel <= -80,
            "the Padded Booth nests inside the Concert Hall and the start (outdoors) is dry");
        var clips = scene.BasicEntities.SelectMany(e => e.GetComponents<AudioComponent>()).ToList();
        Check(clips.Count >= 7 && clips.All(a => File.Exists(Path.Combine(content, a.ClipPath))) &&
              clips.Any(a => !a.Spatial) && clips.Any(a => a.Rolloff == AudioRolloff.Linear),
            "every sound in the audio sample exists, with 2D (stereo) and 3D (distance) emitters");
        Check(scene.MainCamera.Components.OfType<ScriptBehaviourComponent>().Any(s => s.ScriptId == AudioTestSceneScript.ScriptId) &&
              ScriptRegistry.Find(AudioTestSceneScript.ScriptId) != null &&
              File.Exists(Path.Combine(content, "UI", "AudioTest.xml")),
            "the Main Camera runs the registered Audio Test script with its HUD");

        // Walls station: rays against the scene's own Static wall colliders.
        using var system = new PhysicsSystem(new Vector3(0, 0, -9.81f));
        var physics = new ScenePhysics(system) { FallbackGeometry = CollisionChecks.World.BoxGeometry };
        physics.Update(scene.BasicEntities, 1 / 60f, simulate: false);
        BasicEntity radio = scene.BasicEntities.Single(e => e.Name == "Hut - Radio");
        Vector3 station = AudioTestSceneScript.Stations.Single(s => s.Name == "Walls").Position;
        Check(AudioOcclusion.CountWalls(physics, station, radio.Position, radio) == 2 &&
              AudioOcclusion.CountWalls(physics, new Vector3(-24.5f, 17, 1.7f), radio.Position, radio) == 1 &&
              AudioOcclusion.CountWalls(physics, new Vector3(-15, 22, 1.7f), radio.Position, radio) == 0,
            "the hut's radio is behind 2 walls from the Walls station, 1 round the free wall, none through the door");
        AcousticPath fromStation = AudioOcclusion.Trace(physics, station, radio.Position, radio);
        Check(Math.Abs(fromStation.Occlusion - (1 - 0.1f * 0.55f)) < 0.02f &&
              scene.BasicEntities.Single(e => e.Name == "Walls - Free Wall").GetComponent<MaterialComponent>() != null,
            $"from the Walls station the radio is behind concrete + wood: {fromStation.Occlusion:0.000} stopped, and M can recolour the free wall");
        BasicEntity racer = scene.BasicEntities.Single(e => e.Name == "Doppler Racer");
        Check(racer.GetComponent<AudioComponent>() is { Spatial: true, DopplerLevel: 1, PlayOnStart: false } &&
              Math.Abs(racer.Position.Y - -50) < 1e-3f, "the Doppler racer is a 3D engine sound on the y = -50 track");
    }

    /// <summary>
    /// The scripting controls against a running FMOD (called from Program's live <c>--audio</c> block, which owns
    /// the one FMOD instance a process can start). Master volume 0, so it is silent.
    /// </summary>
    public static void RunLive(AudioManager manager)
    {
        if (!manager.IsAvailable)
        {
            Console.WriteLine("SKIP: live FMOD scripting checks (FMOD did not start)");
            return;
        }
        float master = manager.MasterVolume;
        string root = manager.EditorContentRoot;
        manager.MasterVolume = 0;
        manager.EditorContentRoot = new EditorBridge().ContentSourceRoot;
        const string Pulse = "Audio/Test/pulse_loop.wav", Engine = "Audio/Test/engine_loop.wav";
        try
        {
            Check(manager.Preload(Pulse) && manager.Preload(Pulse, spatial: true) && !manager.Preload("Audio/Test/missing.wav"),
                "Preload loads clips for 2D and 3D and reports missing ones");

            // One sound: length, seek, fades.
            var sound = new ScriptSound(manager.PlaySound(Pulse, loop: true, contentRelativePath: true)!.Value);
            Check(Math.Abs(sound.Length - 2) < 0.01f, $"Length is the clip's length ({sound.Length:0.00} s)");
            sound.Time = 1f;
            Wait(manager, 30);
            Check(sound.Time > 0.95f && sound.Time < 1.3f, $"Time seeks ({sound.Time:0.00} s)");
            Channel(sound, out var channel);
            sound.FadeTo(0, 0.15f);
            Wait(manager, 300);
            Check(manager.FadeLevel(channel) == 0 && sound.IsPlaying, "FadeTo(0) silences the sound and keeps it playing");
            sound.FadeTo(1, 0.1f);
            Wait(manager, 250);
            Check(manager.FadeLevel(channel) == 1, "FadeTo(1) brings it back from where the last fade stopped");
            sound.FadeOut(0.1f);
            Wait(manager, 350);
            Check(!sound.IsPlaying, "FadeOut stops the sound when the fade ends");

            // Buses.
            var loop = new ScriptSound(manager.PlaySound(Pulse, loop: true, contentRelativePath: true)!.Value);
            Channel(loop, out var loopChannel);
            loopChannel.Native.getChannelGroup(out FMOD.ChannelGroup effects);
            manager.GetBusLevel(AudioBus.Effects);
            Wait(manager, 200);
            float level = AverageLevel(manager);
            Check(level > 0.01f, $"GetLevel meters the Effects bus ({level:0.000})");
            var bands = new float[16];
            manager.GetBusSpectrum(AudioBus.Effects, bands);
            Wait(manager, 300);
            Check(manager.GetBusSpectrum(AudioBus.Effects, bands) == 16 && bands.Max() > 0 && bands.Take(4).Max() > 0,
                "GetSpectrum fills log-spaced bands (the pulse's 110 Hz chord shows in the low bands)");
            manager.SetBusLowPass(AudioBus.Effects, 150);
            Wait(manager, 200);
            float muffled = AverageLevel(manager);
            Check(manager.GetBusLowPass(AudioBus.Effects) == 150 && muffled < level * 0.9f,
                $"SetLowPass muffles the bus ({level:0.000} -> {muffled:0.000} RMS)");
            manager.SetBusPitch(AudioBus.Effects, 0.5f);
            effects.getPitch(out float pitch);
            Check(pitch == 0.5f && manager.GetBusPitch(AudioBus.Effects) == 0.5f, "SetPitch slows the whole bus (slow motion)");
            manager.SetBusPaused(AudioBus.Effects, true);
            float pausedAt = loop.Time;
            Wait(manager, 200);
            Check(manager.IsBusPaused(AudioBus.Effects) && Math.Abs(loop.Time - pausedAt) < 0.01f && loop.IsPlaying,
                "SetPaused holds every sound on the bus where it is");
            manager.SetBusPaused(AudioBus.Effects, false);
            manager.FadeBus(AudioBus.Effects, 0, 0.1f);
            Wait(manager, 250);
            Check(manager.GetBusFade(AudioBus.Effects) == 0 && AverageLevel(manager) < 0.001f, "FadeBus fades the whole bus out");
            manager.DopplerScale = 2;
            FmodForFoxes.CoreSystem.Native.get3DSettings(out float doppler, out _, out _);
            Check(doppler == 2, "DopplerScale sets FMOD's doppler scale");
            manager.ResetMixing();
            effects.getPitch(out pitch);
            Check(!manager.IsBusPaused(AudioBus.Effects) && pitch == 1 && manager.GetBusLowPass(AudioBus.Effects) == AudioManager.LowPassOff &&
                  manager.GetBusFade(AudioBus.Effects) == 1 && manager.DopplerScale == 1,
                "ResetMixing (Play stopping) puts pause, pitch, low-pass, fade and Doppler back");
            Check(manager.Stats.Channels >= 1, $"Stats counts playing channels ({manager.Stats.Channels}, DSP {manager.Stats.DspCpu:0.0} %)");
            loop.Stop();

            // Music crossfade.
            var first = manager.PlayMusic(Pulse, contentRelativePath: true)!.Value;
            var second = manager.PlayMusic(Engine, contentRelativePath: true, crossfade: 0.2f)!.Value;
            Check(first.IsPlaying && second.IsPlaying && manager.FadeLevel(second) < 0.5f, "a crossfade plays both tracks, the new one fading in");
            Wait(manager, 450);
            Check(!first.IsPlaying && second.IsPlaying && manager.FadeLevel(second) == 1 && manager.IsMusicPlaying,
                "after the crossfade only the new track plays, at full level");
            manager.StopMusic();
        }
        finally
        {
            manager.ResetMixing();
            manager.MasterVolume = master;
            manager.EditorContentRoot = root;
        }
    }

    private static void Wait(AudioManager manager, int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            manager.SystemUpdate();
            Thread.Sleep(10);
        }
    }

    // The pulse's RMS jumps with its ticks: average 20 readings over 200 ms.
    private static float AverageLevel(AudioManager manager)
    {
        float sum = 0;
        for (int i = 0; i < 20; i++)
        {
            Wait(manager, 10);
            sum += manager.GetBusLevel(AudioBus.Effects);
        }
        return sum / 20;
    }

    private static void Channel(ScriptSound sound, out FmodForFoxes.Channel channel) =>
        channel = (FmodForFoxes.Channel)typeof(ScriptSound).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sound)!;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
