using System;
using System.Collections.Generic;
using Engine.Components;
using Engine.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Animation
{
    /// <summary>
    /// The posed bones of a skinned gameobject (Anvil Hierarchy > gameobject > Armature > bone, rotated with the
    /// gizmo or the Inspector). Each posed bone keeps one rotation applied in its own space on top of its rest pose
    /// (or the Animator's pose during Play); the Armature's rotates the whole skeleton about the model origin.
    /// Saved with the scene by bone name. When nothing else skins the mesh (Edit mode, Play without an Animator or
    /// limp Ragdoll) <see cref="UpdateAll"/> skins it with this pose. Game thread only.
    /// </summary>
    public sealed class BonePose : IDisposable
    {
        /// <summary>Rotation key of the Armature (the whole skeleton).</summary>
        public const string ArmatureKey = "";

        // Bone name (or ArmatureKey) -> rotation; only bones that differ from the rest pose.
        private readonly Dictionary<string, Quaternion> _rotations = new(StringComparer.Ordinal);

        // Rotations by bone index for the skeleton they were last mapped to.
        private SkinningData _mapped;
        private int _mappedVersion = -1;
        private Quaternion[] _bone = Array.Empty<Quaternion>();
        private bool[] _posed = Array.Empty<bool>();
        private Quaternion _armature = Quaternion.Identity;
        private bool _armaturePosed;

        // Own evaluation (rest pose + rotations) and the skin it uploads when nothing else drives the mesh.
        private AnimationPlayer _player;
        private int _evaluatedVersion = -1;
        private SkinnedMeshInstance _skin;
        private int _skinnedVersion = -1;
        private BoneObject[] _proxies = Array.Empty<BoneObject>();

        /// <summary>Bumped on every change, so skins know to re-pose.</summary>
        public int Version { get; private set; }

        public bool IsEmpty => _rotations.Count == 0;

        /// <summary>Number of posed bones (the Armature counts as one).</summary>
        public int Count => _rotations.Count;

        /// <summary>The rotation of a bone (<see cref="BoneObject.ArmatureIndex"/> = the Armature); identity when unposed.</summary>
        public Quaternion Get(SkinningData skeleton, int bone) =>
            _rotations.TryGetValue(KeyOf(skeleton, bone), out Quaternion rotation) ? rotation : Quaternion.Identity;

        public void Set(SkinningData skeleton, int bone, Quaternion rotation)
        {
            string key = KeyOf(skeleton, bone);
            if (key == null) return;
            rotation = Quaternion.Normalize(rotation);
            // Within ~0.01° (sin of half the angle below 1e-4): back to the rest pose.
            if (new Vector3(rotation.X, rotation.Y, rotation.Z).LengthSquared() < 1e-8f) _rotations.Remove(key);
            else _rotations[key] = rotation;
            Version++;
        }

        public void Reset(SkinningData skeleton, int bone)
        {
            string key = KeyOf(skeleton, bone);
            if (key != null && _rotations.Remove(key)) Version++;
        }

        /// <summary>Back to the rest pose.</summary>
        public void Clear()
        {
            if (_rotations.Count == 0) return;
            _rotations.Clear();
            Version++;
        }

        public void CopyFrom(BonePose other)
        {
            _rotations.Clear();
            if (other != null) foreach (var kv in other._rotations) _rotations[kv.Key] = kv.Value;
            Version++;
        }

        /// <summary>The saved form (bone name -> rotation), or null when nothing is posed.</summary>
        public Dictionary<string, Quaternion> ToRecord() =>
            _rotations.Count == 0 ? null : new Dictionary<string, Quaternion>(_rotations, StringComparer.Ordinal);

        public void Load(IReadOnlyDictionary<string, Quaternion> record)
        {
            _rotations.Clear();
            if (record != null)
                foreach (var kv in record)
                    if (kv.Key != null) _rotations[kv.Key] = Quaternion.Normalize(kv.Value);
            Version++;
        }

        private static string KeyOf(SkinningData skeleton, int bone)
        {
            if (bone == BoneObject.ArmatureIndex) return ArmatureKey;
            return skeleton != null && bone >= 0 && bone < skeleton.BoneCount ? skeleton.BoneNames[bone] : null;
        }

        /// <summary>
        /// Applies the rotations to a local (parent-relative) pose of <paramref name="skeleton"/> and to the
        /// model-space transform above its root bones. Called by <see cref="AnimationPlayer.Evaluate"/>.
        /// </summary>
        internal void ApplyTo(SkinningData skeleton, Matrix[] local, ref Matrix root)
        {
            if (_rotations.Count == 0) return;
            Map(skeleton);
            for (int i = 0; i < local.Length && i < _bone.Length; i++)
                if (_posed[i]) local[i] = Matrix.CreateFromQuaternion(_bone[i]) * local[i];
            if (_armaturePosed) root *= Matrix.CreateFromQuaternion(_armature);
        }

        private void Map(SkinningData skeleton)
        {
            if (ReferenceEquals(_mapped, skeleton) && _mappedVersion == Version) return;
            _mapped = skeleton;
            _mappedVersion = Version;
            int count = skeleton.BoneCount;
            if (_bone.Length != count)
            {
                _bone = new Quaternion[count];
                _posed = new bool[count];
            }
            for (int i = 0; i < count; i++)
                _posed[i] = _rotations.TryGetValue(skeleton.BoneNames[i], out _bone[i]);
            _armaturePosed = _rotations.TryGetValue(ArmatureKey, out _armature);
        }

        /// <summary>
        /// The skeleton's model-space bone transforms as drawn now: the Animator's while it plays (it applies this
        /// pose too), otherwise the rest pose with this pose's rotations. Null for a model without a skeleton.
        /// </summary>
        public Matrix[] ModelBones(BasicEntity owner)
        {
            SkinningData skeleton = SkinningData.From(owner?.Model);
            if (skeleton == null) return null;
            if (owner.GetComponent<AnimatorComponent>() is { Enabled: true, IsPlaying: true, Player: { } player } &&
                ReferenceEquals(player.Skeleton, skeleton))
                return player.BoneTransforms;
            return Evaluate(skeleton).BoneTransforms;
        }

        private AnimationPlayer Evaluate(SkinningData skeleton)
        {
            if (_player == null || !ReferenceEquals(_player.Skeleton, skeleton))
            {
                _player = new AnimationPlayer(skeleton) { Overrides = this };
                _evaluatedVersion = -1;
            }
            if (_evaluatedVersion != Version)
            {
                _player.Evaluate(0);
                _evaluatedVersion = Version;
            }
            return _player;
        }

        /// <summary>The selectable proxy of a bone (or the Armature); null when the index is out of range.</summary>
        public BoneObject Proxy(BasicEntity owner, int bone)
        {
            SkinningData skeleton = SkinningData.From(owner?.Model);
            if (skeleton == null || bone < BoneObject.ArmatureIndex || bone >= skeleton.BoneCount) return null;
            if (_proxies.Length != skeleton.BoneCount + 1) _proxies = new BoneObject[skeleton.BoneCount + 1];
            BoneObject proxy = _proxies[bone + 1];
            if (proxy == null || proxy.Owner != owner) _proxies[bone + 1] = proxy = new BoneObject(owner, bone);
            return proxy;
        }

        /// <summary>
        /// Skins every posed gameobject that nothing else skins (an Animator or limp Ragdoll uploads its own pose),
        /// and hands an unposed one back to its shared bind-pose buffers. Edit and Play, every frame.
        /// </summary>
        public static void UpdateAll(List<BasicEntity> entities)
        {
            if (entities == null) return;
            for (int i = 0; i < entities.Count; i++) entities[i].BonePose.Update(entities[i]);
        }

        private void Update(BasicEntity owner)
        {
            TransformMatrix transform = owner.WorldTransform;
            bool ownSkin = _skin != null && ReferenceEquals(transform.Skin, _skin);
            // Another skin (Animator, Ragdoll) is drawn: it owns the mesh until it lets go.
            if (transform.Skin != null && !ownSkin)
            {
                _skinnedVersion = -1;
                return;
            }
            SkinningData skeleton = IsEmpty ? null : SkinningData.From(owner.Model);
            if (skeleton == null)
            {
                if (ownSkin)
                {
                    transform.Skin = null;
                    transform.HasChanged = true;
                }
                _skinnedVersion = -1;
                return;
            }
            if (ownSkin && _skinnedVersion == Version) return;

            if (_skin == null)
            {
                GraphicsDevice device = FindDevice(owner.Model);
                if (device == null) return;
                _skin = new SkinnedMeshInstance(device, owner.Model);
            }
            if (!_skin.HasSkinnedBuffers) return;
            _skin.Update(Evaluate(skeleton).SkinTransforms);
            _skinnedVersion = Version;
            transform.Skin = _skin;
            // Skinned vertices moved: redo culling and redraw shadow maps this frame.
            transform.HasChanged = true;
        }

        private static GraphicsDevice FindDevice(Model model)
        {
            foreach (ModelMesh mesh in model.Meshes)
                foreach (ModelMeshPart part in mesh.MeshParts)
                    if (part.VertexBuffer != null) return part.VertexBuffer.GraphicsDevice;
            return null;
        }

        /// <summary>Lines from each joint to its parent's, in world space (the editor's armature overlay).</summary>
        public void CollectLines(BasicEntity owner, List<(Vector3 From, Vector3 To, int Bone)> lines)
        {
            Matrix[] bones = ModelBones(owner);
            if (bones == null) return;
            SkinningData skeleton = SkinningData.From(owner.Model);
            Matrix world = BoneObject.WorldOf(owner);
            for (int i = 0; i < bones.Length; i++)
            {
                int parent = skeleton.ParentIndices[i];
                if (parent < 0) continue;
                lines.Add((Vector3.Transform(bones[parent].Translation, world), Vector3.Transform(bones[i].Translation, world), i));
            }
        }

        /// <summary>Drops the own skin (unregistered gameobject); the next <see cref="UpdateAll"/> makes a new one.</summary>
        public void Release(BasicEntity owner)
        {
            if (_skin != null && owner != null && ReferenceEquals(owner.WorldTransform.Skin, _skin))
            {
                owner.WorldTransform.Skin = null;
                owner.WorldTransform.HasChanged = true;
            }
            Dispose();
        }

        public void Dispose()
        {
            _skin?.Dispose();
            _skin = null;
            _skinnedVersion = -1;
        }
    }
}
