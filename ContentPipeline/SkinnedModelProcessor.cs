using System.ComponentModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using Microsoft.Xna.Framework.Content.Pipeline.Processors;

namespace Obsidian.ContentPipeline
{
    /// <summary>
    /// ModelProcessor that keeps what the stock processor throws away for skinned FBX files:
    /// the skeleton (bind pose, inverse bind pose, hierarchy) and every animation clip. The data
    /// is stored in <c>Model.Tag</c> and read at runtime as <c>Engine.Animation.SkinningData</c>.
    ///
    /// Vertices keep the stock BlendIndices0/BlendWeight0 channels, indexed in
    /// <see cref="MeshHelper.FlattenSkeleton"/> order — the same order used here. A file without
    /// a skeleton builds exactly like ModelProcessor (Tag stays null).
    /// </summary>
    [ContentProcessor(DisplayName = "Skinned Model - Obsidian")]
    public class SkinnedModelProcessor : ModelProcessor
    {
        /// <summary>Replace the FbxImporter's clips with ones read by <see cref="FbxAnimationReader"/>.</summary>
        [DisplayName("Read FBX Animations With Assimp")]
        [DefaultValue(true)]
        public bool ReadFbxAnimationsWithAssimp { get; set; } = true;

        public override ModelContent Process(NodeContent input, ContentProcessorContext context)
        {
            BoneContent skeleton = MeshHelper.FindSkeleton(input);
            if (skeleton == null)
            {
                context.Logger.LogWarning(null, input.Identity, "No skeleton found; building as a static model.");
                return base.Process(input, context);
            }

            // Skinning treats every mesh as authored in model space; bake any mesh-node transform
            // (and those of its non-bone parents) into the vertices.
            FlattenMeshTransforms(input, skeleton);

            string source = input.Identity?.SourceFilename;
            if (ReadFbxAnimationsWithAssimp && string.Equals(Path.GetExtension(source), ".fbx", StringComparison.OrdinalIgnoreCase))
                ReplaceAnimations(input, skeleton, source, context);

            // Applies the Scale/Rotation parameters to meshes, bones and animation keys alike.
            ModelContent model = base.Process(input, context);

            IList<BoneContent> bones = MeshHelper.FlattenSkeleton(skeleton);
            var indexByName = new Dictionary<string, int>();
            var data = new SkinningDataContent();
            for (int i = 0; i < bones.Count; i++)
            {
                BoneContent bone = bones[i];
                indexByName[bone.Name] = i;
                data.BoneNames.Add(bone.Name);
                data.ParentIndices.Add(bone.Parent is BoneContent parent && indexByName.TryGetValue(parent.Name, out int p) ? p : -1);
                data.BindPose.Add(bone.Transform);
                data.InverseBindPose.Add(Matrix.Invert(bone.AbsoluteTransform));
            }
            // A bone whose parent isn't a bone still inherits every transform above it.
            data.RootTransform = skeleton.Parent?.AbsoluteTransform ?? Matrix.Identity;

            foreach (KeyValuePair<string, AnimationContent> animation in CollectAnimations(input))
                data.Clips.Add(BuildClip(animation.Key, animation.Value, indexByName, context, input.Identity));

            context.Logger.LogMessage("Skinned model: {0} bones, {1} clip(s).", bones.Count, data.Clips.Count);
            model.Tag = data;
            return model;
        }

        // Clips go on the skeleton root so base.Process transforms them with the bones.
        private static void ReplaceAnimations(NodeContent input, BoneContent skeleton, string source, ContentProcessorContext context)
        {
            Dictionary<string, AnimationContent> clips;
            try
            {
                var boneNames = MeshHelper.FlattenSkeleton(skeleton).Select(b => b.Name).ToList();
                clips = FbxAnimationReader.Read(source, boneNames, context, input.Identity);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(null, input.Identity, "Reading animations with Assimp failed ({0}); keeping the importer's clips.", ex.Message);
                return;
            }

            ClearAnimations(input);
            foreach (KeyValuePair<string, AnimationContent> clip in clips) skeleton.Animations.Add(clip.Key, clip.Value);
        }

        private static void ClearAnimations(NodeContent node)
        {
            node.Animations.Clear();
            foreach (NodeContent child in node.Children) ClearAnimations(child);
        }

        private static void FlattenMeshTransforms(NodeContent node, BoneContent skeleton)
        {
            if (node == skeleton) return;
            if (node is MeshContent mesh && node.Transform != Matrix.Identity)
            {
                Matrix absolute = mesh.AbsoluteTransform;
                MeshHelper.TransformScene(mesh, absolute);
                mesh.Transform = Matrix.Identity;
            }
            foreach (NodeContent child in node.Children) FlattenMeshTransforms(child, skeleton);
        }

        // Importers attach clips to the skeleton root, but some put them on other nodes.
        private static IEnumerable<KeyValuePair<string, AnimationContent>> CollectAnimations(NodeContent node)
        {
            foreach (KeyValuePair<string, AnimationContent> animation in node.Animations) yield return animation;
            foreach (NodeContent child in node.Children)
                foreach (KeyValuePair<string, AnimationContent> animation in CollectAnimations(child))
                    yield return animation;
        }

        private static ClipContent BuildClip(string name, AnimationContent animation, Dictionary<string, int> indexByName,
            ContentProcessorContext context, ContentIdentity identity)
        {
            var clip = new ClipContent { Name = name, Duration = (float)animation.Duration.TotalSeconds };
            foreach (KeyValuePair<string, AnimationChannel> channel in animation.Channels)
            {
                if (!indexByName.TryGetValue(channel.Key, out int boneIndex))
                {
                    context.Logger.LogWarning(null, identity, "Clip '{0}' animates '{1}', which is not a skeleton bone; ignored.", name, channel.Key);
                    continue;
                }

                var track = new TrackContent { BoneIndex = boneIndex };
                foreach (AnimationKeyframe key in channel.Value)
                {
                    key.Transform.Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 translation);
                    track.Times.Add((float)key.Time.TotalSeconds);
                    track.Translations.Add(translation);
                    track.Rotations.Add(Quaternion.Normalize(rotation));
                    track.Scales.Add(scale);
                }
                if (track.Times.Count > 0) clip.Tracks.Add(track);
            }
            return clip;
        }
    }
}
