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
        /// <summary>
        /// True when the engine is embedded in an external editor (Anvil). Used to
        /// hide the legacy in-engine HelperSuite GUI and to switch input plumbing to
        /// the editor-forwarded path. Standalone <c>Engine.exe</c> leaves this false.
        /// </summary>
        bool IsHostedByEditor { get; }

        /// <summary>
        /// Host-forwarded keyboard state. Avalonia owns keyboard focus when the
        /// engine HWND is reparented, so <c>Keyboard.GetState()</c> returns no
        /// keys. The host pushes key down/up via <see cref="SetHostKeyState"/>
        /// and the engine reads via <see cref="IsHostKeyDown"/>.
        /// </summary>
        bool IsHostKeyDown(int xnaKeyCode);
        void SetHostKeyState(int xnaKeyCode, bool down);

        /// <summary>
        /// True while the host pointer is hovering over the viewport. Defaults to
        /// true (standalone engine has no host). When hosted in Anvil and the
        /// cursor is over the Inspector / Hierarchy / Console, this is false and
        /// the engine input layer treats the mouse as idle so picking and camera
        /// drag don't leak across Avalonia panels.
        /// </summary>
        bool IsHostPointerOverViewport { get; }
        void SetHostPointerOverViewport(bool inside);

        IReadOnlyList<EditorObjectSnapshot> Snapshot { get; }
        event Action<IReadOnlyList<EditorObjectSnapshot>> SnapshotUpdated;

        int? SelectedId { get; }
        event Action<int?> SelectionChanged;
        void RequestSelect(int? id);

        // Default spawn point — updated each frame to the point in front of the editor camera.
        Vector3 SpawnPoint { get; }

        // Mutations (all queued — run on the game thread between frames)

        /// <summary>
        /// Queue a global engine-thread action — used for post-processing /
        /// render-setting tweaks from the editor that touch shader parameters
        /// (those must be set on the game thread). The lambda runs once,
        /// between the next two frames.
        /// </summary>
        void EnqueueGameThreadAction(Action action);

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

        /// <summary>
        /// Queue a runtime import of a model file from disk. Runs on the engine thread:
        /// copies the file under Engine/Content/Art/Models/{key}/, scans sibling textures,
        /// invokes mgcb to build the new entries, loads the resulting model through the
        /// live ContentManager, and registers it with the Assets dictionary. The callback
        /// fires on the game thread with the actual registered key (may differ from the
        /// requested name when collision-deduped) or null on failure.
        /// </summary>
        void EnqueueImportModel(string sourceFilePath, Action<string> onCompleted);

        /// <summary>
        /// Queue a runtime import of texture files for an already-registered model. Copies
        /// them into the model's <c>Art/Models/{key}/Textures/</c> folder, builds them, and
        /// binds a material by filename convention (<c>*_BaseColor</c> → albedo, etc.).
        /// Updates instances already in the scene. The callback fires on the game thread.
        /// </summary>
        void EnqueueImportTextures(string modelKey, string[] sourcePaths, Action onCompleted);

        /// <summary>
        /// Queue permanent deletion of a runtime-imported model: unregisters it and deletes
        /// its content (files + Content.mgcb entries) from disk. Built-in models are ignored.
        /// The callback fires on the game thread once done.
        /// </summary>
        void EnqueueDeleteModelAsset(string modelKey, Action onCompleted);

        /// <summary>
        /// Texture file names currently in a model's <c>Textures/</c> folder (for the
        /// Assets panel). Safe to call from the UI thread.
        /// </summary>
        IReadOnlyList<string> GetModelTextureFiles(string modelKey);

        /// <summary>
        /// True if the model key refers to a runtime-imported model (deletable / a valid
        /// texture-drop target). False for built-ins. Safe to call from the UI thread.
        /// </summary>
        bool IsDeletableModel(string modelKey);

        /// <summary>
        /// Fires on the engine thread whenever a new model is registered in the Assets
        /// registry (today: only via <see cref="EnqueueImportModel"/>). UI consumers
        /// must marshal to the UI thread. <see cref="AvailableModelKeys"/> already
        /// reflects the addition by the time this event fires.
        /// </summary>
        event Action ModelRegistryChanged;
    }
}
