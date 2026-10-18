using System;
using System.Collections.Generic;
using Engine.Entities;
using Engine.Components;
using GameComponent = Engine.Components.GameComponent;
using Engine.Recources;
using Engine.Renderer.Lighting;
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
        SpotLight,
        // A bone (or the Armature) of a skinned gameobject: Entities.BoneObject, listed in EditorObjectSnapshot.Bones.
        Bone,
    }

    /// <summary>
    /// A bone of a skinned gameobject for Anvil's Hierarchy (Entities.BoneObject). The first entry of a gameobject's
    /// list is its Armature (<see cref="Index"/> -1); the bones follow in skeleton order, parents first.
    /// </summary>
    public readonly struct BoneSnapshot
    {
        /// <summary>Editor ID (Entities.BoneObject.IdFor): select / mutate it like any object.</summary>
        public readonly int Id;
        public readonly string Name;
        /// <summary>Bone index; -1 = the Armature.</summary>
        public readonly int Index;
        /// <summary>Parent bone index; -1 = under the Armature, -2 (the Armature) = under the gameobject.</summary>
        public readonly int ParentIndex;
        /// <summary>World position of the joint.</summary>
        public readonly Vector3 Position;
        /// <summary>Rotation away from the rest pose, in the bone's own space (BoneObject.LocalRotation).</summary>
        public readonly Matrix Rotation;
        public readonly bool IsPosed;

        public BoneSnapshot(int id, string name, int index, int parentIndex, Vector3 position, Matrix rotation, bool isPosed)
        {
            Id = id;
            Name = name;
            Index = index;
            ParentIndex = parentIndex;
            Position = position;
            Rotation = rotation;
            IsPosed = isPosed;
        }
    }

    public readonly struct LightSnapshot
    {
        public readonly Color Color;
        public readonly float Intensity;
        public readonly float Radius;
        public readonly bool CastShadows;
        public readonly bool IsDirectional;
        public readonly Vector3 Direction;
        // Spot lights only: full cone angles in degrees.
        public readonly bool IsSpot;
        public readonly float SpotAngle;
        public readonly float InnerSpotAngle;
        // Shadows: per-face / per-cascade resolution; point/spot: never redrawn once drawn
        public readonly int ShadowResolution;
        public readonly bool StaticShadows;
        // Point/spot light source shape (Source 2 look, phase 2.4): sphere radius and tube length in metres
        public readonly float SourceRadius;
        public readonly float SourceLength;
        // Spot lights: cookie texture (Content path), null = none (phase 2.3)
        public readonly string CookiePath;
        // Directional lights: cascaded shadows (phase 2.1)
        public readonly int CascadeCount;
        public readonly float ShadowDistance;
        public readonly float SplitLambda;

        public LightSnapshot(Color color, float intensity, float radius, bool castShadows, bool isDirectional, Vector3 direction, bool isSpot = false, float spotAngle = 0f, float innerSpotAngle = 0f,
            int shadowResolution = 0, bool staticShadows = false, float sourceRadius = 0, float sourceLength = 0, string cookiePath = null,
            int cascadeCount = 0, float shadowDistance = 0, float splitLambda = 0)
        {
            ShadowResolution = shadowResolution;
            StaticShadows = staticShadows;
            SourceRadius = sourceRadius;
            SourceLength = sourceLength;
            CookiePath = cookiePath;
            CascadeCount = cascadeCount;
            ShadowDistance = shadowDistance;
            SplitLambda = splitLambda;
            IsSpot = isSpot;
            SpotAngle = spotAngle;
            InnerSpotAngle = innerSpotAngle;
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
        // BasicEntity only — every attached component, including Physics, as registry records.
        public readonly IReadOnlyList<ComponentRecord> Components;
        // BasicEntity only.
        public readonly GameObjectRole Role;
        public readonly string Tag, Layer, RoleName;
        // BasicEntity only: the saved Persistent flag (not a script's DontDestroyOnLoad).
        public readonly bool IsPersistent;
        // Decal only: Content-relative texture path (Decal.TexturePath); null = the built-in Assets.DefaultDecal.
        public readonly string DecalTexture;
        // BasicEntity only: the prefab this gameobject is an instance of ("Prefabs/Crate.prefab"), or null.
        public readonly string PrefabPath;
        // Prefab instances, selected gameobject only: what it overrides (Logic.PrefabUtility override paths). Null for
        // other gameobjects and when the prefab file is missing (PrefabMissing).
        public readonly IReadOnlyList<string> PrefabOverrides;
        public readonly bool PrefabMissing;
        // BasicEntity with a skinned model only: its Armature and bones (Anvil shows them as its children); else null.
        public readonly IReadOnlyList<BoneSnapshot> Bones;

        public EditorObjectSnapshot(int id, string name, EditorObjectKind kind, Vector3 position, Matrix rotation, Vector3 scale, bool isEnabled, LightSnapshot? light, MaterialSnapshot? material, IReadOnlyList<ComponentRecord> components = null, GameObjectRole role = GameObjectRole.Default, bool isPersistent = false, string decalTexture = null,
            string prefabPath = null, IReadOnlyList<string> prefabOverrides = null, bool prefabMissing = false, IReadOnlyList<BoneSnapshot> bones = null,
            string tag = "Untagged", string layer = "Default", string roleName = null)
        {
            Tag = tag;
            Layer = layer;
            RoleName = roleName ?? role.ToString();
            Bones = bones;
            PrefabPath = prefabPath;
            PrefabOverrides = prefabOverrides;
            PrefabMissing = prefabMissing;
            DecalTexture = decalTexture;
            Components = components ?? Array.Empty<ComponentRecord>();
            Role = role;
            IsPersistent = isPersistent;
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
        /// <summary>Typed automation commands completed on the game thread, independent of UI snapshot throttling.</summary>
        System.Threading.Tasks.Task<EditorAutomationResult> ExecuteAutomationAsync(EditorAutomationRequest request,
            System.Threading.CancellationToken cancellationToken = default, Func<bool> allowWrite = null);

        InputDevices.InputDeviceSnapshot InputDevices { get; }
        Steam.SteamConnectionStatus SteamStatus { get; }
        void EnqueueSteamConnect();
        void EnqueueSteamDisconnect();
        void EnqueueSteamSettings(bool enabled, uint appId, Action onCompleted);

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
        /// True while the host permits viewport input. Defaults to true
        /// (standalone engine has no host). Anvil sets this false while the Engine
        /// tab is hidden, suppressing input regardless of cursor coordinates.
        /// Viewport bounds separately gate pointer input over surrounding panels.
        /// </summary>
        bool IsHostPointerOverViewport { get; }
        void SetHostPointerOverViewport(bool inside);

        /// <summary>True during an editor asset drag; viewport input must not consume it.</summary>
        bool IsHostDragDropActive { get; }
        void SetHostDragDropActive(bool active);

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
        void EnqueueAddComponent(int entityId, string componentType);
        void EnqueueRemoveComponent(int entityId, string componentType, Guid? instanceId = null);
        void EnqueueMutateComponent(int entityId, string componentType, Action<GameComponent> mutate, Guid? instanceId = null);
        /// <summary>Set a gameobject's <see cref="GameObjectRole"/> and mark the scene dirty.</summary>
        void EnqueueSetRole(int entityId, GameObjectRole role);
        IReadOnlyList<ClassificationDefinition> Classifications { get; }
        void EnqueueSetClassification(int entityId, ClassificationKind kind, string definitionId);
        void EnqueueEditClassification(ClassificationEdit edit, ClassificationKind kind, string definitionId, string name, Action<string> completed);
        /// <summary>Set a gameobject's saved Persistent flag (kept across scene loads during Play) and mark the scene dirty.</summary>
        void EnqueueSetPersistent(int entityId, bool persistent);
        void EnqueuePlayAudio(int entityId, bool play);
        /// <summary>Enable or mute the engine's output, including editor audio previews.</summary>
        void EnqueueSetAudioEnabled(bool enabled);
        void EnqueueAddPointLight(Vector3 position, float radius, Color color, float intensity);
        void EnqueueAddSpotLight(Vector3 position, Vector3 direction, float radius, Color color, float intensity, float spotAngle);
        void EnqueueAddDirectionalLight(Vector3 direction, Color color, float intensity);
        /// <summary>Add a deferred decal projecting <c>Assets.DefaultDecal</c> down its local -Z; the box is 2 x <paramref name="scale"/> m.</summary>
        void EnqueueAddDecal(Vector3 position, Vector3 scale);
        /// <summary>Project a Content texture ("Textures/stain.png") with a decal; null or empty goes back to <c>Assets.DefaultDecal</c>.</summary>
        void EnqueueSetDecalTexture(int decalId, string texturePath);
        /// <param name="name">Gameobject name; null keeps the default.</param>
        /// <param name="scale">Null keeps 1.</param>
        /// <param name="componentTypes">Registered component ids (<see cref="ComponentRegistry"/>) to attach, e.g. a Particle System.</param>
        void EnqueueAddBasicEntity(string modelKey, Vector3 position, string name = null, Vector3? scale = null,
            IReadOnlyList<string> componentTypes = null);
        void EnqueueDelete(int id);

        // ---- Prefabs (Content/Prefabs/*.prefab, see Logic.PrefabUtility). Callbacks run on the game thread. ----

        /// <summary>
        /// Save a gameobject as a new prefab in a Content folder (<paramref name="folder"/>, "Prefabs" when null), named
        /// after it, and link it to that prefab. The callback gets the prefab's Content-relative path, or null and an error.
        /// </summary>
        void EnqueueCreatePrefab(int entityId, string folder, Action<string, string> onCompleted = null);
        /// <summary>
        /// Write a prefab instance's values into its prefab (Apply All) and update the other instances in the scene
        /// (their own overrides stay). Edit mode only. The callback gets how many other instances changed, or -1 and an error.
        /// </summary>
        void EnqueueApplyPrefab(int entityId, Action<int, string> onCompleted = null);
        /// <summary>Drop every override of a prefab instance (Revert All). Edit mode only. The callback gets an error or null.</summary>
        void EnqueueRevertPrefab(int entityId, Action<string> onCompleted = null);
        /// <summary>Unlink a prefab instance from its prefab, keeping its current values. Edit mode only.</summary>
        void EnqueueUnpackPrefab(int entityId);
        /// <summary>
        /// Place a prefab instance (prefab named or pathed as <see cref="Logic.Prefab.Load"/> accepts) at
        /// <paramref name="position"/> and select it. The callback gets its ID, or null when the prefab can't be placed.
        /// </summary>
        void EnqueueInstantiatePrefab(string prefab, Vector3 position, Action<int?> onCompleted = null);

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
        /// <summary>
        /// Edit > Project Settings > Delete Project: swaps in an empty scene, forgets imported models,
        /// runs <see cref="Recources.ProjectReset"/> on this checkout (source files go through
        /// <paramref name="deleteSource"/>, e.g. the Recycle Bin) and saves the new project's
        /// first scene. Edit mode only. The callback runs on the game thread.
        /// </summary>
        void EnqueueResetProject(Action<string> deleteSource, Action<Recources.ProjectReset.Result> onCompleted);
        string CurrentScenePath { get; }
        string CurrentSceneName { get; }
        bool IsSceneDirty { get; }
        event Action SceneChanged;

        // ---- Baked lighting (probe volume) — Inspector > Lighting ----

        /// <summary>Copy of the active scene's lighting settings. Safe to call from the UI thread.</summary>
        LightingSettings GetLightingSettings();

        Logic.EnvironmentSettings GetEnvironmentSettings();
        void EnqueueMutateEnvironment(Action<Logic.EnvironmentSettings> mutate);
        /// <summary>
        /// Queue an edit of the active scene's post processing (EnvironmentSettings.Post: tonemapping, grading, eye
        /// adaptation, bloom). Marks the scene dirty; unlike environment edits it doesn't recapture the reflections.
        /// </summary>
        void EnqueueMutatePost(Action<Logic.PostProcessSettings> mutate);
        /// <summary>Import a PNG/JPEG panorama. Callback returns an error, or null on success.</summary>
        void EnqueueImportSkybox(string sourcePath, Action<string> onCompleted);

        // ---- Render pipeline (Anvil > Render Pipeline): user render features, Renderer.Features.RenderFeatures ----

        /// <summary>
        /// The render pipeline in draw order: built-in passes, injection points and the render features (copies,
        /// with their compile status). Safe to call from the UI thread.
        /// </summary>
        IReadOnlyList<Renderer.Features.RenderPipelineLayer> GetRenderPipeline();

        /// <summary>Fires on the game thread when a render feature, its compile status or the list changed; UI consumers must marshal.</summary>
        event Action RenderPipelineChanged;

        /// <summary>Queue a rebuild of <see cref="GetRenderPipeline"/> (render setting toggles don't notify).</summary>
        void EnqueueRefreshRenderPipeline();

        /// <summary>
        /// Queue a new render feature at <paramref name="at"/>: writes <c>Content/RenderFeatures/&lt;Name&gt;.fx</c>
        /// from the template, saves RenderFeatures.json and compiles it. The callback gets the feature's id on the game thread.
        /// </summary>
        void EnqueueCreateRenderFeature(string name, Renderer.Features.RenderFeatureEvent at, Action<Guid> onCompleted);

        /// <summary>Queue an edit of a render feature's settings (saved).</summary>
        void EnqueueMutateRenderFeature(Guid id, Action<Renderer.Features.RenderFeature> mutate);

        /// <summary>Queue moving a render feature one row up (-1) or down (+1), across built-in passes into the neighbouring injection point.</summary>
        void EnqueueMoveRenderFeature(Guid id, int direction);

        /// <summary>Queue compiling a render feature's shader again.</summary>
        void EnqueueRecompileRenderFeature(Guid id);

        /// <summary>
        /// Queue removing a render feature. With <paramref name="deleteShader"/> its .fx and compiled .xnb go through
        /// <paramref name="deleteFile"/> (Anvil: the Recycle Bin).
        /// </summary>
        void EnqueueDeleteRenderFeature(Guid id, bool deleteShader, Action<string> deleteFile);

        /// <summary>Queue an edit of the active scene's lighting settings (marks the scene dirty).</summary>
        void EnqueueMutateLighting(Action<LightingSettings> mutate);

        /// <summary>Queue a bake of the active scene. Progress arrives via <see cref="LightingStatusChanged"/>.</summary>
        void EnqueueBakeLighting();

        /// <summary>Cancel a running bake. Safe from any thread.</summary>
        void CancelLightingBake();

        /// <summary>Queue removal of the active scene's baked probes.</summary>
        void EnqueueClearBakedLighting();

        /// <summary>One-line summary of the active scene's bake (grid, samples, date), or null.</summary>
        string GetBakedLightingSummary();

        LightingBakeStatus LightingStatus { get; }

        /// <summary>Fires on the game thread or the bake worker; UI consumers must marshal.</summary>
        event Action<LightingBakeStatus> LightingStatusChanged;

        // Model picker support — names of available ModelDefinitions in the Assets registry.
        IReadOnlyList<string> AvailableModelKeys { get; }

        /// <summary>
        /// Queue a runtime import of a model file from disk. Runs on the engine thread:
        /// copies the file under Engine/Content/GameObjects/Models/{key}/, scans sibling textures,
        /// invokes mgcb to build the new entries, loads the resulting model through the
        /// live ContentManager, and registers it with the Assets dictionary. The callback
        /// fires on the game thread with the actual registered key (may differ from the
        /// requested name when collision-deduped) or null on failure.
        /// </summary>
        void EnqueueImportModel(string sourceFilePath, Action<string> onCompleted);

        /// <summary>
        /// Queue a runtime import of texture files for an already-registered model. Copies
        /// them into the model's <c>GameObjects/Models/{key}/Textures/</c> folder, builds them, and
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
        /// Absolute path of the source <c>Engine/Content</c> folder (the one Content.mgcb
        /// lives in), for the editor's Assets browser. Safe to call from the UI thread.
        /// </summary>
        string ContentSourceRoot { get; }

        /// <summary>
        /// Content-relative, extensionless asset path a model key was loaded from
        /// (e.g. <c>GameObjects/Test/cube</c>), or null if unknown. Safe to call from the UI thread.
        /// </summary>
        string GetModelAssetPath(string modelKey);

        /// <summary>
        /// Fires on the engine thread whenever a new model is registered in the Assets
        /// registry (today: only via <see cref="EnqueueImportModel"/>). UI consumers
        /// must marshal to the UI thread. <see cref="AvailableModelKeys"/> already
        /// reflects the addition by the time this event fires.
        /// </summary>
        event Action ModelRegistryChanged;
    }
}
