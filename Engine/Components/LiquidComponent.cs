using System.Text.Json.Serialization;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// Liquid inside a bottle, glass or jar, like Half-Life: Alyx's (Docs/Research/BottleLiquid): the gameobject's own
/// Forward Shaded (glass) mesh shows liquid below a surface plane that keeps level, leans and lags when the container
/// moves, wobbles and ripples when it's shaken and slowly settles, with a meniscus, a water line, foam and bubbles.
/// The fill follows the container's volume (<see cref="LiquidVolume"/>) as it tilts, lies on its side or turns
/// upside down. Works on any closed mesh whose material is Forward Shaded (the built-in Bottle). Runs in Edit and
/// Play (held while <see cref="GameFlow.Paused"/>); visual only, physics don't change.
/// </summary>
public sealed class LiquidComponent : GameComponent
{
    public const string TypeId = "liquid";

    /// <summary>How full the container is, 0 (empty) to 1 (brim).</summary>
    public float Fill { get; set; } = 0.6f;
    /// <summary>Colour of the liquid (alpha unused; see <see cref="Opacity"/>).</summary>
    [JsonConverter(typeof(Particles.ParticleColorJsonConverter))]
    public Color LiquidColor { get; set; } = new Color(196, 120, 34);
    /// <summary>Colour of the foam on top and of the bubbles.</summary>
    [JsonConverter(typeof(Particles.ParticleColorJsonConverter))]
    public Color FoamColor { get; set; } = new Color(245, 235, 210);
    /// <summary>How much the liquid hides what's behind it (water ~0.35, wine ~0.85, milk 1).</summary>
    public float Opacity { get; set; } = 0.75f;
    /// <summary>Surface roughness of the liquid (its top and the glass in front of it keep their own shine).</summary>
    public float Roughness { get; set; } = 0.1f;
    /// <summary>Lets the liquid glow (potions, chemicals); 0 = lit only.</summary>
    public float Emissive { get; set; }

    /// <summary>Seconds a shaken liquid takes to calm down (Alyx: 20).</summary>
    public float SettleTime { get; set; } = 20f;
    /// <summary>Seconds the foam and bubbles take to go (Alyx: 30).</summary>
    public float FoamSettleTime { get; set; } = 30f;
    /// <summary>How easily motion shakes it: 1 = Alyx; 0.5 = syrup, 2 = very runny. Scales the velocity change.</summary>
    public float Sensitivity { get; set; } = 1f;
    /// <summary>How much the surface leans while the container moves (Alyx: 1).</summary>
    public float Lean { get; set; } = 1f;
    /// <summary>How far the wobble and ripples swing (Alyx: 0.02).</summary>
    public float WobbleScale { get; set; } = 0.02f;
    /// <summary>Wobble speed (Alyx: 1; smaller containers slosh faster).</summary>
    public float WobbleSpeed { get; set; } = 1f;
    /// <summary>How much the surface climbs the glass at its edges (Alyx: 1).</summary>
    public float SurfaceTension { get; set; } = 1f;
    /// <summary>Brightness of the line where the surface meets the glass.</summary>
    public float WaterLine { get; set; } = 1f;
    /// <summary>How much foam shaking makes: 0 = none (water), 1 = Alyx, 3 = beer.</summary>
    public float Foaminess { get; set; } = 1f;
    /// <summary>Bubble amount: 0 = still, 1 = fizzy.</summary>
    public float Fizz { get; set; } = 0.3f;

    /// <summary>The liquid's state on the owner's <see cref="TransformMatrix.Liquid"/> while enabled.</summary>
    [JsonIgnore] public LiquidSlosh State { get; } = new LiquidSlosh();
    /// <summary>0..1: how shaken it is now.</summary>
    [JsonIgnore] public float Agitation => State.Agitation;
    /// <summary>0..1: how much foam it has now.</summary>
    [JsonIgnore] public float FoamAmount => State.Foam;

    /// <summary>Shakes the liquid as a sudden change of <paramref name="speed"/> m/s would (a hit, a stir).</summary>
    public void Agitate(float speed = 2f) => State.Agitate(speed);

