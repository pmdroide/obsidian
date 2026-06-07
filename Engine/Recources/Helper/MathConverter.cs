using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;
using Quaternion = Microsoft.Xna.Framework.Quaternion;
using Vector3 = Microsoft.Xna.Framework.Vector3;

namespace Engine.Recources.Helper
{
    /// <summary>
    /// Converts between MonoGame/XNA math types and the <see cref="System.Numerics"/>
    /// types used by BEPUphysics v2. BEPU v2 (unlike the old v1 BEPUutilities types)
    /// speaks System.Numerics directly, so this is the only conversion the engine
    /// needs. Kept centralized so <c>Engine.Physics.PhysicsSystem</c> stays the single
    /// BEPU seam.
    /// </summary>
    public static class MathConverter
    {
        //Vector3
        public static NumVector3 ToNumerics(Vector3 v)
        {
            return new NumVector3(v.X, v.Y, v.Z);
        }

        public static Vector3 ToXna(NumVector3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        //Quaternion
        public static NumQuaternion ToNumerics(Quaternion q)
        {
            return new NumQuaternion(q.X, q.Y, q.Z, q.W);
        }

        public static Quaternion ToXna(NumQuaternion q)
        {
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }
    }
}
