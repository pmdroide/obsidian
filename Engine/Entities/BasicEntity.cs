using System.Collections.Generic;
using Engine.Components;
using GameComponent = Engine.Components.GameComponent;
using BepuPhysics;
using Engine.Physics;
using Engine.Recources;
using Engine.Recources.Helper;
using Engine.Renderer.Helper;
using Engine.Scripting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using BoundingBox = Microsoft.Xna.Framework.BoundingBox;
using Matrix = Microsoft.Xna.Framework.Matrix;
using Vector3 = Microsoft.Xna.Framework.Vector3;

namespace Engine.Entities
{
    public sealed class BasicEntity : TransformableObject
    {
        //Avoid nesting, but i could also just provide the ModelDefinition instead
        public readonly ModelDefinition ModelDefinition;
        public readonly Model Model;
        public readonly BoundingBox BoundingBox;
        public readonly Vector3 BoundingBoxOffset;
        public readonly SignedDistanceField SignedDistanceField;
        public readonly MaterialEffect Material;
        private MeshMaterialLibrary _materialLibrary;
        private readonly List<MaterialEffect> _materialInstances = new();
        /// <summary>The material the mesh library draws the model with: the Material component's instance, else the gameobject's.</summary>
        internal MaterialEffect DrawMaterial => _materialInstances.Count > 0 ? _materialInstances[0] : Material ?? FirstPartMaterial();
        // LOD component, as registered: the models of levels 1..n and the camera distances they start at.
        private readonly List<Model> _lodModels = new();
        private readonly List<float> _lodDistances = new();
        private float _lodCullDistance;

        /// <summary>Models the library draws for LOD levels 1..n (level 0 is <see cref="Model"/>).</summary>
        public IReadOnlyList<Model> LodModels => _lodModels;

        private int _id;

        private Vector3 _position;

        // Read from the Physics component (Inspector > Add Component > Physics). ScenePhysics
        // creates/rebuilds the actual BEPU body to match these.
        public PhysicsComponent Physics => GetComponent<PhysicsComponent>();
        public PhysicsBodyType PhysicsType => Physics?.ActiveBodyType ?? PhysicsBodyType.None;
        public float Mass => Physics?.ClampedMass ?? 1f;

        // BEPUphysics v2 runtime state, owned by ScenePhysics. Handles are value-type
        // indices into the simulation; null when the entity has no body of that kind.
        public StaticHandle? StaticBody = null;
        public BodyHandle? DynamicBody = null;
        internal PhysicsBodyType AttachedPhysicsType;
        internal Vector3 AttachedScale;
        internal float AttachedMass;
        internal bool AttachedTrigger;
        internal bool AttachedFreezeRotation;
        internal Vector3 AttachedColliderStretch;
        // Collider centre in entity-local (scaled) space; dynamic bodies sit on their centre of mass.
        internal Vector3 ColliderOffset;
        // Transform last exchanged with the body, used to detect editor/script moves.
        internal Vector3 SyncedPosition;
        internal Matrix SyncedRotation;
        // The ScenePhysics that owns this entity's body; null without one.
        internal ScenePhysics PhysicsScene;
        internal int PhysicsFrame;
        // The Ragdoll component's bodies (ScenePhysics.CreateRagdoll); null without one.
        internal Ragdoll Ragdoll;
        // The Breakable Glass component's shards (ScenePhysics.CreateGlass); null without one.
        internal GlassShatter Glass;
        // The Breakable component's pieces (ScenePhysics.CreateFracture); null without one.
        internal FractureShatter Fracture;
        // Bones shrunk to a point while skinning (a Breakable head that broke off this body); runtime only.
        internal readonly HashSet<string> CollapsedBones = new(StringComparer.Ordinal);
        // Bumped when CollapsedBones changes, so skins re-pose.
        internal int CollapsedBonesVersion;
        // Collision group shared by this gameobject's own body and its ragdoll parts; 0 until it has a ragdoll.
        internal int CollisionGroup;

