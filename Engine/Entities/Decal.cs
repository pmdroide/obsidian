using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Engine.Recources;
using Engine.Recources.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Entities
{
    public class Decal : TransformableObject
    {
        public Vector3 _scale;
        private Vector3 _position;
        private Matrix _rotationMatrix;

        public override Vector3 Position
        {
            get { return _position; }
            set { _position = value;
                UpdateWorldMatrix();
            }
        }

        public override int Id { get; set; }

        public sealed override Matrix RotationMatrix
        {
            get { return _rotationMatrix; }
            set { _rotationMatrix = value;
                UpdateWorldMatrix();
            }
        }

        public override bool IsEnabled { get; set; }

        public override TransformableObject Clone
        {
            get { return new Decal(Texture, Position, RotationMatrix, Scale) { TexturePath = TexturePath, IsEnabled = IsEnabled };}
        }

        public override string Name { get; set; }

        public override Vector3 Scale
        {
            get { return _scale; }
            set
            {
                _scale = value; 
                UpdateWorldMatrix();
            }
        }

        public Matrix World;
        public Matrix InverseWorld;
        public Texture2D Texture;
        /// <summary>
        /// Content-relative source path of <see cref="Texture"/> ("Textures/stain.png"), saved with the scene.
        /// Null when the texture is a built-in <c>Assets</c> field (saved by field name, e.g. DefaultDecal).
        /// </summary>
        public string TexturePath;

        /// <summary>Loads a Content texture by its source path (cached by the content manager). Null when it can't.</summary>
        public static Texture2D LoadTexture(string path, ContentManager content = null)
        {
            content ??= Globals.content;
            if (content == null || !Components.MaterialComponent.IsTextureAsset(path)) return null;
            try
            {
                return content.Load<Texture2D>(System.IO.Path.ChangeExtension(path.Replace('\\', '/'), null));
            }
            catch (ContentLoadException ex)
            {
                Editor.EditorBridge.Log($"Decal texture '{path}' could not load: {ex.Message}");
                return null;
            }
        }

        public Decal(Texture2D texture, Vector3 position, double angleZ, double angleX, double angleY, Vector3 scale) : 
            this(texture, position, Matrix.CreateRotationX((float)angleX) * Matrix.CreateRotationY((float)angleY) *
                                  Matrix.CreateRotationZ((float)angleZ), scale)
        { }

        public Decal(Texture2D texture, Vector3 position, Matrix rotationMatrix, Vector3 scale)
        {
            Texture = texture;
            Position = position;
            RotationMatrix = rotationMatrix;

            _scale = scale;
            Id = IdGenerator.GetNewId();
            Name = GetType().Name + " " + Id;

            UpdateWorldMatrix();
        }

        public void UpdateWorldMatrix()
        {
            World = Matrix.CreateScale(Scale) * RotationMatrix * Matrix.CreateTranslation(Position);
            InverseWorld = Matrix.Invert(World);
        }
    }
}
