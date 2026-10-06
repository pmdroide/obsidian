using System;
using Microsoft.Xna.Framework;

namespace Engine.Animation
{
    /// <summary>
    /// Samples a clip onto a skeleton and produces skin matrices. The clip may come from another
    /// model with the same bone names (e.g. two Mixamo characters). Then each bone gets the
    /// source bone's model-space rotation change from its bind pose, so differing rest poses
    /// don't leak into the motion; the target keeps its own bone lengths, and the root bone's
    /// translation is scaled by the ratio of the two skeletons' root heights.
    /// </summary>
    public sealed class AnimationPlayer
    {
        public SkinningData Skeleton { get; }
        public AnimationClip Clip { get; private set; }
        /// <summary>True when the clip belongs to a different skeleton and is mapped by bone name.</summary>
        public bool IsRetargeted { get; private set; }

        /// <summary>Removes the root bone's horizontal travel (root motion), keeping its bob.</summary>
        public bool InPlace;

        /// <summary>Model-space skin matrices (inverse bind * posed bone), indexed like BlendIndices.</summary>
        public Matrix[] SkinTransforms { get; }
        /// <summary>Model-space bone transforms of the last <see cref="Evaluate"/>.</summary>
        public Matrix[] BoneTransforms { get; }

        private readonly Matrix[] _local;
        private readonly Vector3[] _bindTranslation;
        private readonly Vector3[] _bindScale;
        private readonly Quaternion[] _bindRotation;
        private readonly Quaternion[] _bindGlobalRotation;
        private readonly Quaternion[] _globalRotation;
        private readonly Quaternion _rootRotation;
        private readonly int _root;
        private Vector3 _up = Vector3.UnitZ;

        // Clip mapping. Same skeleton: track -> bone. Retargeted: target bone -> source bone.
        private int[] _trackBone = Array.Empty<int>();
        private SkinningData _source;
        private int[] _sourceBone = Array.Empty<int>();
        private Quaternion[] _sourceBindLocal, _sourceBindGlobal, _sourceLocal, _sourceGlobal;
        private Quaternion _sourceRootRotation = Quaternion.Identity;
        private int _sourceRoot = -1;
        private float _rootScale = 1f;

        public AnimationPlayer(SkinningData skeleton)
        {
            Skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
            int count = skeleton.BoneCount;
            _local = new Matrix[count];
            SkinTransforms = new Matrix[count];
            BoneTransforms = new Matrix[count];
            _bindTranslation = new Vector3[count];
            _bindScale = new Vector3[count];
            _bindRotation = new Quaternion[count];
            _bindGlobalRotation = new Quaternion[count];
            _globalRotation = new Quaternion[count];

            _rootRotation = RotationOf(skeleton.RootTransform);
            for (int i = 0; i < count; i++)
            {
                skeleton.BindPose[i].Decompose(out _bindScale[i], out _bindRotation[i], out _bindTranslation[i]);
                SkinTransforms[i] = Matrix.Identity;
            }
            Globals(skeleton, _bindRotation, _rootRotation, _bindGlobalRotation);

            _root = Array.IndexOf(skeleton.ParentIndices, -1);
            if (_root >= 0 && _bindTranslation[_root].LengthSquared() > 1e-8f)
                _up = Vector3.Normalize(_bindTranslation[_root]); // the root (hips) sits above the origin
        }

        /// <summary>Plays <paramref name="clip"/>, which belongs to <paramref name="clipSkeleton"/>.</summary>
        public void SetClip(AnimationClip clip, SkinningData clipSkeleton)
        {
            Clip = clip;
            clipSkeleton ??= Skeleton;
            IsRetargeted = !ReferenceEquals(clipSkeleton, Skeleton);
            _source = clipSkeleton;
            _rootScale = 1f;

            if (!IsRetargeted)
            {
                _trackBone = new int[clip?.Tracks.Length ?? 0];
                for (int t = 0; t < _trackBone.Length; t++)
                {
                    int bone = clip.Tracks[t].BoneIndex;
                    _trackBone[t] = bone >= 0 && bone < Skeleton.BoneCount ? bone : -1;
                }
                return;
            }

            int sourceCount = clipSkeleton.BoneCount;
            _sourceBindLocal = new Quaternion[sourceCount];
            _sourceBindGlobal = new Quaternion[sourceCount];
            _sourceLocal = new Quaternion[sourceCount];
            _sourceGlobal = new Quaternion[sourceCount];
            for (int j = 0; j < sourceCount; j++)
                _sourceBindLocal[j] = RotationOf(clipSkeleton.BindPose[j]);
            _sourceRootRotation = RotationOf(clipSkeleton.RootTransform);
            Globals(clipSkeleton, _sourceBindLocal, _sourceRootRotation, _sourceBindGlobal);

            _sourceBone = new int[Skeleton.BoneCount];
            for (int i = 0; i < _sourceBone.Length; i++)
                _sourceBone[i] = clipSkeleton.FindBone(Skeleton.BoneNames[i]);

            _sourceRoot = _root >= 0 ? _sourceBone[_root] : -1;
            if (_sourceRoot >= 0)
            {
                float sourceHeight = clipSkeleton.BindPose[_sourceRoot].Translation.Length();
                if (sourceHeight > 1e-4f) _rootScale = _bindTranslation[_root].Length() / sourceHeight;
            }
        }

