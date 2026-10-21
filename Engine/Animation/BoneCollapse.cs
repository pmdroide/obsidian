using System;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Animation
{
    /// <summary>
    /// Shrinks bones of a skinned gameobject (and every bone under them) to a point at the bone's joint, so the part
    /// of the mesh they carry disappears: the head of a zombie whose Breakable head was shot off. The bones are
    /// <see cref="BasicEntity.CollapsedBones"/> (by name, set during Play); the Animator, the Ragdoll and posed bones
    /// pass their skin matrices through <see cref="Apply"/> before skinning. Game thread only.
    /// </summary>
    internal static class BoneCollapse
    {
        // Shrinking to a tiny size instead of zero keeps the skinned normals finite.
        private const float Shrink = 1e-3f;

        [ThreadStatic] private static Matrix[] _scratch;
        [ThreadStatic] private static bool[] _collapsed;

        /// <summary><paramref name="skin"/> itself when nothing is collapsed, else a scratch copy with the collapsed bones shrunk.</summary>
        public static Matrix[] Apply(BasicEntity owner, SkinningData skeleton, Matrix[] skin)
        {
            if (owner == null || skeleton == null || skin == null || owner.CollapsedBones.Count == 0) return skin;
            int count = Math.Min(skin.Length, skeleton.BoneCount);
            if (_scratch == null || _scratch.Length < skin.Length) _scratch = new Matrix[skin.Length];
            if (_collapsed == null || _collapsed.Length < count) _collapsed = new bool[count];
            Array.Copy(skin, _scratch, skin.Length);

            bool any = false;
            for (int i = 0; i < count; i++)
            {
                int parent = skeleton.ParentIndices != null && i < skeleton.ParentIndices.Length ? skeleton.ParentIndices[i] : -1;
                // Bones come after their parents, so a parent's flag is already known.
                bool collapsed = owner.CollapsedBones.Contains(skeleton.BoneNames[i]);
                _collapsed[i] = collapsed;
                if (!collapsed && parent >= 0 && parent < i && _collapsed[parent]) _collapsed[i] = true;
                any |= _collapsed[i];
            }
            if (!any) return skin;

            for (int i = 0; i < count; i++)
            {
                if (!_collapsed[i]) continue;
                // The topmost collapsed bone of this one: everything under it shrinks to its joint.
                int root = i;
                while (true)
                {
                    int parent = skeleton.ParentIndices[root];
                    if (parent < 0 || parent >= count || !_collapsed[parent]) break;
                    root = parent;
                }
                Vector3 joint = Vector3.Transform(Matrix.Invert(skeleton.InverseBindPose[root]).Translation, skin[root]);
                _scratch[i] = skin[i] * Matrix.CreateTranslation(-joint) * Matrix.CreateScale(Shrink) * Matrix.CreateTranslation(joint);
            }
            return _scratch;
        }
    }
}
