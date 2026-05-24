using System;
using System.Collections.Generic;
using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework;

namespace Engine.Editor
{
    public enum EditorObjectKind
    {
        BasicEntity,
        PointLight,
        DirectionalLight,
        Camera,
        Decal,
    }

    public readonly struct LightSnapshot
    {
        public readonly Color Color;
        public readonly float Intensity;
        public readonly float Radius;
        public readonly bool CastShadows;
        public readonly bool IsDirectional;
        public readonly Vector3 Direction;

        public LightSnapshot(Color color, float intensity, float radius, bool castShadows, bool isDirectional, Vector3 direction)
        {
            Color = color;
            Intensity = intensity;
            Radius = radius;
            CastShadows = castShadows;
            IsDirectional = isDirectional;
            Direction = direction;
        }
    }

    public readonly struct MaterialSnapshot
    {
        public readonly Vector3 DiffuseColor;
        public readonly float Roughness;
        public readonly float Metallic;
        public readonly float EmissiveStrength;
        public readonly bool IsTransparent;
        public readonly int MaterialType;

        public MaterialSnapshot(Vector3 diffuseColor, float roughness, float metallic, float emissiveStrength, bool isTransparent, int materialType)
        {
            DiffuseColor = diffuseColor;
            Roughness = roughness;
            Metallic = metallic;
            EmissiveStrength = emissiveStrength;
            IsTransparent = isTransparent;
            MaterialType = materialType;
        }
    }

    public readonly struct EditorObjectSnapshot
    {
        public readonly int Id;
        public readonly string Name;
        public readonly EditorObjectKind Kind;
        public readonly Vector3 Position;
        public readonly Matrix Rotation;
        public readonly Vector3 Scale;
        public readonly bool IsEnabled;
        public readonly LightSnapshot? Light;
        public readonly MaterialSnapshot? Material;

        public EditorObjectSnapshot(int id, string name, EditorObjectKind kind, Vector3 position, Matrix rotation, Vector3 scale, bool isEnabled, LightSnapshot? light, MaterialSnapshot? material)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            IsEnabled = isEnabled;
            Light = light;
            Material = material;
        }
    }

    public interface IEditorBridge
    {
        IReadOnlyList<EditorObjectSnapshot> Snapshot { get; }
        event Action<IReadOnlyList<EditorObjectSnapshot>> SnapshotUpdated;

        int? SelectedId { get; }
        event Action<int?> SelectionChanged;
        void RequestSelect(int? id);

        // Default spawn point — updated each frame to the point in front of the editor camera.
        Vector3 SpawnPoint { get; }

        // Mutations (all queued — run on the game thread between frames)
        void EnqueueMutate(int id, Action<TransformableObject> mutate);
        void EnqueueMutateMaterial(int entityId, Action<MaterialEffect> mutate);
        void EnqueueAddPointLight(Vector3 position, float radius, Color color, float intensity);
        void EnqueueAddDirectionalLight(Vector3 direction, Color color, float intensity);
        void EnqueueAddBasicEntity(string modelKey, Vector3 position);
        void EnqueueDelete(int id);

        // Gizmo mode (Translate/Rotate/Scale). Set to null to suppress all gizmo
        // interaction (e.g. "Select" tool — selection still works, drag does not).
        void RequestGizmoMode(Logic.EditorLogic.GizmoModes? mode);

        // Play/Stop. Calling Play() while already playing or Stop() while stopped
        // is a no-op. ModeChanged fires on the game thread.
        Logic.GameMode Mode { get; }
        void RequestPlay();
        void RequestStop();
        event Action<Logic.GameMode> ModeChanged;

        // Scene file ops — queued. SceneChanged fires (on the game thread) after a
        // load/new completes; UI consumers must marshal to the UI thread.
        void EnqueueNewScene();
        void EnqueueLoadScene(string path);
        void EnqueueSaveScene(string path);
        string CurrentScenePath { get; }
        string CurrentSceneName { get; }
        bool IsSceneDirty { get; }
        event Action SceneChanged;

        // Model picker support — names of available ModelDefinitions in the Assets registry.
        IReadOnlyList<string> AvailableModelKeys { get; }
    }
}
