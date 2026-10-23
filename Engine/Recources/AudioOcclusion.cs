using Engine.Components;
using Engine.Entities;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Recources
{
    /// <summary>
    /// What a collider is made of, for sound passing through it (Physics component Sound Material).
    /// Saved by name, so never rename a value.
    /// </summary>
    public enum SoundMaterial
    {
        /// <summary>A generic wall: blocks <see cref="AudioManager.WallOcclusion"/> (0.6).</summary>
        Default,
        Wood,
        Plaster,
        Glass,
        Metal,
        Brick,
        Concrete,
        Stone,
        Fabric,
        Foliage,
        /// <summary>The Physics component's own Sound Block / Sound Dullness.</summary>
        Custom,
    }

    /// <summary>How a wall of one material changes sound passing through it.</summary>
    /// <param name="Block">0..1 of the direct sound it stops (quieter).</param>
    /// <param name="Dullness">0..1 of the treble it takes away on top (muffled): bass gets through thick walls, treble doesn't.</param>
    public readonly record struct SoundMaterialProperties(float Block, float Dullness);

    /// <summary>Muffling along the paths from the listener to a sound (averaged over the rays).</summary>
    /// <param name="Walls">Walls passed through (average over the rays).</param>
    /// <param name="Occlusion">0..1 of the direct sound the walls stop together.</param>
    /// <param name="Dullness">0..1 of extra treble loss the walls cause together.</param>
    public readonly record struct AcousticPath(float Walls, float Occlusion, float Dullness)
    {
        public static readonly AcousticPath Open = new(0, 0, 0);
    }

    /// <summary>
    /// Wall muffling for 3D sounds: rays from the listener to the emitter against the static colliders
    /// (walls, floors, static props; dynamic bodies and triggers are see-through). Each gameobject a ray
    /// passes through is one wall, of its Physics component's <see cref="SoundMaterial"/>: a wooden door
    /// lets more through than concrete, metal rings through brighter than brick. A centre ray plus two rays
    /// aimed beside the emitter (<see cref="SpreadRadius"/>) are averaged, so a sound just inside a doorway
    /// is only partly muffled.
    /// </summary>
    public static class AudioOcclusion
    {
        /// <summary>Metres beside the emitter the side rays aim at: how far sound "bends" around an opening.</summary>
        public const float SpreadRadius = 0.6f;
        public const int MaxWalls = 3;

        /// <summary>
        /// Block / dullness per material. Real walls: concrete and stone stop nearly everything but a bass
        /// rumble, wood and plaster let voices through muffled, glass and metal sheets stay clearer (they ring),
        /// curtains and hedges barely matter. Default's block is the scene-wide <paramref name="defaultBlock"/>.
        /// </summary>
        public static SoundMaterialProperties PropertiesOf(SoundMaterial material, float defaultBlock = 0.6f) => material switch
        {
            SoundMaterial.Wood => new(0.45f, 0.6f),
            SoundMaterial.Plaster => new(0.5f, 0.55f),
            SoundMaterial.Glass => new(0.4f, 0.3f),
            SoundMaterial.Metal => new(0.7f, 0.25f),
            SoundMaterial.Brick => new(0.8f, 0.75f),
            SoundMaterial.Concrete => new(0.9f, 0.85f),
            SoundMaterial.Stone => new(0.92f, 0.8f),
            SoundMaterial.Fabric => new(0.15f, 0.3f),
            SoundMaterial.Foliage => new(0.08f, 0.15f),
            _ => new(MathHelper.Clamp(defaultBlock, 0, 1), 0.5f),
        };

        /// <summary>
        /// What the hit gameobject's collider does to sound: its Physics component's material (Custom = its own
        /// values); colliders without one (the voxel world) count as Default.
        /// </summary>
        public static SoundMaterialProperties PropertiesOf(BasicEntity wall, float defaultBlock = 0.6f)
        {
            PhysicsComponent physics = wall?.GetComponent<PhysicsComponent>();
            if (physics == null) return PropertiesOf(SoundMaterial.Default, defaultBlock);
            if (physics.SoundMaterial == SoundMaterial.Custom)
                return new(Clamp01(physics.SoundBlock), Clamp01(physics.SoundDullness));
            return PropertiesOf(physics.SoundMaterial, defaultBlock);
        }

        private static float Clamp01(float v) => float.IsFinite(v) ? MathHelper.Clamp(v, 0, 1) : 0;

        /// <summary>
        /// Muffling between <paramref name="listener"/> and <paramref name="emitter"/>, averaged over the centre
        /// ray and two rays beside the emitter.
        /// </summary>
        public static AcousticPath Trace(ScenePhysics physics, Vector3 listener, Vector3 emitter, BasicEntity ignore = null,
                                         float defaultBlock = 0.6f)
        {
            if (physics == null) return AcousticPath.Open;
            Vector3 toEmitter = emitter - listener;
            if (toEmitter.LengthSquared() < 0.01f) return AcousticPath.Open;
            // Side offsets perpendicular to the ray, in the horizontal plane (vertical when the ray is vertical).
            Vector3 side = Vector3.Cross(toEmitter, Vector3.UnitZ);
            if (side.LengthSquared() < 1e-6f) side = Vector3.UnitX;
            side = Vector3.Normalize(side) * SpreadRadius;
            var a = TraceRay(physics, listener, emitter, ignore, defaultBlock);
            var b = TraceRay(physics, listener, emitter + side, ignore, defaultBlock);
            var c = TraceRay(physics, listener, emitter - side, ignore, defaultBlock);
            // Average what gets through (not the walls): one clear side ray lets a lot of sound past a door edge.
            return new AcousticPath((a.Walls + b.Walls + c.Walls) / 3f,
                1 - (a.Transmission + b.Transmission + c.Transmission) / 3f,
                1 - (a.Clarity + b.Clarity + c.Clarity) / 3f);
        }

        /// <summary>Average number of walls between <paramref name="listener"/> and <paramref name="emitter"/> (0..MaxWalls).</summary>
        public static float Walls(ScenePhysics physics, Vector3 listener, Vector3 emitter, BasicEntity ignore = null) =>
            Trace(physics, listener, emitter, ignore).Walls;

        /// <summary>Gameobjects with static colliders a straight line from <paramref name="from"/> to <paramref name="to"/> passes through.</summary>
        public static int CountWalls(ScenePhysics physics, Vector3 from, Vector3 to, BasicEntity ignore = null) =>
            TraceRay(physics, from, to, ignore, 0.6f).Walls;

        /// <summary>
        /// One ray: the walls it passes and the fractions of the sound (Transmission) and of its treble (Clarity)
        /// that get through all of them.
        /// </summary>
        private static (int Walls, float Transmission, float Clarity) TraceRay(ScenePhysics physics, Vector3 from, Vector3 to,
                                                                               BasicEntity ignore, float defaultBlock)
        {
            if (physics == null) return (0, 1, 1);
            Vector3 direction = to - from;
            float length = direction.Length();
            if (length < 0.1f) return (0, 1, 1);
            direction /= length;

            int walls = 0;
            float transmission = 1, clarity = 1;
            BasicEntity last = null;
            bool any = false;
            float travelled = 0, step = 0.02f;
            // Every hit (entering or leaving a mesh, or starting inside a convex) moves the ray on;
            // a wall counts once however many of its faces the ray meets.
            for (int i = 0; i < 48 && walls < MaxWalls; i++)
            {
                float remaining = length - travelled - 0.05f; // stop just short of the emitter
                if (remaining <= 0) break;
                if (!physics.RaycastStatic(from + direction * travelled, direction, remaining, out RaycastHit hit)) break;
                if (hit.GameObject != ignore && (!any || hit.GameObject != last))
                {
                    walls++;
                    last = hit.GameObject;
                    any = true;
                    SoundMaterialProperties material = PropertiesOf(hit.GameObject, defaultBlock);
                    transmission *= 1 - material.Block;
                    clarity *= 1 - material.Dullness;
                }
                // Inside a convex the hit is at 0: step out faster through thick walls.
                step = hit.Distance < 1e-3f ? Math.Min(step * 2, 1f) : 0.02f;
                travelled += hit.Distance + step;
            }
            return (walls, transmission, clarity);
        }

        /// <summary>FMOD direct-path occlusion (0..1) for <paramref name="walls"/> walls each blocking <paramref name="perWall"/>.</summary>
        public static float OcclusionFor(float walls, float perWall) =>
            walls <= 0 ? 0 : 1 - MathF.Pow(1 - MathHelper.Clamp(perWall, 0, 1), walls);
    }
}
