using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Components;
using Microsoft.Xna.Framework;
using GameComponent = Engine.Components.GameComponent;

namespace Engine.Entities
{
    public class Camera
    {
        /// <summary>
        /// Script Behaviours attached to the scene's main camera (Inspector > Main Camera >
        /// Add Component). They run in Play mode and are saved with the scene. Other
        /// component types need a gameobject, so the camera accepts only Script Behaviours.
        /// </summary>
        public List<GameComponent> Components { get; } = new();

        public static bool SupportsComponent(string typeId) => typeId == ScriptBehaviourComponent.TypeId;

        public T GetComponent<T>() where T : GameComponent => Components.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetComponents<T>() where T : GameComponent => Components.OfType<T>();
        /// <summary>True while an enabled script drives this camera, which turns off the built-in Play controls.</summary>
        public bool HasActiveScript => Components.Any(c => c.Enabled);

        public bool AddComponent(GameComponent component)
        {
            if (component is not ScriptBehaviourComponent script || Components.Contains(component)) return false;
            script.HostCamera = this;
            Components.Add(component);
            component.OnAdded(null);
            return true;
        }

        public bool RemoveComponent(GameComponent component)
        {
            if (!Components.Remove(component)) return false;
            component.OnRemoved(null);
            if (component is ScriptBehaviourComponent script) script.HostCamera = null;
            return true;
        }

        private Vector3 _position;
        private Vector3 _up = Vector3.UnitZ;
        private Vector3 _forward = Vector3.Up;
        private float _fieldOfView = (float) Math.PI/4;

        public bool HasChanged = true;
        public bool HasMoved;

        public Camera(Vector3 position, Vector3 lookat)
        {
            _position = position;
            _forward = lookat - position;
            _forward.Normalize();
        }

        public Vector3 Position
        {
            get
            {
                return _position;
            }
            set
            {
                if (_position != value)
                {
                    _position = value;
                    HasChanged = true;
                    HasMoved = true;
                }
            }
        }
        
        public Vector3 Up
        {
            get
            {
                return _up;
            }
            set
            {
                if (_up != value)
                {
                    _up = value;
                    HasChanged = true;
                }
            }
        }

        public Vector3 Forward
        {
            get
            {
                return _forward;
            }
            set
            {
                if (_forward != value)
                {
                    _forward = value;
                    HasChanged = true;
                }
            }
        }

        public float FieldOfView
        {
            get { return _fieldOfView; }
            set
            {
                _fieldOfView = value;
                HasChanged = true;
            }
        }

        public Vector3 Lookat
        {
            get { return Position + Forward; }
            set
            {
                Forward = value - Position;
                Forward.Normalize();
            }
        }
    }
}
