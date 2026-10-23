using System.Collections.Generic;
using System.Text.Json.Serialization;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// FMOD reverb environment presets (FMOD.PRESET). Saved by name (Reverb Zone Preset), so never rename a value.
/// </summary>
public enum ReverbPreset
{
    Generic, PaddedCell, Room, Bathroom, LivingRoom, StoneRoom, Auditorium, ConcertHall, Cave, Arena, Hangar,
    CarpetedHallway, Hallway, StoneCorridor, Alley, Forest, City, Mountains, Quarry, Plain, ParkingLot, SewerPipe,
    Underwater,
}

/// <summary>
/// Reverb for an area: while the listener (the active camera) is inside the gameobject's box (a Cube with its
/// position, rotation and scale; the mesh is never drawn) every sound gets this zone's reverb, fading in over
/// <see cref="BlendDistance"/> outside the box. Zones blend in Priority order, the highest last; on equal priority
/// the smaller box goes last, so a bathroom inside a hall wins without setting a priority. <see cref="Infinite"/>
/// zones apply everywhere (an outdoor ambience under the rooms). Works in the editor and in Play. How much reverb
/// a sound gets is its Audio component's Reverb Send.
/// </summary>
public sealed class ReverbZoneComponent : GameComponent
{
    public const string TypeId = "reverb-zone";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReverbPreset Preset { get; set; } = ReverbPreset.Room;
    /// <summary>Applies everywhere instead of inside the box.</summary>
    public bool Infinite { get; set; }
    /// <summary>Metres outside the box over which the reverb fades in.</summary>
    public float BlendDistance { get; set; } = 1;
    /// <summary>0..1: how much of the zone's reverb is used at full strength.</summary>
    public float Weight { get; set; } = 1;
    /// <summary>Zones are blended lowest priority first; the highest wins where they overlap.</summary>
    public int Priority { get; set; }
    /// <summary>Multiplies the preset's decay time (tail length): 2 = twice as long.</summary>
    public float DecayScale { get; set; } = 1;
    /// <summary>Added to the preset's wet level, in dB: +6 = about twice as loud, -6 = half.</summary>
    public float WetLevel { get; set; }

    [JsonIgnore] public float ClampedWeight => float.IsFinite(Weight) ? MathHelper.Clamp(Weight, 0, 1) : 0;

