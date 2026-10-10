using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Engine.Animation;
using Engine.Recources.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NumericsVector3 = System.Numerics.Vector3;

namespace Engine.Physics
{
    /// <summary>How a ragdoll joint may bend; picked from the child bone's name (see <see cref="RagdollRig.JointFor"/>).</summary>
    public enum RagdollJointKind
    {
        Other,
        Spine,
        Neck,
        Clavicle,
        UpperArm,
        Elbow,
        Thigh,
        Knee,
        Hand,
        Foot,
    }

    /// <summary>
    /// How a skinned model splits into ragdoll parts, worked out once per model from its bind pose.
    /// Every vertex belongs to its most heavily weighted bone. A bone whose vertices cover enough of
    /// the model becomes a part (a rigid body); the vertices of smaller bones (fingers, toes, a short
    /// neck) join the nearest part above them. The topmost part is the root (usually the hips).
    /// Each part's collider is the convex hull of its vertices in its bone's space.
    /// </summary>
    public sealed class RagdollRig
    {
        // A bone becomes a part when it owns this many vertices (and at least MinVertexShare of them)...
        private const int MinVertices = 16;
        private const float MinVertexShare = 0.004f;
        // ...and its vertices span this fraction of the model's size (so fingers, toes and a short neck don't).
        private const float MinExtentShare = 0.07f;
        // BEPU's solver struggles when a joint links bodies of very different mass.
        private const float MinMassShareOfHeaviest = 1f / 10f;

        public sealed class Part
        {
            /// <summary>The skeleton bone this part follows.</summary>
            public int Bone;
            public string Name;
            /// <summary>Parent part (joined at this part's bone origin), or -1 for the root part.</summary>
            public int Parent = -1;
            public RagdollJointKind Joint;
            /// <summary>Hull points in the bone's bind space (vertex * InverseBindPose[Bone]).</summary>
            public Vector3[] Points = Array.Empty<Vector3>();
            /// <summary>Fraction of the ragdoll's total mass (all parts sum to 1).</summary>
            public float MassShare;
            /// <summary>Model-space bind direction from the joint along the part (unit length).</summary>
            public Vector3 RestDirection;
        }

        public SkinningData Skeleton { get; private set; }
        public Part[] Parts { get; private set; } = Array.Empty<Part>();
        /// <summary>The part each bone moves with: its own, else the nearest part above it; -1 above the root part.</summary>
        public int[] PartOfBone { get; private set; } = Array.Empty<int>();
        /// <summary>Model-space bind transform of every bone (BindPose chained under RootTransform).</summary>
        public Matrix[] BindModel { get; private set; } = Array.Empty<Matrix>();
        /// <summary>The character's model-space up, left and forward in its bind pose.</summary>
        public Vector3 Up { get; private set; } = Vector3.UnitZ;
        public Vector3 Left { get; private set; } = Vector3.UnitX;
        public Vector3 Forward { get; private set; } = -Vector3.UnitY;

        private static readonly ConditionalWeakTable<Model, RagdollRig> Cache = new();

        /// <summary>The rig of a skinned model (cached), or null for a model without skeleton or skinned vertices.</summary>
        public static RagdollRig For(Model model)
        {
            SkinningData skeleton = SkinningData.From(model);
            if (skeleton == null) return null;
            lock (Cache)
            {
                if (Cache.TryGetValue(model, out RagdollRig cached)) return cached;
                var positions = new List<Vector3>();
                var bones = new List<int>();
                SkinnedMeshInstance.CollectDominantBones(model, positions, bones);
                RagdollRig rig = Build(skeleton, positions, bones);
                if (rig != null) Cache.Add(model, rig);
                return rig;
            }
        }

        /// <summary>
        /// Builds a rig from bind-pose vertices and the bone each mostly follows (tests build skeletons by hand).
        /// Null when no bone qualifies as a part.
        /// </summary>
        public static RagdollRig Build(SkinningData skeleton, IReadOnlyList<Vector3> positions, IReadOnlyList<int> bones)
        {
            int boneCount = skeleton.BoneCount;
            var rig = new RagdollRig { Skeleton = skeleton, BindModel = new Matrix[boneCount] };
            for (int i = 0; i < boneCount; i++)
            {
                int parent = skeleton.ParentIndices[i];
                rig.BindModel[i] = skeleton.BindPose[i] * (parent >= 0 ? rig.BindModel[parent] : skeleton.RootTransform);
            }

            // Vertices per bone, and how far they spread in the bone's own space.
            var owned = new List<int>[boneCount];
            var min = new Vector3[boneCount];
            var max = new Vector3[boneCount];
            Vector3 modelMin = new Vector3(float.MaxValue), modelMax = new Vector3(float.MinValue);
            int vertexCount = 0;
            for (int v = 0; v < positions.Count; v++)
            {
                int bone = bones[v];
                if (bone < 0 || bone >= boneCount) continue;
                Vector3 local = Vector3.Transform(positions[v], skeleton.InverseBindPose[bone]);
                if (owned[bone] == null)
                {
                    owned[bone] = new List<int>();
                    min[bone] = max[bone] = local;
                }
                owned[bone].Add(v);
                min[bone] = Vector3.Min(min[bone], local);
                max[bone] = Vector3.Max(max[bone], local);
                modelMin = Vector3.Min(modelMin, positions[v]);
                modelMax = Vector3.Max(modelMax, positions[v]);
                vertexCount++;
            }
            if (vertexCount == 0) return null;
            Vector3 modelSize = modelMax - modelMin;
            float size = Math.Max(modelSize.X, Math.Max(modelSize.Y, modelSize.Z));

            int minVertices = Math.Max(MinVertices, (int)(vertexCount * MinVertexShare));
            var isPart = new bool[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                if (owned[i] == null || owned[i].Count < minVertices) continue;
                Vector3 extent = max[i] - min[i];
                isPart[i] = Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) >= size * MinExtentShare;
            }