        public override Vector3 Position
        {
            get
            {
                return _position;
            }
            set
            {
                WorldTransform.HasChanged = true;
                _position = value;
            }
        }

        private Vector3 _scale;
        public override Vector3 Scale
        {
            get
            {
                return _scale;
            }
            set
            {
                WorldTransform.HasChanged = true;
                _scale = value;
            }
        }

        public override int Id
        {
            get { return _id; }
            set
            {
                _id = value;
                // The ID/picking pass draws WorldTransform.Id; it must follow persisted scene IDs.
                if (WorldTransform != null) WorldTransform.Id = value;
            }
        }

        public Matrix _rotationMatrix;

        public override Matrix RotationMatrix
        {
            get { return _rotationMatrix; }
            set
            {
                _rotationMatrix = value;
                WorldTransform.HasChanged = true;
            }
        }
        
        // Components (audio, Play-mode lifecycle) and picking skip disabled entities.
        public override bool IsEnabled { get; set; } = true;

        /// <summary>What this gameobject is for (Inspector > Role); e.g. Water makes it a volume to float in.</summary>
        public GameObjectRole Role
        {
            get => Enum.TryParse<GameObjectRole>(RoleId, out var role) && Enum.IsDefined(role) ? role : GameObjectRole.Default;
            set => _roleId = Enum.IsDefined(value) ? value.ToString() : "Default";
        }

        private string _roleId = "Default", _layerId = "Default", _tagId = "Untagged";
        public string RoleId { get => ClassificationCatalog.Project.ResolveId(ClassificationKind.Role, _roleId); set => _roleId = value; }
        public string LayerId { get => ClassificationCatalog.Project.ResolveId(ClassificationKind.Layer, _layerId); set => _layerId = value; }
        public string TagId { get => ClassificationCatalog.Project.ResolveId(ClassificationKind.Tag, _tagId); set => _tagId = value; }
        public string RoleName { get => ClassificationCatalog.Project.NameOf(ClassificationKind.Role, RoleId); set => RoleId = ClassificationCatalog.Project.FindName(ClassificationKind.Role, value)?.Id; }
        public string Layer { get => ClassificationCatalog.Project.NameOf(ClassificationKind.Layer, LayerId); set => LayerId = ClassificationCatalog.Project.FindName(ClassificationKind.Layer, value)?.Id; }
        public string Tag { get => ClassificationCatalog.Project.NameOf(ClassificationKind.Tag, TagId); set => TagId = ClassificationCatalog.Project.FindName(ClassificationKind.Tag, value)?.Id; }

        /// <summary>Created by a script during Play (<c>ScriptBehaviour.Spawn</c>); never saved with the scene.</summary>
        public bool IsRuntimeSpawned { get; internal set; }

        /// <summary>
        /// Inspector > Persistent (saved): when a script loads another scene during Play, this gameobject moves
        /// into it with its components and scripts still running. See <c>Logic.PersistentGameObjects</c>.
        /// </summary>
        public bool Persistent { get; set; }

        /// <summary>Set by <c>ScriptBehaviour.DontDestroyOnLoad</c> for the current Play session only; cleared on Stop.</summary>
        public bool RuntimePersistent { get; internal set; }

        /// <summary>True when the next scene load during Play carries this gameobject over.</summary>
        public bool IsPersistent => Persistent || RuntimePersistent;

        /// <summary>
        /// Inspector > Lightmap Static (saved): the lighting bake gives this gameobject a lightmap (indirect light, AO and the
        /// static lights' shadows, Source 2 look phase 4.2). It must not move after the bake.
        /// </summary>
        public bool LightmapStatic { get; set; }

        /// <summary>Inspector > Lightmap Scale (saved): multiplies the scene's lightmap texel density for this gameobject.</summary>
        public float LightmapScale { get => _lightmapScale; set => _lightmapScale = float.IsFinite(value) ? System.Math.Clamp(value, 0.01f, 100f) : 1f; }
        private float _lightmapScale = 1f;

