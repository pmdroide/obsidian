using Engine.Animation;
using Microsoft.Xna.Framework;

namespace Engine.Entities
{
    /// <summary>
    /// A bone of a skinned gameobject (or its whole Armature) as something the editor can select and rotate:
    /// Anvil lists them under the gameobject in the Hierarchy, and the gizmo and Inspector write through
    /// <see cref="RotationMatrix"/> / <see cref="LocalRotation"/> into the owner's <see cref="BasicEntity.BonePose"/>,
    /// which deforms the mesh. Bones only rotate: position and scale follow the owner. Not part of the scene lists;
    /// its <see cref="Id"/> is derived from the owner's and the bone index (<see cref="IdFor"/>), never saved.
    /// </summary>
    public sealed class BoneObject : TransformableObject
    {
        /// <summary>Bone index of the Armature node (the whole skeleton).</summary>
        public const int ArmatureIndex = -1;
        /// <summary>Bones per gameobject the ID scheme can address.</summary>
        public const int MaxBones = 1023;

        public BasicEntity Owner { get; }
        public int BoneIndex { get; }
        public bool IsArmature => BoneIndex == ArmatureIndex;

        internal BoneObject(BasicEntity owner, int boneIndex)
        {
            Owner = owner;
            BoneIndex = boneIndex;
        }

        /// <summary>
        /// The editor ID of a gameobject's bone: negative (below the Main Camera's -1), so it never meets a scene
        /// ID, and decodable back to the gameobject and bone (<see cref="TryDecode"/>).
        /// </summary>
        public static int IdFor(int ownerId, int boneIndex)
        {
            long id = -(2L + (long)ownerId * (MaxBones + 1) + boneIndex + 1);
            return ownerId > 0 && boneIndex >= ArmatureIndex && boneIndex < MaxBones && id >= int.MinValue ? (int)id : 0;
        }

        public static bool TryDecode(int id, out int ownerId, out int boneIndex)
        {
            long n = -(long)id - 2;
            ownerId = (int)(n / (MaxBones + 1));
            boneIndex = (int)(n % (MaxBones + 1)) - 1;
            return n >= 0 && ownerId > 0;
        }

        public SkinningData Skeleton => SkinningData.From(Owner.Model);

        /// <summary>Owner's world matrix from its current transform (WorldTransform.World can lag a frame behind).</summary>
        internal static Matrix WorldOf(BasicEntity owner) =>
            Matrix.CreateScale(owner.Scale) * owner.RotationMatrix * Matrix.CreateTranslation(owner.Position);

        // The bone's frame in world space: its posed model transform (the Armature: its rotation) times the owner's.
        private Matrix WorldFrame()
        {
            Matrix world = WorldOf(Owner);
            if (IsArmature) return Matrix.CreateFromQuaternion(Owner.BonePose.Get(Skeleton, ArmatureIndex)) * world;
            Matrix[] bones = Owner.BonePose.ModelBones(Owner);
            return bones != null && BoneIndex < bones.Length ? bones[BoneIndex] * world : world;
        }

        /// <summary>World position of the joint (the Armature: the gameobject's origin). Read-only.</summary>
        public override Vector3 Position
        {
            get => IsArmature ? Owner.Position : WorldFrame().Translation;
            set { }
        }

        /// <summary>
        /// World rotation of the bone, as the gizmo sees it. Setting it re-poses the bone so it ends up there
        /// (its children follow); the change is kept as a rotation in the bone's own space.
        /// </summary>
        public override Matrix RotationMatrix
        {
            get => RotationOf(WorldFrame());
            set
            {
                // The bone's world rotation is (pose rotation) * (everything above it), so turning it from the
                // current world rotation to `value` multiplies the pose rotation by value * current⁻¹ on the left.
                Matrix current = RotationMatrix;
                Matrix posed = Matrix.CreateFromQuaternion(Owner.BonePose.Get(Skeleton, BoneIndex));
                Matrix next = RotationOf(value) * Matrix.Transpose(current) * posed;
                Owner.BonePose.Set(Skeleton, BoneIndex, Quaternion.CreateFromRotationMatrix(next));
            }
        }

        /// <summary>The bone's rotation away from its rest pose, in its own space (the Inspector's Rotation).</summary>
        public Matrix LocalRotation
        {
            get => Matrix.CreateFromQuaternion(Owner.BonePose.Get(Skeleton, BoneIndex));
            set => Owner.BonePose.Set(Skeleton, BoneIndex, Quaternion.CreateFromRotationMatrix(RotationOf(value)));
        }

        public bool IsPosed => !Owner.BonePose.Get(Skeleton, BoneIndex).Equals(Quaternion.Identity);

        /// <summary>Puts this bone back in its rest pose.</summary>
        public void ResetBone() => Owner.BonePose.Reset(Skeleton, BoneIndex);

        /// <summary>Puts every bone of the gameobject back in its rest pose.</summary>
        public void ResetPose() => Owner.BonePose.Clear();

        private static Matrix RotationOf(Matrix matrix)
        {
            if (!matrix.Decompose(out _, out Quaternion rotation, out _)) return Matrix.Identity;
            return Matrix.CreateFromQuaternion(Quaternion.Normalize(rotation));
        }

        public override Vector3 Scale { get => Vector3.One; set { } }

        public override int Id
        {
            get => IdFor(Owner.Id, BoneIndex);
            set { }
        }

        public override bool IsEnabled { get => Owner.IsEnabled; set { } }

        public override TransformableObject Clone => new BoneObject(Owner, BoneIndex);

        public override string Name
        {
            get
            {
                if (IsArmature) return "Armature";
                SkinningData skeleton = Skeleton;
                return skeleton != null && BoneIndex < skeleton.BoneCount ? skeleton.BoneNames[BoneIndex] : "Bone " + BoneIndex;
            }
            set { }
        }
    }
}
