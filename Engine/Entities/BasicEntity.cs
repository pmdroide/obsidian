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
        // Collider centre in entity-local (scaled) space; dynamic bodies sit on their centre of mass.
        internal Vector3 ColliderOffset;
        // Transform last exchanged with the body, used to detect editor/script moves.
        internal Vector3 SyncedPosition;
        internal Matrix SyncedRotation;
        // The ScenePhysics that owns this entity's body; null without one.
        internal ScenePhysics PhysicsScene;
        internal int PhysicsFrame;

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
        public GameObjectRole Role { get; set; } = GameObjectRole.Default;

        /// <summary>Created by a script during Play (<c>ScriptBehaviour.Spawn</c>); never saved with the scene.</summary>
        public bool IsRuntimeSpawned { get; internal set; }

        public override TransformableObject Clone {
            get
            {
                //Not very clean...
                return new BasicEntity(ModelDefinition, Material, Position, RotationMatrix, Scale)
                {
                    Components = Components.Select(ComponentRegistry.Copy).ToList(),
                    Role = Role,
                };
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

        public void RegisterInLibrary(MeshMaterialLibrary library)
        {
            _materialLibrary = library;
            var component = Components.OfType<MaterialComponent>().FirstOrDefault(c => c.Enabled);
            if (component == null)
            {
                library.Register(Material, Model, WorldTransform);
                return;
            }
            if (Model == null) return;
            foreach (var mesh in Model.Meshes)
                foreach (var part in mesh.MeshParts)
                {
                    var source = Material ?? part.Effect as MaterialEffect;
                    var instance = source != null ? source.Clone() : new MaterialEffect(part.Effect);
                    component.ApplyTo(instance);
                    _materialInstances.Add(instance);
                    library.Register(instance, part, WorldTransform, mesh.BoundingSphere);
                }
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
            foreach (var instance in _materialInstances) instance.Dispose();
            _materialInstances.Clear();
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

        // Posed vertex buffers (Animator component) drawn in place of the shared mesh's; null = static.
        public Animation.SkinnedMeshInstance Skin;

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