    /// <summary>Calms the liquid at once.</summary>
    public void Settle() => State.Settle();

    public override void OnRemoved(BasicEntity owner)
    {
        base.OnRemoved(owner);
        Detach(owner);
    }

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        if (!Enabled) Detach(owner);
    }

    public override void OnStart(BasicEntity owner) => State.Settle();

    public override void OnStop() => State.Settle();

    /// <summary>
    /// Advances every enabled Liquid component of <paramref name="entities"/> by <paramref name="dt"/> seconds in a
    /// scene with <paramref name="gravity"/> (m/s²) and attaches its state to the gameobject's transform. Called
    /// each frame by MainSceneLogic in Edit and Play; pass dt = 0 to hold it.
    /// </summary>
    public static void UpdateAll(IReadOnlyList<BasicEntity> entities, float dt, Vector3 gravity)
    {
        if (entities == null) return;
        for (int i = 0; i < entities.Count; i++)
        {
            BasicEntity entity = entities[i];
            LiquidComponent component = entity?.GetComponent<LiquidComponent>();
            if (component == null)
            {
                if (entity?.WorldTransform.Liquid != null) entity.WorldTransform.Liquid = null;
                continue;
            }
            if (!component.Enabled || !entity.IsEnabled)
            {
                Detach(entity);
                continue;
            }
            component.Update(entity, dt, gravity);
        }
    }

    internal void Update(BasicEntity owner, float dt, Vector3 gravity)
    {
        LiquidSlosh state = State;
        state.Fill = Limit(Fill, 0, 1);
        state.SettleDuration = Limit(SettleTime, 0.1f, 600);
        state.FoamSettleDuration = Limit(FoamSettleTime, 0.1f, 600);
        // Alyx's dampening raises the velocity change to 1 - 0.4; sensitivity scales the change before it.
        float sensitivity = Limit(Sensitivity, 0, 10);
        state.MaxAgitation = 5f / MathF.Pow(System.Math.Max(sensitivity, 0.01f), 0.6f);
        state.FoamMaxAgitation = 200f / System.Math.Max(Limit(Foaminess, 0, 4), 0.01f);
        state.VelocityScale = Limit(Lean, 0, 5);
        state.WobbleScale = Limit(WobbleScale, 0, 0.5f);
        state.WobbleSpeed = Limit(WobbleSpeed, 0, 10);
        state.SurfaceTension = Limit(SurfaceTension, 0, 5);
        state.Phase = Hash(owner.Id);
        state.Update(owner.WorldTransform.World, gravity, dt, LiquidVolume.For(owner.ModelDefinition));
        owner.WorldTransform.Liquid = state;
        owner.WorldTransform.LiquidLook = new LiquidLook
        {
            Color = LiquidColor.ToVector3(),
            Foam = FoamColor.ToVector3(),
            Opacity = Limit(Opacity, 0, 1),
            Roughness = Limit(Roughness, 0.02f, 1),
            Emissive = Limit(Emissive, 0, 20),
            WaterLine = Limit(WaterLine, 0, 5),
            Foaminess = Limit(Foaminess, 0, 4),
            Fizz = Limit(Fizz, 0, 4),
        };
    }

    private static void Detach(BasicEntity owner)
    {
        if (owner?.WorldTransform.Liquid == null) return;
        owner.WorldTransform.Liquid = null;
    }

    private static float Limit(float value, float min, float max) =>
        float.IsFinite(value) ? System.Math.Clamp(value, min, max) : min;

    private static float Hash(int id)
    {
        float h = MathF.Sin(id * 12.9898f + 4.1414f) * 43758.547f;
        return h - MathF.Floor(h);
    }
}

/// <summary>The Liquid component's colours for Forward.fx, set per instance with <see cref="LiquidSlosh"/>.</summary>
public struct LiquidLook
{
    public Vector3 Color;
    public Vector3 Foam;
    public float Opacity;
    public float Roughness;
    public float Emissive;
    public float WaterLine;
    public float Foaminess;
    public float Fizz;
}
