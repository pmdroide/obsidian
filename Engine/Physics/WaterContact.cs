using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>
    /// A dynamic body crossing the surface of a <see cref="GameObjectRole.Water"/> gameobject. Passed to a script's
    /// OnWaterEnter/OnWaterExit and to <see cref="ScenePhysics.WaterEntered"/>/<see cref="ScenePhysics.WaterExited"/>;
    /// <see cref="Particles.WaterSplashes"/> turns it into a splash.
    /// </summary>
    public struct WaterContact
    {
        /// <summary>The gameobject that went in or came out.</summary>
        public BasicEntity GameObject;
        /// <summary>Where it crossed the (moving) surface: under the collider's centre, at the water's height.</summary>
        public Vector3 Point;
        /// <summary>The body's velocity as it crossed, m/s.</summary>
        public Vector3 Velocity;
        /// <summary>m/s downwards into the water on enter, upwards out of it on exit (0 when it drifted across).</summary>
        public float Speed;
        /// <summary>Horizontal half-size of the collider in metres: how wide a splash it makes.</summary>
        public float Radius;
        /// <summary>The body's mass.</summary>
        public float Mass;
    }
}
