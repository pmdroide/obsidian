using System.Text.Json.Serialization;
using Engine.Entities;
using Engine.Logic;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// Sways a tree, bush or grass gameobject in the scene wind (Environment > Wind Speed / Direction) on the GPU:
/// the trunk bends with gusts, branches bob, and leaves (materials of type Foliage) flutter. The motion is
/// computed in <c>Shaders/Common/FoliageWind.fx</c> from the vertex positions alone, so it works on any model
/// whose base is at its origin with Z up. Shadows and the editor selection move with it. Runs in Edit and Play
/// (held while <see cref="GameFlow.Paused"/>). Visual only: physics colliders stay at the rest pose.
/// </summary>
public sealed class FoliageWindComponent : GameComponent
{
    public const string TypeId = "foliage-wind";

    /// <summary>Height of the tree in model units; 0 = from the model's bounds. Bending is relative to it.</summary>
    public float Height { get; set; }
    /// <summary>How far the whole tree leans and sways (0 = a rigid trunk).</summary>
    public float TrunkBend { get; set; } = 1f;
    /// <summary>How much the limbs bob and swing, more towards their ends.</summary>
    public float BranchSway { get; set; } = 1f;
    /// <summary>How much leaves (Foliage material) flap; 0 keeps them still on their twigs.</summary>
    public float LeafFlutter { get; set; } = 1f;
    /// <summary>Scales the scene wind for this tree (a sheltered tree &lt; 1, an exposed one &gt; 1).</summary>
    public float WindScale { get; set; } = 1f;
    /// <summary>Wind in m/s that always reaches the tree, so it never freezes in a calm scene.</summary>
    public float MinWind { get; set; } = 1f;
    /// <summary>Speeds up or slows down the motion (heavier trees sway slower).</summary>
    public float Speed { get; set; } = 1f;

    [JsonIgnore] public float ClampedTrunkBend => Limit(TrunkBend, 0, 3);
    [JsonIgnore] public float ClampedBranchSway => Limit(BranchSway, 0, 3);
    [JsonIgnore] public float ClampedLeafFlutter => Limit(LeafFlutter, 0, 3);
    [JsonIgnore] public float ClampedWindScale => Limit(WindScale, 0, 5);
    [JsonIgnore] public float ClampedMinWind => Limit(MinWind, 0, 40);
    [JsonIgnore] public float ClampedSpeed => Limit(Speed, 0.05f, 5);

    /// <summary>The shader state on the owner's <see cref="TransformMatrix.Wind"/> while enabled.</summary>
    [JsonIgnore] public FoliageWind State { get; } = new FoliageWind();

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

    /// <summary>
    /// Advances every enabled Foliage Wind component of <paramref name="entities"/> by <paramref name="dt"/>
    /// seconds in <paramref name="wind"/> (m/s, world XY) and attaches its state to the gameobject's transform
    /// (scene loads attach components without <see cref="OnChanged"/>). Called each frame by MainSceneLogic in
    /// Edit and Play; pass dt = 0 to hold the motion.
    /// </summary>
    public static void UpdateAll(IReadOnlyList<BasicEntity> entities, float dt, Vector2 wind)
    {
        if (entities == null) return;
        for (int i = 0; i < entities.Count; i++)
        {
            BasicEntity entity = entities[i];
            FoliageWindComponent component = entity?.GetComponent<FoliageWindComponent>();
            if (component == null)
            {
                // A component removed without OnRemoved (e.g. a scene record edited away).
                if (entity?.WorldTransform.Wind != null) entity.WorldTransform.Wind = null;
                continue;
            }
            if (!component.Enabled || !entity.IsEnabled)
            {
                Detach(entity);
                continue;
            }
            component.Update(entity, dt, wind);
        }
    }

    internal void Update(BasicEntity owner, float dt, Vector2 wind)
    {
        FoliageWind state = State;
        float speed = wind.Length();
        Vector2 direction = speed > 0.001f ? wind / speed : new Vector2(0.7071f, 0.7071f);
        speed = Math.Max(speed * ClampedWindScale, ClampedMinWind);

        state.Height = ModelHeight(owner);
        state.Bend = ClampedTrunkBend;
        state.Branch = ClampedBranchSway;
        state.Flutter = ClampedLeafFlutter;
        state.Direction = direction;
        state.Strength = speed / FoliageWind.StrengthReferenceSpeed;
        // Neighbouring trees sway out of step.
        state.Phase = Hash(owner.Position);
        // Stronger wind moves everything faster; taller trees sway slower.
        float rate = ClampedSpeed * (0.7f + 0.45f * MathF.Min(state.Strength, 3f)) * MathF.Sqrt(8f / Math.Max(state.Height, 1f));
        state.Clock += dt * Math.Clamp(rate, 0.05f, 6f);
        // Keep float precision in long sessions: every wave in FoliageWind.fx has a period dividing 1000 s.
        if (state.Clock > 1000f) state.Clock -= 1000f;

        owner.WorldTransform.Wind = state;
        // The mesh moves every frame: redo culling and redraw the shadow maps.
        owner.WorldTransform.HasChanged = true;
    }

    /// <summary>World height of the tree: <see cref="Height"/> (or the model's top) times the Z scale.</summary>
    public float ModelHeight(BasicEntity owner)
    {
        float height = float.IsFinite(Height) && Height > 0 ? Height : owner?.BoundingBox.Max.Z ?? 0;
        if (height <= 0.01f) height = 1f;
        return height * Math.Max(Math.Abs(owner?.Scale.Z ?? 1f), 0.01f);
    }

    private static void Detach(BasicEntity owner)
    {
        if (owner?.WorldTransform.Wind == null) return;
        owner.WorldTransform.Wind = null;
        owner.WorldTransform.HasChanged = true;
    }

    private static float Limit(float value, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : min;

    private static float Hash(Vector3 p)
    {
        float h = MathF.Sin(p.X * 12.9898f + p.Y * 78.233f + p.Z * 37.719f) * 43758.547f;
        return h - MathF.Floor(h);
    }
}
