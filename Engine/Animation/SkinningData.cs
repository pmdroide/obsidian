using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Animation
{
    /// <summary>
    /// Skeleton and animation clips of a skinned model, stored in <see cref="Model.Tag"/> by the
    /// content pipeline's SkinnedModelProcessor (ContentPipeline/). Bone indices match the
    /// BlendIndices0 vertex channel. Shared by every entity using the model, so never mutated.
    /// </summary>
    public sealed class SkinningData
    {
        public string[] BoneNames;
        public int[] ParentIndices;
        /// <summary>Local (parent-relative) bind transforms.</summary>
        public Matrix[] BindPose;
        /// <summary>Inverse of each bone's model-space bind transform.</summary>
        public Matrix[] InverseBindPose;
        /// <summary>Model-space transform above the root bone(s).</summary>
        public Matrix RootTransform = Matrix.Identity;
        public AnimationClip[] Clips = Array.Empty<AnimationClip>();

        private Dictionary<string, int> _boneIndex;

        public int BoneCount => BoneNames.Length;

        /// <summary>Skinning data of a loaded model, or null for a static one.</summary>
        public static SkinningData From(Model model) => model?.Tag as SkinningData;

        public int FindBone(string name)
        {
            if (name == null) return -1;
            if (_boneIndex == null)
            {
                var map = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < BoneNames.Length; i++) map[BoneNames[i]] = i;
                _boneIndex = map;
            }
            return _boneIndex.TryGetValue(name, out int index) ? index : -1;
        }

        /// <summary>The named clip, or (for an empty name) the longest one; null when not found.</summary>
        public AnimationClip FindClip(string name)
        {
            AnimationClip best = null;
            foreach (var clip in Clips)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    if (string.Equals(clip.Name, name, StringComparison.OrdinalIgnoreCase)) return clip;
                }
                else if (best == null || clip.Duration > best.Duration) best = clip;
            }
            return best;
        }
    }

    public sealed class AnimationClip
    {
        public string Name;
        /// <summary>Length in seconds.</summary>
        public float Duration;
        public BoneTrack[] Tracks = Array.Empty<BoneTrack>();
    }

    /// <summary>Local keyframes of one bone, decomposed for interpolation.</summary>
    public sealed class BoneTrack
    {
        public int BoneIndex;
        public float[] Times;
        public Vector3[] Translations;
        public Quaternion[] Rotations;
        public Vector3[] Scales;

        /// <summary>Linear/spherical interpolation between the keys around <paramref name="time"/>.</summary>
        public void Sample(float time, out Vector3 translation, out Quaternion rotation, out Vector3 scale)
        {
            int last = Times.Length - 1;
            if (last <= 0 || time <= Times[0])
            {
                translation = Translations[0]; rotation = Rotations[0]; scale = Scales[0];
                return;
            }
            if (time >= Times[last])
            {
                translation = Translations[last]; rotation = Rotations[last]; scale = Scales[last];
                return;
            }

            // Last key at or before time.
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (Times[mid] <= time) lo = mid; else hi = mid;
            }
            float span = Times[hi] - Times[lo];
            float t = span > 0 ? (time - Times[lo]) / span : 0;
            translation = Vector3.Lerp(Translations[lo], Translations[hi], t);
            rotation = Quaternion.Slerp(Rotations[lo], Rotations[hi], t);
            scale = Vector3.Lerp(Scales[lo], Scales[hi], t);
        }
    }

    /// <summary>Field order must match Obsidian.ContentPipeline.SkinningDataWriter.</summary>
    public sealed class SkinningDataReader : ContentTypeReader<SkinningData>
    {
        private const int FormatVersion = 1;

        protected override SkinningData Read(ContentReader input, SkinningData existingInstance)
        {
            int version = input.ReadInt32();
            if (version != FormatVersion)
                throw new ContentLoadException($"Skinning data version {version} is not supported (expected {FormatVersion}); rebuild the content.");

            int boneCount = input.ReadInt32();
            var data = new SkinningData
            {
                BoneNames = new string[boneCount],
                ParentIndices = new int[boneCount],
                BindPose = new Matrix[boneCount],
                InverseBindPose = new Matrix[boneCount],
            };
            for (int i = 0; i < boneCount; i++)
            {
                data.BoneNames[i] = input.ReadString();
                data.ParentIndices[i] = input.ReadInt32();
                data.BindPose[i] = input.ReadMatrix();
                data.InverseBindPose[i] = input.ReadMatrix();
            }
            data.RootTransform = input.ReadMatrix();

            data.Clips = new AnimationClip[input.ReadInt32()];
            for (int c = 0; c < data.Clips.Length; c++)
            {
                var clip = new AnimationClip { Name = input.ReadString(), Duration = input.ReadSingle() };
                clip.Tracks = new BoneTrack[input.ReadInt32()];
                for (int t = 0; t < clip.Tracks.Length; t++)
                {
                    int boneIndex = input.ReadInt32();
                    int keyCount = input.ReadInt32();
                    var track = new BoneTrack
                    {
                        BoneIndex = boneIndex,
                        Times = new float[keyCount],
                        Translations = new Vector3[keyCount],
                        Rotations = new Quaternion[keyCount],
                        Scales = new Vector3[keyCount],
                    };
                    for (int k = 0; k < keyCount; k++)
                    {
                        track.Times[k] = input.ReadSingle();
                        track.Translations[k] = input.ReadVector3();
                        track.Rotations[k] = input.ReadQuaternion();
                        track.Scales[k] = input.ReadVector3();
                    }
                    clip.Tracks[t] = track;
                }
                data.Clips[c] = clip;
            }
            return data;
        }
    }
}
