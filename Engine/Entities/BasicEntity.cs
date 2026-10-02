using System.Collections.Generic;
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

        private int _id;

        private Vector3 _position;

        // Physics component — authored in Anvil's Inspector > Physics and saved with the
        // scene. ScenePhysics creates/rebuilds the actual BEPU body to match these.
        public PhysicsBodyType PhysicsType = PhysicsBodyType.None;
        public float Mass = 1f;

        // BEPUphysics v2 runtime state, owned by ScenePhysics. Handles are value-type
        // indices into the simulation; null when the entity has no body of that kind.
        public StaticHandle? StaticBody = null;
        public BodyHandle? DynamicBody = null;
        internal PhysicsBodyType AttachedPhysicsType;
        internal Vector3 AttachedScale;
        internal float AttachedMass;
        // Collider centre in entity-local (scaled) space; dynamic bodies sit on their centre of mass.
        internal Vector3 ColliderOffset;
        // Transform last exchanged with the body, used to detect editor/script moves.
        internal Vector3 SyncedPosition;
        internal Matrix SyncedRotation;
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

        public override int Id {
            get { return _id; }
            set { _id = value; } }

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
        
        public override bool IsEnabled { get; set; }

        public override TransformableObject Clone {
            get
            {
                //Not very clean...
                return new BasicEntity(ModelDefinition, Material, Position, RotationMatrix, Scale)
                {
                    PhysicsType = PhysicsType,
                    Mass = Mass,
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
            library.Register(Material, Model, WorldTransform);
        }

        public void Dispose(MeshMaterialLibrary library)
        {
            library.DeleteFromRegistry(this);
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
        public readonly int Id;

        public Vector3 Scale;

        public Matrix World;

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