            // Parents precede children, so each bone's part is known before its children's.
            var parts = new List<Part>();
            rig.PartOfBone = new int[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                int parent = skeleton.ParentIndices[i];
                int parentPart = parent >= 0 ? rig.PartOfBone[parent] : -1;
                if (!isPart[i])
                {
                    rig.PartOfBone[i] = parentPart;
                    continue;
                }
                rig.PartOfBone[i] = parts.Count;
                string name = skeleton.BoneNames[i] ?? "";
                // A second skeleton root hangs off the first part instead of floating free.
                parts.Add(new Part
                {
                    Bone = i,
                    Name = name,
                    Parent = parts.Count == 0 ? -1 : parentPart >= 0 ? parentPart : 0,
                    Joint = JointFor(name),
                });
            }
            if (parts.Count == 0) return null;
            rig.Parts = parts.ToArray();

            rig.FindCharacterFrame();

            // Each part takes the vertices of its own bone and of the small bones merged into it.
            var partVertices = new List<Vector3>[parts.Count];
            for (int p = 0; p < parts.Count; p++) partVertices[p] = new List<Vector3>();
            for (int i = 0; i < boneCount; i++)
            {
                int p = rig.PartOfBone[i];
                if (p < 0 || owned[i] == null) continue;
                Matrix toBone = skeleton.InverseBindPose[rig.Parts[p].Bone];
                foreach (int v in owned[i]) partVertices[p].Add(Vector3.Transform(positions[v], toBone));
            }

            var volumes = new float[parts.Count];
            var pool = new BufferPool();
            try
            {
                for (int p = 0; p < parts.Count; p++)
                    rig.Parts[p].Points = HullPoints(partVertices[p], pool, out volumes[p]);
            }
            finally { pool.Clear(); }

            rig.SetMassShares(volumes);
            rig.SetRestDirections(positions, bones);
            return rig;
        }

        /// <summary>
        /// The joint a bone's name suggests (Mixamo, Unreal, Unity and Blender rig names; any prefix such as
        /// "mixamorig:" is ignored). Elbows and knees become hinges, the rest cones of differing width.
        /// </summary>
        public static RagdollJointKind JointFor(string boneName)
        {
            string name = StripPrefix(boneName).ToLowerInvariant();
            bool Has(params string[] words)
            {
                foreach (string word in words)
                    if (name.Contains(word)) return true;
                return false;
            }

            if (Has("forearm", "lowerarm", "lower_arm", "elbow")) return RagdollJointKind.Elbow;
            if (Has("upleg", "upperleg", "upper_leg", "thigh")) return RagdollJointKind.Thigh;
            if (Has("shoulder", "clavicle", "collar")) return RagdollJointKind.Clavicle;
            if (Has("hand", "wrist", "finger", "thumb")) return RagdollJointKind.Hand;
            if (Has("foot", "ankle", "toe")) return RagdollJointKind.Foot;
            if (Has("calf", "shin", "knee", "leg")) return RagdollJointKind.Knee;
            if (Has("arm")) return RagdollJointKind.UpperArm;
            if (Has("head", "neck")) return RagdollJointKind.Neck;
            if (Has("spine", "chest", "hips", "pelvis", "torso", "abdomen")) return RagdollJointKind.Spine;
            return RagdollJointKind.Other;
        }

        /// <summary>
        /// Up is the engine's Z. Left points from each right bone to its mirrored left bone ("LeftArm"/"RightArm",
        /// "thigh_l"/"thigh_r", "hand.L"/"hand.R"), falling back to +X (Mixamo's left once imported Z-up); forward
        /// follows from both.
        /// </summary>
        private void FindCharacterFrame()
        {
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < BindModel.Length; i++) byName[StripPrefix(Skeleton.BoneNames[i]).ToLowerInvariant()] = i;

            Vector3 lateral = Vector3.Zero;
            foreach (var (name, left) in byName)
            {
                string right = name.StartsWith("left") ? "right" + name.Substring(4)
                    : name.StartsWith("l_") ? "r_" + name.Substring(2)
                    : name.EndsWith("_l") ? name.Substring(0, name.Length - 2) + "_r"
                    : name.EndsWith(".l") ? name.Substring(0, name.Length - 2) + ".r"
                    : null;
                if (right != null && byName.TryGetValue(right, out int mirror))
                    lateral += BindModel[left].Translation - BindModel[mirror].Translation;
            }