    /// <summary>The zones that shaped the last evaluated reverb with their weights (0..1), in blend order.</summary>
    public static IReadOnlyList<(BasicEntity Zone, float Weight)> ActiveZones => Active;
    private static readonly List<(BasicEntity Zone, float Weight)> Active = new();

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        PostProcessVolumeComponent.SetMeshHidden(owner, Enabled && owner?.IsEnabled == true);
    }

    public override void OnRemoved(BasicEntity owner)
    {
        base.OnRemoved(owner);
        PostProcessVolumeComponent.SetMeshHidden(owner, false);
    }

    /// <summary>Weight of this zone for a listener at <paramref name="position"/>: full inside, fading out over Blend Distance.</summary>
    public float WeightAt(BasicEntity owner, Vector3 position)
    {
        float weight = ClampedWeight;
        if (Infinite || weight <= 0 || owner == null) return weight;
        Vector3 scale = owner.Scale;
        if (Math.Abs(scale.X) < 1e-4f || Math.Abs(scale.Y) < 1e-4f || Math.Abs(scale.Z) < 1e-4f) return 0;
        // Distance outside the box in world metres: box space is the unit cube, so scale the overshoot back
        Vector3 p = Vector3.Transform(position, Matrix.Invert(FogVolumeComponent.BoxWorld(owner)));
        Vector3 outside = Vector3.Max(new Vector3(Math.Abs(p.X), Math.Abs(p.Y), Math.Abs(p.Z)) - Vector3.One, Vector3.Zero);
        float distance = (outside * new Vector3(Math.Abs(scale.X), Math.Abs(scale.Y), Math.Abs(scale.Z))).Length();
        if (distance <= 0) return weight;
        float blend = float.IsFinite(BlendDistance) ? Math.Max(BlendDistance, 0) : 0;
        return blend <= 0 ? 0 : weight * MathHelper.Clamp(1 - distance / blend, 0, 1);
    }

    /// <summary>The preset with this zone's Decay Scale and Wet Level applied.</summary>
    public FMOD.REVERB_PROPERTIES Properties
    {
        get
        {
            FMOD.REVERB_PROPERTIES p = PresetProperties(Preset);
            float decay = float.IsFinite(DecayScale) ? MathHelper.Clamp(DecayScale, 0.1f, 10f) : 1f;
            p.DecayTime = MathHelper.Clamp(p.DecayTime * decay, 100f, 20000f);
            if (float.IsFinite(WetLevel)) p.WetLevel = MathHelper.Clamp(p.WetLevel + WetLevel, -80f, 20f);
            return p;
        }
    }

    /// <summary>
    /// The reverb a listener at <paramref name="listener"/> hears from the enabled zones of <paramref name="entities"/>;
    /// <see cref="FMOD.PRESET.OFF"/> outside every zone. Keeps the zones' meshes hidden (scene loads attach
    /// components without <see cref="OnChanged"/>) and fills <see cref="ActiveZones"/>.
    /// </summary>
    public static FMOD.REVERB_PROPERTIES Evaluate(IReadOnlyList<BasicEntity> entities, Vector3 listener)
    {
        Active.Clear();
        var zones = new List<(BasicEntity Entity, ReverbZoneComponent Zone, float Weight, float Size)>();
        for (int i = 0; entities != null && i < entities.Count; i++)
        {
            BasicEntity entity = entities[i];
            ReverbZoneComponent zone = entity?.GetComponent<ReverbZoneComponent>();
            if (zone == null) continue;
            bool active = zone.Enabled && entity.IsEnabled;
            PostProcessVolumeComponent.SetMeshHidden(entity, active);
            if (!active) continue;
            float weight = zone.WeightAt(entity, listener);
            if (weight <= 0) continue;
            Vector3 s = entity.Scale;
            zones.Add((entity, zone, weight, zone.Infinite ? float.MaxValue : Math.Abs(s.X * s.Y * s.Z)));
        }
        if (zones.Count == 0) return FMOD.PRESET.OFF();
        // Lowest priority first, and the bigger zone first on a tie, so nested rooms win.
        zones.Sort((a, b) => a.Zone.Priority != b.Zone.Priority ? a.Zone.Priority.CompareTo(b.Zone.Priority) : b.Size.CompareTo(a.Size));

        // The room's character (decay, filters...) blends between zones; the wet level blends as a gain so
        // fading a zone in from silence is gradual instead of jumping from -80 dB.
        FMOD.REVERB_PROPERTIES result = default;
        float wetGain = 0, presence = 0;
        bool exact = false; // the last zone is at full weight: keep its own dB value (no float round trip)
        foreach (var z in zones)
        {
            FMOD.REVERB_PROPERTIES p = z.Zone.Properties;
            result = presence <= 0 || z.Weight >= 1 ? p : Lerp(result, p, z.Weight);
            wetGain = MathHelper.Lerp(wetGain, DbToGain(p.WetLevel), z.Weight);
            presence = MathHelper.Lerp(presence, 1, z.Weight);
            exact = z.Weight >= 1;
            Active.Add((z.Entity, z.Weight));
        }
        if (!exact) result.WetLevel = wetGain > 1e-4f ? MathHelper.Clamp(20f * MathF.Log10(wetGain), -80f, 20f) : -80f;
        return result;
    }

    private static float DbToGain(float db) => db <= -80f ? 0 : MathF.Pow(10f, db / 20f);

    private static FMOD.REVERB_PROPERTIES Lerp(FMOD.REVERB_PROPERTIES a, FMOD.REVERB_PROPERTIES b, float t) => new(
        MathHelper.Lerp(a.DecayTime, b.DecayTime, t), MathHelper.Lerp(a.EarlyDelay, b.EarlyDelay, t),
        MathHelper.Lerp(a.LateDelay, b.LateDelay, t), MathHelper.Lerp(a.HFReference, b.HFReference, t),
        MathHelper.Lerp(a.HFDecayRatio, b.HFDecayRatio, t), MathHelper.Lerp(a.Diffusion, b.Diffusion, t),
        MathHelper.Lerp(a.Density, b.Density, t), MathHelper.Lerp(a.LowShelfFrequency, b.LowShelfFrequency, t),
        MathHelper.Lerp(a.LowShelfGain, b.LowShelfGain, t), MathHelper.Lerp(a.HighCut, b.HighCut, t),
        MathHelper.Lerp(a.EarlyLateMix, b.EarlyLateMix, t), MathHelper.Lerp(a.WetLevel, b.WetLevel, t));

    public static FMOD.REVERB_PROPERTIES PresetProperties(ReverbPreset preset) => preset switch
    {
        ReverbPreset.PaddedCell => FMOD.PRESET.PADDEDCELL(),
        ReverbPreset.Room => FMOD.PRESET.ROOM(),
        ReverbPreset.Bathroom => FMOD.PRESET.BATHROOM(),
        ReverbPreset.LivingRoom => FMOD.PRESET.LIVINGROOM(),
        ReverbPreset.StoneRoom => FMOD.PRESET.STONEROOM(),
        ReverbPreset.Auditorium => FMOD.PRESET.AUDITORIUM(),
        ReverbPreset.ConcertHall => FMOD.PRESET.CONCERTHALL(),
        ReverbPreset.Cave => FMOD.PRESET.CAVE(),
        ReverbPreset.Arena => FMOD.PRESET.ARENA(),
        ReverbPreset.Hangar => FMOD.PRESET.HANGAR(),
        ReverbPreset.CarpetedHallway => FMOD.PRESET.CARPETTEDHALLWAY(),
        ReverbPreset.Hallway => FMOD.PRESET.HALLWAY(),
        ReverbPreset.StoneCorridor => FMOD.PRESET.STONECORRIDOR(),
        ReverbPreset.Alley => FMOD.PRESET.ALLEY(),
        ReverbPreset.Forest => FMOD.PRESET.FOREST(),
        ReverbPreset.City => FMOD.PRESET.CITY(),
        ReverbPreset.Mountains => FMOD.PRESET.MOUNTAINS(),
        ReverbPreset.Quarry => FMOD.PRESET.QUARRY(),
        ReverbPreset.Plain => FMOD.PRESET.PLAIN(),
        ReverbPreset.ParkingLot => FMOD.PRESET.PARKINGLOT(),
        ReverbPreset.SewerPipe => FMOD.PRESET.SEWERPIPE(),
        ReverbPreset.Underwater => FMOD.PRESET.UNDERWATER(),
        _ => FMOD.PRESET.GENERIC(),
    };
}