        /// <summary>Poses the skeleton at <paramref name="time"/> seconds into the clip.</summary>
        public void Evaluate(float time)
        {
            if (Clip == null) Array.Copy(Skeleton.BindPose, _local, _local.Length);
            else if (IsRetargeted) PoseRetargeted(time);
            else PoseDirect(time);

            for (int i = 0; i < _local.Length; i++)
            {
                int parent = Skeleton.ParentIndices[i];
                BoneTransforms[i] = _local[i] * (parent >= 0 ? BoneTransforms[parent] : Skeleton.RootTransform);
                SkinTransforms[i] = Skeleton.InverseBindPose[i] * BoneTransforms[i];
            }
        }

        private void PoseDirect(float time)
        {
            Array.Copy(Skeleton.BindPose, _local, _local.Length);
            for (int t = 0; t < _trackBone.Length; t++)
            {
                int bone = _trackBone[t];
                if (bone < 0) continue;
                BoneTrack track = Clip.Tracks[t];
                track.Sample(time, out Vector3 translation, out Quaternion rotation, out Vector3 scale);
                if (bone == _root && InPlace) translation = KeepVertical(translation, track.Translations[0]);
                _local[bone] = Compose(scale, rotation, translation);
            }
        }

        private void PoseRetargeted(float time)
        {
            Array.Copy(_sourceBindLocal, _sourceLocal, _sourceLocal.Length);
            Vector3 rootTranslation = _root >= 0 ? _bindTranslation[_root] : Vector3.Zero;
            Vector3 rootStart = rootTranslation;
            foreach (BoneTrack track in Clip.Tracks)
            {
                int bone = track.BoneIndex;
                if (bone < 0 || bone >= _sourceLocal.Length) continue;
                track.Sample(time, out Vector3 translation, out Quaternion rotation, out _);
                _sourceLocal[bone] = rotation;
                if (bone == _sourceRoot)
                {
                    rootTranslation = translation * _rootScale;
                    rootStart = track.Translations[0] * _rootScale;
                }
            }
            Globals(_source, _sourceLocal, _sourceRootRotation, _sourceGlobal);

            for (int i = 0; i < _local.Length; i++)
            {
                int parent = Skeleton.ParentIndices[i];
                Quaternion parentGlobal = parent >= 0 ? _globalRotation[parent] : _rootRotation;
                int j = _sourceBone[i];
                // Target bind, then the source bone's model-space change from its own bind.
                _globalRotation[i] = j < 0
                    ? Quaternion.Concatenate(_bindRotation[i], parentGlobal)
                    : Quaternion.Concatenate(Quaternion.Concatenate(_bindGlobalRotation[i],
                        Quaternion.Inverse(_sourceBindGlobal[j])), _sourceGlobal[j]);
                Quaternion local = Quaternion.Normalize(Quaternion.Concatenate(_globalRotation[i], Quaternion.Inverse(parentGlobal)));

                Vector3 translation = _bindTranslation[i];
                if (i == _root && _sourceRoot >= 0)
                    translation = InPlace ? KeepVertical(rootTranslation, rootStart) : rootTranslation;
                _local[i] = Compose(_bindScale[i], local, translation);
            }
        }

        // Keeps the start's horizontal placement and only the vertical motion.
        private Vector3 KeepVertical(Vector3 translation, Vector3 start) =>
            start + _up * Vector3.Dot(translation - start, _up);

        private static Matrix Compose(Vector3 scale, Quaternion rotation, Vector3 translation) =>
            Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);

        private static Quaternion RotationOf(Matrix matrix)
        {
            matrix.Decompose(out _, out Quaternion rotation, out _);
            return rotation;
        }

        // Model-space rotations from local ones (parents precede children in bone order).
        private static void Globals(SkinningData skeleton, Quaternion[] local, Quaternion rootRotation, Quaternion[] global)
        {
            for (int i = 0; i < local.Length; i++)
            {
                int parent = skeleton.ParentIndices[i];
                global[i] = Quaternion.Concatenate(local[i], parent >= 0 ? global[parent] : rootRotation);
            }
        }
    }
}