            Up = Vector3.UnitZ;
            lateral -= Up * Vector3.Dot(lateral, Up);
            Left = lateral.LengthSquared() > 1e-8f ? Vector3.Normalize(lateral) : Vector3.UnitX;
            Forward = Vector3.Cross(Left, Up);
        }

        // "mixamorig:LeftArm" -> "LeftArm".
        private static string StripPrefix(string boneName)
        {
            string name = boneName ?? "";
            int colon = name.LastIndexOf(':');
            return colon >= 0 ? name.Substring(colon + 1) : name;
        }

        /// <summary>Shares follow each part's hull volume, with light parts raised so joints stay stable.</summary>
        private void SetMassShares(float[] volumes)
        {
            float heaviest = 0;
            foreach (float volume in volumes) heaviest = Math.Max(heaviest, volume);
            float total = 0;
            for (int p = 0; p < Parts.Length; p++)
            {
                float volume = heaviest > 0 ? Math.Max(volumes[p], heaviest * MinMassShareOfHeaviest) : 1;
                Parts[p].MassShare = volume;
                total += volume;
            }
            for (int p = 0; p < Parts.Length; p++) Parts[p].MassShare /= total;
        }

        /// <summary>
        /// A part with child parts points at their joints (upper arm -> elbow); an end part (head, hand,
        /// foot) at the middle of its own vertices; failing both, along the line from its parent's joint.
        /// </summary>
        private void SetRestDirections(IReadOnlyList<Vector3> positions, IReadOnlyList<int> bones)
        {
            var childSum = new Vector3[Parts.Length];
            var childCount = new int[Parts.Length];
            for (int p = 0; p < Parts.Length; p++)
            {
                int parent = Parts[p].Parent;
                if (parent < 0) continue;
                childSum[parent] += BindModel[Parts[p].Bone].Translation;
                childCount[parent]++;
            }

            var centroid = new Vector3[Parts.Length];
            var count = new int[Parts.Length];
            for (int v = 0; v < positions.Count; v++)
            {
                int bone = bones[v];
                if (bone < 0 || bone >= PartOfBone.Length || PartOfBone[bone] < 0) continue;
                centroid[PartOfBone[bone]] += positions[v];
                count[PartOfBone[bone]]++;
            }

            for (int p = 0; p < Parts.Length; p++)
            {
                Vector3 joint = BindModel[Parts[p].Bone].Translation;
                Vector3 direction = childCount[p] > 0 ? childSum[p] / childCount[p] - joint : Vector3.Zero;
                if (direction.LengthSquared() < 1e-6f && count[p] > 0) direction = centroid[p] / count[p] - joint;
                if (direction.LengthSquared() < 1e-6f && Parts[p].Parent >= 0)
                    direction = joint - BindModel[Parts[Parts[p].Parent].Bone].Translation;
                Parts[p].RestDirection = direction.LengthSquared() > 1e-10f ? Vector3.Normalize(direction) : Up;
            }
        }

        /// <summary>The points on the convex hull of <paramref name="points"/>, and the hull's volume.</summary>
        private static Vector3[] HullPoints(List<Vector3> points, BufferPool pool, out float volume)
        {
            volume = 0;
            var unique = new List<Vector3>(new HashSet<Vector3>(points));
            if (unique.Count < 4) return unique.ToArray();

            var numerics = new NumericsVector3[unique.Count];
            for (int i = 0; i < numerics.Length; i++) numerics[i] = MathConverter.ToNumerics(unique[i]);
            HullData hull;
            try { ConvexHullHelper.ComputeHull(numerics, pool, out hull); }
            catch { return unique.ToArray(); }
            try
            {
                if (hull.OriginalVertexMapping.Length < 4) return unique.ToArray();
                var result = new Vector3[hull.OriginalVertexMapping.Length];
                for (int i = 0; i < result.Length; i++) result[i] = unique[hull.OriginalVertexMapping[i]];

                // Faces index the hull's own points; fan each face into tetrahedra around the hull's mean.
                Vector3 mean = Vector3.Zero;
                foreach (Vector3 point in result) mean += point;
                mean /= result.Length;
                for (int f = 0; f < hull.FaceStartIndices.Length; f++)
                {
                    int start = hull.FaceStartIndices[f];
                    int end = f + 1 < hull.FaceStartIndices.Length ? hull.FaceStartIndices[f + 1] : hull.FaceVertexIndices.Length;
                    Vector3 a = result[hull.FaceVertexIndices[start]] - mean;
                    for (int k = start + 1; k + 1 < end; k++)
                    {
                        Vector3 b = result[hull.FaceVertexIndices[k]] - mean;
                        Vector3 c = result[hull.FaceVertexIndices[k + 1]] - mean;
                        volume += Math.Abs(Vector3.Dot(a, Vector3.Cross(b, c))) / 6f;
                    }
                }
                return result;
            }
            finally { hull.Dispose(pool); }
        }
    }
}