        /// <summary>
        /// Prefab instance: the Content-relative .prefab this gameobject was made from ("Prefabs/Crate.prefab"),
        /// saved with the scene; null for a plain gameobject. See <c>Logic.PrefabUtility</c>.
        /// </summary>
        public string PrefabPath { get; set; }

        /// <summary>True when this gameobject is linked to a prefab (<see cref="PrefabPath"/>).</summary>
        public bool IsPrefabInstance => !string.IsNullOrEmpty(PrefabPath);

        /// <summary>
        /// Posed bones of a skinned model (Anvil Hierarchy > Armature; saved): rotations on top of the rest pose or
        /// the Animator's clip. Empty for other models. See <see cref="Animation.BonePose"/>.
        /// </summary>
        public Animation.BonePose BonePose { get; } = new();

        public override TransformableObject Clone {
            get
            {
                //Not very clean...
                var copy = new BasicEntity(ModelDefinition, Material, Position, RotationMatrix, Scale)
                {
                    Components = Components.Select(ComponentRegistry.Copy).ToList(),
                    RoleId = RoleId,
                    LayerId = LayerId,
                    TagId = TagId,
                    Persistent = Persistent,
                    LightmapStatic = LightmapStatic,
                    LightmapScale = LightmapScale,
                    // A duplicate of a prefab instance is another instance of that prefab, like Unity.
                    PrefabPath = PrefabPath,
                };
                copy.BonePose.CopyFrom(BonePose);
                // Script fields that reference this gameobject reference the copy instead, like Unity.
                foreach (var script in copy.Components.OfType<ScriptBehaviourComponent>())
                    script.RemapGameObjectReferences(Id, copy.Id);
                return copy;
            }  
        }

        public override string Name { get; set; }

        /// <summary>
        /// Optional script behaviours. Driven by <see cref="Logic.PlayModeController"/>:
        /// <see cref="IScript.OnStart"/> runs once on Play, <see cref="IScript.OnUpdate"/>
        /// every Play-mode frame. Empty in edit mode.
        /// </summary>
        public readonly List<IScript> Scripts = new List<IScript>();

        /// <summary>Script behaviours allow multiple attachments; other types are unique by default.</summary>
        public List<GameComponent> Components { get; private set; } = new();

        public T GetComponent<T>() where T : GameComponent
        {
            for (int i = 0; i < Components.Count; i++)
                if (Components[i] is T match) return match;
            return null;
        }

        public IEnumerable<T> GetComponents<T>() where T : GameComponent => Components.OfType<T>();

        /// <summary>Attach a component, enforcing its registry's multiplicity and unique attachment identity.</summary>
        public bool AddComponent(GameComponent component)
        {
            if (component == null || Components.Any(c => c.InstanceId == component.InstanceId)) return false;
            if (ComponentRegistry.Find(component.GetType())?.AllowMultiple != true &&
                Components.Any(c => c.GetType() == component.GetType())) return false;
            Components.Add(component);
            component.OnAdded(this);
            return true;
        }

        public bool RemoveComponent(GameComponent component)
        {
            if (component == null || !Components.Remove(component)) return false;
            component.OnRemoved(this);
            return true;
        }


        public readonly TransformMatrix WorldTransform;
        private Matrix _worldMatrix = Matrix.Identity;
        
        public BasicEntity(ModelDefinition modelbb, MaterialEffect material, Vector3 position, double angleZ, double angleX, double angleY, Vector3 scale, MeshMaterialLibrary library = null)
        {
            Id = IdGenerator.GetNewId();
            Name = GetType().Name + " " + Id;
            WorldTransform = new TransformMatrix(Matrix.Identity, Id);
            ModelDefinition = modelbb;
            Model = modelbb.Model;
            BoundingBox = modelbb.BoundingBox;
            BoundingBoxOffset = modelbb.BoundingBoxOffset;
            SignedDistanceField = modelbb.SDF;
            
            Material = material;
            Position = position;
            Scale = scale;
            
            RotationMatrix = Matrix.CreateRotationX((float)angleX) * Matrix.CreateRotationY((float)angleY) *
                                  Matrix.CreateRotationZ((float)angleZ);

            if (library != null)
                RegisterInLibrary(library);

            WorldTransform.World = Matrix.CreateScale(Scale) * RotationMatrix * Matrix.CreateTranslation(Position);
            WorldTransform.Scale = Scale;
            WorldTransform.InverseWorld = Matrix.Invert(Matrix.CreateTranslation(BoundingBoxOffset * Scale) * RotationMatrix * Matrix.CreateTranslation(Position));
        }

