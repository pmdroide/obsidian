using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;

namespace Engine.Editor
{
    /// <summary>
    /// Thread-safe queue + snapshot publisher that bridges the engine (game thread)
    /// and the editor UI (Avalonia UI thread). All public surface is the IEditorBridge
    /// interface; the engine pokes the internal Bind/DrainAndPublish methods.
    /// </summary>
    public sealed class EditorBridge : IEditorBridge
    {
        private readonly ConcurrentQueue<Action> _pendingOps = new ConcurrentQueue<Action>();
        private IReadOnlyList<EditorObjectSnapshot> _snapshot = Array.Empty<EditorObjectSnapshot>();
        private int? _selectedId;
        private Vector3 _spawnPoint;

        private MainSceneLogic _scene;
        private EditorLogic _editor;
        private Assets _assets;
        private Dictionary<string, ModelDefinition> _modelKeys = new Dictionary<string, ModelDefinition>();
        private List<string> _modelKeyList = new List<string>();

        // Throttle snapshot publication: 60fps engine / 6 = ~10Hz. Lower than
        // 20Hz because each publish re-evaluates every NumericUpDown / ColorPicker
        // value in the inspector — too frequent and the user loses input focus.
        private int _framesSinceLastPublish;
        private const int PublishEveryNFrames = 6;

        public IReadOnlyList<EditorObjectSnapshot> Snapshot => _snapshot;
        public int? SelectedId => _selectedId;
        public Vector3 SpawnPoint => _spawnPoint;
        public IReadOnlyList<string> AvailableModelKeys => _modelKeyList;

        // Diagnostic log paths. Desktop is the primary (easy to find for the user);
        // %LOCALAPPDATA%\Anvil is the fallback when Desktop writes fail (locked-down
        // user profiles, redirected folders, OneDrive sync conflicts).
        public static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "anvil-bridge.log");

