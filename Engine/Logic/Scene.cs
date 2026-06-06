using System.Collections.Generic;
using Engine.Entities;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Logic
{
    /// <summary>
    /// Scene content: the data a user creates, edits, and saves to a <c>.obsc</c> file.
    /// Runtime drivers (physics space, mesh/material library, SDF generator, editor camera)
    /// stay on <see cref="MainSceneLogic"/> — Scene holds only what should round-trip to disk.
    /// </summary>
    public class Scene
    {
        public string Name = "Untitled";
        public string FilePath;
        public bool IsDirty;

        public readonly List<BasicEntity> BasicEntities = new List<BasicEntity>();
        public readonly List<Decal> Decals = new List<Decal>();
        public readonly List<PointLight> PointLights = new List<PointLight>();
        public readonly List<DirectionalLight> DirectionalLights = new List<DirectionalLight>();

        public EnvironmentSample EnvironmentSample;

        /// <summary>
        /// The scene's game camera. The editor camera lives on <see cref="MainSceneLogic"/>
        /// and is not part of saved scene data.
        /// </summary>
        public Camera MainCamera;

        public void Clear()
        {
            BasicEntities.Clear();
            Decals.Clear();
            PointLights.Clear();
            DirectionalLights.Clear();
            EnvironmentSample = null;
            MainCamera = null;
        }
    }
}