        public BasicEntity(ModelDefinition modelbb, MaterialEffect material, Vector3 position, Matrix rotationMatrix, Vector3 scale)
        {
            Id = IdGenerator.GetNewId();
            Name = GetType().Name + " " + Id;
            WorldTransform = new TransformMatrix(Matrix.Identity, Id);
            Model = modelbb.Model;
            ModelDefinition = modelbb;
            BoundingBox = modelbb.BoundingBox;
            BoundingBoxOffset = modelbb.BoundingBoxOffset;
            SignedDistanceField = modelbb.SDF;

            Material = material;
            Position = position;
            RotationMatrix = rotationMatrix;
            Scale = scale;
            RotationMatrix = rotationMatrix;

            WorldTransform.World = Matrix.CreateScale(Scale) * RotationMatrix * Matrix.CreateTranslation(Position);
            WorldTransform.Scale = Scale;
            WorldTransform.InverseWorld = Matrix.Invert(Matrix.CreateTranslation(BoundingBoxOffset * Scale) * RotationMatrix * Matrix.CreateTranslation(Position));
        }

        /// <summary>The material the library draws a model part with (<paramref name="index"/> = its order in the model).</summary>
        internal MaterialEffect PartMaterial(ModelMeshPart part, int index)
        {
            if (_materialInstances.Count > 0) return _materialInstances[Math.Min(index, _materialInstances.Count - 1)];
            return Material ?? part?.Effect as MaterialEffect ?? FirstPartMaterial();
        }

        private MaterialEffect FirstPartMaterial()
        {
            if (Model == null) return null;
            foreach (var mesh in Model.Meshes)
                foreach (var part in mesh.MeshParts)
                    if (part.Effect is MaterialEffect material) return material;
            return null;
        }

        public void RegisterInLibrary(MeshMaterialLibrary library)
        {
            _materialLibrary = library;
            CollectLevelsOfDetail();
            RegisterModel(library, Model, 0);
            for (int i = 0; i < _lodModels.Count; i++)
                RegisterModel(library, _lodModels[i], i + 1);
        }

        private void RegisterModel(MeshMaterialLibrary library, Model model, int lodLevel)
        {
            var component = Components.OfType<MaterialComponent>().FirstOrDefault(c => c.Enabled);
            if (component == null)
            {
                library.Register(Material, model, WorldTransform, lodLevel);
                return;
            }
            if (model == null) return;
            foreach (var mesh in model.Meshes)
                foreach (var part in mesh.MeshParts)
                {
                    var source = Material ?? part.Effect as MaterialEffect;
                    var instance = source != null ? source.Clone() : new MaterialEffect(part.Effect);
                    component.ApplyTo(instance);
                    _materialInstances.Add(instance);
                    library.Register(instance, part, WorldTransform, mesh.BoundingSphere, lodLevel);
                }
        }

        // Reads the enabled LOD component into the levels the library draws; without one only the model draws.
        private void CollectLevelsOfDetail()
        {
            _lodModels.Clear();
            _lodDistances.Clear();
            _lodCullDistance = 0;
            WorldTransform.LodLevel = 0;
            var lod = GetComponent<LodComponent>();
            if (lod is not { Enabled: true }) return;
            foreach (var level in lod.ResolveLevels(this))
            {
                _lodModels.Add(level.Model);
                _lodDistances.Add(level.Distance);
            }
            _lodCullDistance = lod.ClampedCullDistance;
        }