        public static readonly string FallbackLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Anvil",
            "anvil-bridge.log");

        // Static ctor: emit a bootstrap line before any other engine code runs.
        // If logging itself is broken (both paths fail), nothing else this class
        // does can be diagnosed — so this is the canary.
        static EditorBridge()
        {
            Log($"--- EditorBridge static init pid={Environment.ProcessId} ---");
        }

        public static void Log(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}";
            try { File.AppendAllText(LogPath, line); return; }
            catch { /* fall through to fallback */ }
            try
            {
                string dir = Path.GetDirectoryName(FallbackLogPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(FallbackLogPath, line);
            }
            catch { /* logging never throws */ }
        }

        public event Action<IReadOnlyList<EditorObjectSnapshot>> SnapshotUpdated;
        public event Action<int?> SelectionChanged;
        public event Action SceneChanged;
        public event Action<Logic.GameMode> ModeChanged;

        public string CurrentScenePath => _scene?.ActiveScene?.FilePath;
        public string CurrentSceneName => _scene?.ActiveScene?.Name;
        public bool IsSceneDirty => _scene?.ActiveScene?.IsDirty ?? false;

        public Logic.GameMode Mode => _scene?.PlayMode?.Mode ?? Logic.GameMode.Edit;

        internal void Bind(MainSceneLogic scene, EditorLogic editor, Assets assets)
        {
            _scene = scene;
            _editor = editor;
            _assets = assets;
            BuildModelKeys(assets);

            // Forward Scene swap events to UI consumers.
            if (scene != null)
            {
                scene.SceneManager.SceneChanged += OnSceneSwap;
                if (scene.PlayMode != null) scene.PlayMode.ModeChanged += OnModeChanged;
            }

            // Confirms logging works AND that the bridge has been wired. If
            // the file does not appear at LogPath after a run, the bridge
            // never got bound (engine likely failed to boot).
            Log($"Bridge bound — scene={(scene != null)}, editor={(editor != null)}, assets={(assets != null)}, models={_modelKeyList.Count}");
        }

        private void OnSceneSwap(Logic.Scene oldScene, Logic.Scene newScene)
        {
            // Clear selection — the old SelectedObject reference is dead.
            if (_editor != null) _editor.SelectedObject = null;
            try { SceneChanged?.Invoke(); }
            catch (Exception ex) { Log("SceneChanged handler threw: " + ex); }
        }

        private void BuildModelKeys(Assets assets)
        {
            _modelKeys.Clear();
            _modelKeyList.Clear();
            if (assets == null) return;

            FieldInfo[] fields = typeof(Assets).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                if (f.FieldType != typeof(ModelDefinition)) continue;
                if (f.GetValue(assets) is ModelDefinition md && md != null)
                {
                    _modelKeys[f.Name] = md;
                    _modelKeyList.Add(f.Name);
                }
            }
        }

        // ---------- enqueue API ----------

        public void RequestSelect(int? id)
        {
            _pendingOps.Enqueue(() =>
            {
                if (_editor == null || _scene == null) return;
                if (id == null) { _editor.SelectedObject = null; return; }
                TransformableObject found = LookupById(id.Value);
                _editor.SelectedObject = found;
            });
        }

        public void EnqueueMutate(int id, Action<TransformableObject> mutate)
        {
            if (mutate == null) return;
            _pendingOps.Enqueue(() =>
            {
                TransformableObject obj = LookupById(id);
                if (obj == null) return;
                try { mutate(obj); } catch { /* swallow per-op errors */ }
            });
        }

        public void EnqueueMutateMaterial(int entityId, Action<MaterialEffect> mutate)
        {
            if (mutate == null) return;
            _pendingOps.Enqueue(() =>
            {
                BasicEntity be = LookupEntityById(entityId);
                if (be == null || be.Material == null) return;
                try { mutate(be.Material); } catch { /* swallow */ }
            });
        }

        public void EnqueueAddPointLight(Vector3 position, float radius, Color color, float intensity)
        {
            Log($"EnqueueAddPointLight pos={position} radius={radius} intensity={intensity}");
            _pendingOps.Enqueue(() =>
            {
                if (_scene == null) { Log("AddPointLight: scene is null"); return; }
                try
                {
                    PointLight light = _scene.EditorAddPointLight(position, radius, color, intensity);
                    Log($"AddPointLight ok: id={light?.Id}");
                }
                catch (Exception ex) { Log("AddPointLight threw: " + ex); }
            });
        }

        public void EnqueueAddDirectionalLight(Vector3 direction, Color color, float intensity)
        {
            Log($"EnqueueAddDirectionalLight dir={direction} intensity={intensity}");
            _pendingOps.Enqueue(() =>
            {
                if (_scene == null) { Log("AddDirectionalLight: scene is null"); return; }
                try
                {
                    DirectionalLight light = _scene.EditorAddDirectionalLight(direction, color, intensity);
                    Log($"AddDirectionalLight ok: id={light?.Id}");
                }
                catch (Exception ex) { Log("AddDirectionalLight threw: " + ex); }
            });
        }

        public void EnqueueAddBasicEntity(string modelKey, Vector3 position)
        {
            Log($"EnqueueAddBasicEntity modelKey={modelKey} pos={position}");
            _pendingOps.Enqueue(() =>
            {
                if (_scene == null || _assets == null) { Log("AddBasicEntity: scene/assets null"); return; }
                if (string.IsNullOrEmpty(modelKey)) { Log("AddBasicEntity: empty modelKey"); return; }
                if (!_modelKeys.TryGetValue(modelKey, out ModelDefinition md) || md == null)
                {
                    Log($"AddBasicEntity: model '{modelKey}' not in registry");
                    return;
                }
                try
                {
                    MaterialEffect material = _assets.BaseMaterial?.Clone();
                    BasicEntity entity = _scene.EditorAddBasicEntity(md, material, position);
                    Log($"AddBasicEntity ok: id={entity?.Id}");
                }
                catch (Exception ex) { Log("AddBasicEntity threw: " + ex); }
            });
        }

        public void EnqueueDelete(int id)
        {
            _pendingOps.Enqueue(() =>
            {
                if (_scene == null) return;
                _scene.EditorDelete(id);
            });
        }

        public void EnqueueNewScene()
        {
            Log("EnqueueNewScene");
            _pendingOps.Enqueue(() =>
            {
                if (_scene == null) { Log("NewScene: scene-logic null"); return; }
                try { _scene.SceneManager.NewScene(); }
                catch (Exception ex) { Log("NewScene threw: " + ex); }
            });
        }

        public void EnqueueLoadScene(string path)
        {
            Log($"EnqueueLoadScene path='{path}'");
            _pendingOps.Enqueue(() =>
            {
                if (_scene == null) { Log("LoadScene: scene-logic null"); return; }
                try
                {
                    Logic.Scene loaded = _scene.SceneManager.LoadScene(path);
                    if (loaded == null) Log("LoadScene returned null");
                }
                catch (Exception ex) { Log("LoadScene threw: " + ex); }
            });
        }

        public void EnqueueSaveScene(string path)
        {
            Log($"EnqueueSaveScene path='{path}'");
            _pendingOps.Enqueue(() =>
            {
                if (_scene == null) { Log("SaveScene: scene-logic null"); return; }
                try
                {
                    bool ok = _scene.SceneManager.SaveScene(path);
                    if (!ok) Log("SaveScene returned false");
                }
                catch (Exception ex) { Log("SaveScene threw: " + ex); }
            });
        }

        public void RequestGizmoMode(EditorLogic.GizmoModes? mode)
        {
            _pendingOps.Enqueue(() =>
            {
                if (_editor == null) return;
                if (mode is EditorLogic.GizmoModes m)
                {
                    GameStats.e_gizmoMode = m;
                    _editor.IsGizmoSuppressed = false;
                }
                else
                {
                    // null → Select tool: gizmo hidden, only picking active.
                    _editor.IsGizmoSuppressed = true;
                }
            });
        }

        public void RequestPlay()
        {
            _pendingOps.Enqueue(() =>
            {
                if (_scene?.PlayMode == null) { Log("RequestPlay: PlayMode null"); return; }
                try { _scene.PlayMode.Play(); }
                catch (Exception ex) { Log("Play threw: " + ex); }
            });
        }

        public void RequestStop()
        {
            _pendingOps.Enqueue(() =>
            {
                if (_scene?.PlayMode == null) { Log("RequestStop: PlayMode null"); return; }
                try { _scene.PlayMode.Stop(); }
                catch (Exception ex) { Log("Stop threw: " + ex); }
            });
        }

        private void OnModeChanged(Logic.GameMode mode)
        {
            try { ModeChanged?.Invoke(mode); }
            catch (Exception ex) { Log("ModeChanged handler threw: " + ex); }
        }

        // ---------- engine-side drain ----------

        /// <summary>
        /// Called once per frame on the game thread. Drains queued ops, refreshes the
        /// snapshot, and raises events. Events fire on the game thread — UI consumers
        /// must marshal to the UI thread themselves.
        /// </summary>
        internal void DrainAndPublish()
        {
            if (_scene == null || _editor == null) return;

            // 1. Drain queued operations.
            while (_pendingOps.TryDequeue(out Action op))
            {
                try { op(); }
                catch (Exception ex) { Log("op threw: " + ex); }
            }

            // 2. Cache spawn point for "Add at camera focus".
            if (_scene.Camera != null)
            {
                _spawnPoint = _scene.Camera.Position + _scene.Camera.Forward * 5f;
            }

            // 3. Selection sync.
            int? sel = _editor.SelectedObject?.Id;
            if (sel != _selectedId)
            {
                _selectedId = sel;
                try { SelectionChanged?.Invoke(sel); }
                catch (Exception ex) { Log("SelectionChanged threw: " + ex); }
            }

            // 4. Throttle snapshot publication.
            _framesSinceLastPublish++;
            if (_framesSinceLastPublish < PublishEveryNFrames) return;
            _framesSinceLastPublish = 0;

            List<EditorObjectSnapshot> next;
            try { next = BuildSnapshot(); }
            catch (Exception ex) { Log("BuildSnapshot threw: " + ex); return; }

            _snapshot = next;
            try { SnapshotUpdated?.Invoke(next); }
            catch (Exception ex) { Log("SnapshotUpdated handler threw: " + ex); }
        }

        private List<EditorObjectSnapshot> BuildSnapshot()
        {
            int capacity = (_scene.BasicEntities?.Count ?? 0)
                         + (_scene.PointLights?.Count ?? 0)
                         + (_scene.DirectionalLights?.Count ?? 0)
                         + (_scene.Decals?.Count ?? 0)
                         + 1;
            List<EditorObjectSnapshot> list = new List<EditorObjectSnapshot>(capacity);

            // Camera — synthesize a snapshot entry so the hierarchy shows it.
            if (_scene.Camera != null)
            {
                list.Add(new EditorObjectSnapshot(
                    id: -1,
                    name: "Main Camera",
                    kind: EditorObjectKind.Camera,
                    position: _scene.Camera.Position,
                    rotation: Matrix.Identity,
                    scale: Vector3.One,
                    isEnabled: true,
                    light: null,
                    material: null));
            }

            for (int i = 0; i < _scene.BasicEntities.Count; i++)
            {
                BasicEntity be = _scene.BasicEntities[i];
                MaterialSnapshot? matSnap = null;
                if (be.Material != null)
                {
                    matSnap = new MaterialSnapshot(
                        diffuseColor: be.Material.DiffuseColor,
                        roughness: be.Material.Roughness,
                        metallic: be.Material.Metallic,
                        emissiveStrength: be.Material.EmissiveStrength,
                        isTransparent: be.Material.IsTransparent,
                        materialType: (int)be.Material.Type);
                }
                list.Add(new EditorObjectSnapshot(
                    id: be.Id,
                    name: be.Name ?? ("BasicEntity " + be.Id),
                    kind: EditorObjectKind.BasicEntity,
                    position: be.Position,
                    rotation: be.RotationMatrix,
                    scale: be.Scale,
                    isEnabled: be.IsEnabled,
                    light: null,
                    material: matSnap));
            }

            for (int i = 0; i < _scene.PointLights.Count; i++)
            {
                PointLight p = _scene.PointLights[i];
                LightSnapshot ls = new LightSnapshot(
                    color: p.Color,
                    intensity: p.Intensity,
                    radius: p.Radius,
                    castShadows: p.CastShadows,
                    isDirectional: false,
                    direction: Vector3.Zero);
                list.Add(new EditorObjectSnapshot(
                    id: p.Id,
                    name: p.Name ?? ("PointLight " + p.Id),
                    kind: EditorObjectKind.PointLight,
                    position: p.Position,
                    rotation: Matrix.Identity,
                    scale: Vector3.One,
                    isEnabled: p.IsEnabled,
                    light: ls,
                    material: null));
            }

            for (int i = 0; i < _scene.DirectionalLights.Count; i++)
            {
                DirectionalLight d = _scene.DirectionalLights[i];
                LightSnapshot ls = new LightSnapshot(
                    color: d.Color,
                    intensity: d.Intensity,
                    radius: 0f,
                    castShadows: d.CastShadows,
                    isDirectional: true,
                    direction: d.Direction);
                list.Add(new EditorObjectSnapshot(
                    id: d.Id,
                    name: d.Name ?? ("DirectionalLight " + d.Id),
                    kind: EditorObjectKind.DirectionalLight,
                    position: d.Position,
                    rotation: d.RotationMatrix,
                    scale: Vector3.One,
                    isEnabled: d.IsEnabled,
                    light: ls,
                    material: null));
            }

            for (int i = 0; i < _scene.Decals.Count; i++)
            {
                Decal dc = _scene.Decals[i];
                list.Add(new EditorObjectSnapshot(
                    id: dc.Id,
                    name: dc.Name ?? ("Decal " + dc.Id),
                    kind: EditorObjectKind.Decal,
                    position: dc.Position,
                    rotation: dc.RotationMatrix,
                    scale: dc.Scale,
                    isEnabled: dc.IsEnabled,
                    light: null,
                    material: null));
            }

            return list;
        }

        private TransformableObject LookupById(int id)
        {
            for (int i = 0; i < _scene.BasicEntities.Count; i++)
                if (_scene.BasicEntities[i].Id == id) return _scene.BasicEntities[i];
            for (int i = 0; i < _scene.PointLights.Count; i++)
                if (_scene.PointLights[i].Id == id) return _scene.PointLights[i];
            for (int i = 0; i < _scene.DirectionalLights.Count; i++)
                if (_scene.DirectionalLights[i].Id == id) return _scene.DirectionalLights[i];
            for (int i = 0; i < _scene.Decals.Count; i++)
                if (_scene.Decals[i].Id == id) return _scene.Decals[i];
            return null;
        }

        private BasicEntity LookupEntityById(int id)
        {
            for (int i = 0; i < _scene.BasicEntities.Count; i++)
                if (_scene.BasicEntities[i].Id == id) return _scene.BasicEntities[i];
            return null;
        }
    }
}
