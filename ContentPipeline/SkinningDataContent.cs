using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler;

namespace Obsidian.ContentPipeline
{
    /// <summary>Build-time mirror of <c>Engine.Animation.SkinningData</c>.</summary>
    public sealed class SkinningDataContent
    {
        public List<string> BoneNames { get; } = new();
        public List<int> ParentIndices { get; } = new();
        /// <summary>Local (parent-relative) bind transforms.</summary>
        public List<Matrix> BindPose { get; } = new();
        /// <summary>Inverse of each bone's model-space bind transform.</summary>
        public List<Matrix> InverseBindPose { get; } = new();
        /// <summary>Model-space transform above the root bone.</summary>
        public Matrix RootTransform { get; set; } = Matrix.Identity;
        public List<ClipContent> Clips { get; } = new();
    }

    public sealed class ClipContent
    {
        public string Name { get; set; }
        public float Duration { get; set; }
        public List<TrackContent> Tracks { get; } = new();
    }

    /// <summary>Decomposed local keyframes of one bone.</summary>
    public sealed class TrackContent
    {
        public int BoneIndex { get; set; }
        public List<float> Times { get; } = new();
        public List<Vector3> Translations { get; } = new();
        public List<Quaternion> Rotations { get; } = new();
        public List<Vector3> Scales { get; } = new();
    }

    /// <summary>Field order must match <c>Engine.Animation.SkinningDataReader</c>.</summary>
    [ContentTypeWriter]
    public sealed class SkinningDataWriter : ContentTypeWriter<SkinningDataContent>
    {
        public const int FormatVersion = 1;

        protected override void Write(ContentWriter output, SkinningDataContent value)
        {
            output.Write(FormatVersion);
            output.Write(value.BoneNames.Count);
            for (int i = 0; i < value.BoneNames.Count; i++)
            {
                output.Write(value.BoneNames[i]);
                output.Write(value.ParentIndices[i]);
                output.Write(value.BindPose[i]);
                output.Write(value.InverseBindPose[i]);
            }
            output.Write(value.RootTransform);

            output.Write(value.Clips.Count);
            foreach (ClipContent clip in value.Clips)
            {
                output.Write(clip.Name ?? string.Empty);
                output.Write(clip.Duration);
                output.Write(clip.Tracks.Count);
                foreach (TrackContent track in clip.Tracks)
                {
                    output.Write(track.BoneIndex);
                    output.Write(track.Times.Count);
                    for (int k = 0; k < track.Times.Count; k++)
                    {
                        output.Write(track.Times[k]);
                        output.Write(track.Translations[k]);
                        output.Write(track.Rotations[k]);
                        output.Write(track.Scales[k]);
                    }
                }
            }
        }

        public override string GetRuntimeReader(TargetPlatform targetPlatform) =>
            "Engine.Animation.SkinningDataReader, Engine";

        public override string GetRuntimeType(TargetPlatform targetPlatform) =>
            "Engine.Animation.SkinningData, Engine";
    }
}