        /// <summary>
        /// Shows the LOD level for a camera at <paramref name="cameraPosition"/> (see <see cref="LodComponent"/>);
        /// does nothing without an LOD component.
        /// </summary>
        public void SelectLevelOfDetail(Vector3 cameraPosition)
        {
            if (_lodModels.Count == 0 && _lodCullDistance <= 0) return;
            // Measured to the centre of the model's bounds.
            Vector3 center = Position + Vector3.Transform(BoundingBoxOffset * Scale, RotationMatrix);
            int level = LodComponent.SelectLevel(Vector3.Distance(cameraPosition, center), _lodDistances,
                _lodCullDistance, WorldTransform.LodLevel);
            if (level == WorldTransform.LodLevel) return;
            WorldTransform.LodLevel = level;
            // Other meshes draw now: redo culling and redraw shadow maps this frame.
            WorldTransform.HasChanged = true;
        }

        public void RefreshMaterials()
        {
            if (_materialLibrary == null) return;
            var library = _materialLibrary;
            Dispose(library);
            RegisterInLibrary(library);
            WorldTransform.HasChanged = true;
        }

        public void Dispose(MeshMaterialLibrary library)
        {
            library.DeleteFromRegistry(this);
            BonePose.Release(this);
            foreach (var instance in _materialInstances) instance.Dispose();
            _materialInstances.Clear();
            _lodModels.Clear();
            _lodDistances.Clear();
            _lodCullDistance = 0;
            WorldTransform.LodLevel = 0;
            _materialLibrary = null;
        }

        public void ApplyTransformation()
        {
            // Dynamic bodies write their pose back into Position/RotationMatrix
            // (ScenePhysics), so every entity builds its world matrix the same way.
            Matrix scaleMatrix = Matrix.CreateScale(Scale);
            _worldMatrix = scaleMatrix * RotationMatrix * Matrix.CreateTranslation(Position);

            WorldTransform.Scale = Scale;
            WorldTransform.World = _worldMatrix;

            WorldTransform.InverseWorld = Matrix.Invert(Matrix.CreateTranslation(BoundingBoxOffset * Scale) * RotationMatrix * Matrix.CreateTranslation(Position));
        }
    }

    public class TransformMatrix
    {
        public Matrix InverseWorld;
        public bool Rendered = true;
        public bool HasChanged = true;
        // Kept equal to the owning entity's Id (BasicEntity.Id setter).
        public int Id { get; internal set; }

        public Vector3 Scale;

        public Matrix World;
        // World of the previous frame (motion vectors for TAA); valid once HasPrevious, kept by
        // MeshMaterialLibrary.FrustumCullingFinalizeFrame at the end of every frame.
        public Matrix PreviousWorld;
        public bool HasPrevious;

        // Posed vertex buffers (Animator component) drawn in place of the shared mesh's; null = static.
        public Animation.SkinnedMeshInstance Skin;
        // Simulated vertex buffers (Cloth component) drawn in place of the shared mesh's; null = static.
        public Physics.ClothMeshInstance Cloth;
        // Not drawn at all (shadows and picking included): a Particle System's marker mesh during Play.
        public bool Hidden;
        // LOD component: the level of detail that draws (0 = the model, 1..n = its LOD models, -1 = culled).
        public int LodLevel;
        // Foliage Wind component: the vertex shaders sway this mesh in the wind; null = static.
        public FoliageWind Wind;
        // Planar Reflection component: the mirror image its forward (glass) shader shows this frame; null = the probes.
        public Renderer.RenderModules.PlanarReflectionView PlanarReflection;
        // Liquid component: the liquid surface its forward (glass) shader fills this mesh below, and its colours; null = none.
        public Physics.LiquidSlosh Liquid;
        public Components.LiquidLook LiquidLook;

        public TransformMatrix(Matrix world, int id)
        {
            World = world;
            Id = id;
        }

        public Vector3 TransformMatrixSubModel(Vector3 translateSub)
        {
            return Vector3.Transform(translateSub, World);
        }
    }
}
