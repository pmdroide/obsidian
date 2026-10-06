using Assimp;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaQuaternion = Microsoft.Xna.Framework.Quaternion;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Obsidian.ContentPipeline
{
    /// <summary>
    /// Re-reads an FBX file's animations with Assimp, pivots merged into the bones.
    ///
    /// MonoGame 3.8.4's FbxImporter keeps FBX pivots and composes each bone's PreRotation onto its
    /// animation keys, but Assimp's keys already contain it, so Mixamo clips come out with the
    /// pre-rotation applied twice (legs flipped ~180°). With pivots merged, Assimp's keys are plain
    /// local bone transforms in the same space as the imported bind pose.
    /// </summary>
    internal static class FbxAnimationReader
    {
        public static Dictionary<string, AnimationContent> Read(string path, ICollection<string> boneNames,
            ContentProcessorContext context, ContentIdentity identity)
        {
            var result = new Dictionary<string, AnimationContent>();
            var bones = new HashSet<string>(boneNames, StringComparer.Ordinal);

            using var importer = new AssimpContext();
            importer.SetConfig(new Assimp.Configs.FBXPreservePivotsConfig(false));
            Scene scene = importer.ImportFile(path, PostProcessSteps.None);
            if (scene == null || !scene.HasAnimations) return result;

            foreach (Animation animation in scene.Animations)
            {
                double ticksPerSecond = animation.TicksPerSecond > 0 ? animation.TicksPerSecond : 25.0;
                var content = new AnimationContent
                {
                    Name = UniqueName(result, string.IsNullOrEmpty(animation.Name) ? "Take" : animation.Name),
                    Identity = identity,
                    Duration = TimeSpan.FromSeconds(animation.DurationInTicks / ticksPerSecond),
                };

                foreach (NodeAnimationChannel channel in animation.NodeAnimationChannels)
                {
                    if (!bones.Contains(channel.NodeName))
                    {
                        context.Logger.LogWarning(null, identity, "Clip '{0}' animates '{1}', which is not a skeleton bone; ignored.",
                            content.Name, channel.NodeName);
                        continue;
                    }

                    var keyframes = new AnimationChannel();
                    foreach (double tick in KeyTimes(channel))
                    {
                        XnaVector3 scale = channel.HasScalingKeys ? Sample(channel.ScalingKeys, tick) : XnaVector3.One;
                        XnaQuaternion rotation = channel.HasRotationKeys ? Sample(channel.RotationKeys, tick) : XnaQuaternion.Identity;
                        XnaVector3 translation = channel.HasPositionKeys ? Sample(channel.PositionKeys, tick) : XnaVector3.Zero;
                        XnaMatrix transform = XnaMatrix.CreateScale(scale) * XnaMatrix.CreateFromQuaternion(rotation) *
                                              XnaMatrix.CreateTranslation(translation);
                        keyframes.Add(new AnimationKeyframe(TimeSpan.FromSeconds(tick / ticksPerSecond), transform));
                    }
                    if (keyframes.Count > 0) content.Channels.Add(channel.NodeName, keyframes);
                }

                if (content.Channels.Count > 0) result.Add(content.Name, content);
            }
            return result;
        }

        private static string UniqueName(Dictionary<string, AnimationContent> taken, string name)
        {
            string unique = name;
            for (int n = 2; taken.ContainsKey(unique); n++) unique = name + "_" + n;
            return unique;
        }

        private static SortedSet<double> KeyTimes(NodeAnimationChannel channel)
        {
            var times = new SortedSet<double>();
            foreach (VectorKey key in channel.PositionKeys) times.Add(key.Time);
            foreach (QuaternionKey key in channel.RotationKeys) times.Add(key.Time);
            foreach (VectorKey key in channel.ScalingKeys) times.Add(key.Time);
            return times;
        }

        private static XnaVector3 Sample(List<VectorKey> keys, double time)
        {
            int i = Upper(keys.Count, k => keys[k].Time, time);
            if (i == 0) return ToXna(keys[0].Value);
            if (i == keys.Count) return ToXna(keys[^1].Value);
            VectorKey a = keys[i - 1], b = keys[i];
            float t = (float)((time - a.Time) / (b.Time - a.Time));
            return XnaVector3.Lerp(ToXna(a.Value), ToXna(b.Value), t);
        }

        private static XnaQuaternion Sample(List<QuaternionKey> keys, double time)
        {
            int i = Upper(keys.Count, k => keys[k].Time, time);
            if (i == 0) return ToXna(keys[0].Value);
            if (i == keys.Count) return ToXna(keys[^1].Value);
            QuaternionKey a = keys[i - 1], b = keys[i];
            float t = (float)((time - a.Time) / (b.Time - a.Time));
            return XnaQuaternion.Slerp(ToXna(a.Value), ToXna(b.Value), t);
        }

        // Index of the first key strictly after time (keys are sorted).
        private static int Upper(int count, Func<int, double> timeOf, double time)
        {
            int lo = 0, hi = count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (timeOf(mid) <= time) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private static XnaVector3 ToXna(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
        private static XnaQuaternion ToXna(System.Numerics.Quaternion q) => XnaQuaternion.Normalize(new XnaQuaternion(q.X, q.Y, q.Z, q.W));
    }
}
