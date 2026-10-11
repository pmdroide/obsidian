using System;
using System.Collections.Generic;
using System.IO;
using Engine.Components;
using Engine.Entities;
using Engine.Recources;
using Engine.Recources.Helper;
using Microsoft.Xna.Framework;

namespace Engine.Logic
{
    /// <summary>
    /// Persistent gameobjects (<see cref="BasicEntity.IsPersistent"/>) across scene loads during Play.
    ///
    /// When a script loads a scene (<see cref="GameFlow.LoadScene(int, bool)"/>), they leave the departing
    /// scene before its scripts stop and join the new one after it loads, still running: scripts keep
    /// their state, bodies their velocity, HUD layers stay open. The new scene's own copy of a carried
    /// gameobject is left out, so the one that came along wins: the same gameobject from its file
    /// (returning to the scene it was made in), or one marked Persistent with the same name.
    ///
    /// Inside Anvil, Stop puts carried gameobjects from the edited scene back where they were, as they were
    /// when Play started; ones picked up in other scenes are dropped with those scenes.
    /// <see cref="MainSceneLogic"/> drives this and handles the mesh library and physics.
    /// </summary>
    internal sealed class PersistentGameObjects
    {
        private sealed class Carried
        {
            public BasicEntity Entity;
            // The scene file it came from and its ID there, to recognise its copy when that scene loads again.
            public string OriginScene;
            public int OriginId;
            // Anvil: its place in the edited scene and its pose when Play started.
            public Scene HomeScene;
            public int HomeIndex, HomeId;
            public Vector3 HomePosition, HomeScale;
            public Matrix HomeRotation;
        }

        private readonly List<Carried> _carried = new List<Carried>();
        private readonly HashSet<BasicEntity> _entities = new HashSet<BasicEntity>();
        // The arrived scene's left-out copies, by their ID, and the carried gameobject that took their place.
        private readonly Dictionary<int, BasicEntity> _replaced = new Dictionary<int, BasicEntity>();

        /// <summary>The gameobjects being carried (or carried by the last scene load).</summary>
        public IReadOnlyCollection<BasicEntity> Entities => _entities;

        public int Count => _carried.Count;

        /// <summary>
        /// Removes the persistent gameobjects from the departing scene. With <paramref name="isEditScene"/>
        /// (Anvil), records where they belong so Stop can return them.
        /// </summary>
        public void TakeFrom(Scene from, bool isEditScene, PlayModeController playMode)
        {
            // Carried gameobjects destroyed since they arrived are gone for good.
            _carried.RemoveAll(c => !from.BasicEntities.Contains(c.Entity));

            string sceneKey = SceneKey(from.FilePath);
            for (int i = 0; i < from.BasicEntities.Count; i++)
            {
                BasicEntity entity = from.BasicEntities[i];
                if (!entity.IsPersistent || _carried.Exists(c => c.Entity == entity)) continue;
                var carried = new Carried
                {
                    Entity = entity,
                    OriginScene = entity.IsRuntimeSpawned ? null : sceneKey,
                    OriginId = entity.Id,
                };
                if (isEditScene && !entity.IsRuntimeSpawned)
                {
                    carried.HomeScene = from;
                    carried.HomeIndex = i;
                    carried.HomeId = entity.Id;
                    if (playMode == null || !playMode.TryGetStartTransform(entity, out carried.HomePosition, out carried.HomeRotation, out carried.HomeScale))
                    {
                        carried.HomePosition = entity.Position;
                        carried.HomeRotation = entity.RotationMatrix;
                        carried.HomeScale = entity.Scale;
                    }
                }
                _carried.Add(carried);
            }

            foreach (var carried in _carried) from.BasicEntities.Remove(carried.Entity);
            Rebuild();
        }

        /// <summary>
        /// Before <paramref name="scene"/> becomes active: leaves out its copies of carried gameobjects and
        /// gives carried ones fresh IDs where its file already uses theirs.
        /// </summary>
        public void PrepareArrival(Scene scene)
        {
            _replaced.Clear();
            if (_carried.Count == 0) return;
            string sceneKey = SceneKey(scene.FilePath);
            scene.BasicEntities.RemoveAll(IsCopyOfCarried);

            var used = new HashSet<int>();
            foreach (var e in scene.BasicEntities) used.Add(e.Id);
            foreach (var l in scene.PointLights) used.Add(l.Id);
            foreach (var l in scene.DirectionalLights) used.Add(l.Id);
            foreach (var d in scene.Decals) used.Add(d.Id);
            if (scene.EnvironmentSample != null) used.Add(scene.EnvironmentSample.Id);
            foreach (var carried in _carried)
            {
                BasicEntity entity = carried.Entity;
                if (used.Contains(entity.Id))
                {
                    int id;
                    do id = IdGenerator.GetNewId(); while (used.Contains(id));
                    entity.Id = id;
                }
                used.Add(entity.Id);
                // Objects added later must not get a carried gameobject's ID either.
                IdGenerator.Reseed(entity.Id);
            }

            bool IsCopyOfCarried(BasicEntity candidate)
            {
                foreach (var carried in _carried)
                {
                    bool sameObject = sceneKey != null && carried.OriginScene != null && carried.OriginId == candidate.Id &&
                                      string.Equals(carried.OriginScene, sceneKey, StringComparison.OrdinalIgnoreCase);
                    bool sameName = candidate.Persistent && carried.Entity.Name == candidate.Name;
                    if (sameObject || sameName)
                    {
                        _replaced[candidate.Id] = carried.Entity;
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>The carried gameobject that replaced the arrived scene's copy with this ID, or null.</summary>
        public BasicEntity ReplacementFor(int id) => _replaced.TryGetValue(id, out var entity) ? entity : null;

        /// <summary>Adds the carried gameobjects to the (now active) scene, after its own.</summary>
        public void Arrive(Scene scene)
        {
            foreach (var carried in _carried) scene.BasicEntities.Add(carried.Entity);
        }

        /// <summary>Runs <c>OnSceneLoaded</c> on the carried gameobjects' running scripts.</summary>
        public void NotifySceneLoaded()
        {
            foreach (var carried in _carried.ToArray())
                foreach (var script in carried.Entity.GetComponents<ScriptBehaviourComponent>().ToArray())
                    script.Notify("OnSceneLoaded", s => s.OnSceneLoaded());
        }

        /// <summary>
        /// Anvil Stop: takes the carried gameobjects out of <paramref name="active"/>, puts the edited scene's
        /// back with their ID and start pose, and returns every one taken out (for the caller to dispose).
        /// </summary>
        public List<BasicEntity> ReturnHome(Scene active, Scene editScene)
        {
            var removed = new List<BasicEntity>();
            var home = new List<Carried>();
            foreach (var carried in _carried)
            {
                if (!active.BasicEntities.Remove(carried.Entity)) continue; // destroyed during Play
                removed.Add(carried.Entity);
                if (carried.HomeScene == null || !ReferenceEquals(carried.HomeScene, editScene)) continue;
                BasicEntity entity = carried.Entity;
                entity.Id = carried.HomeId;
                entity.Position = carried.HomePosition;
                entity.RotationMatrix = carried.HomeRotation;
                entity.Scale = carried.HomeScale;
                home.Add(carried);
            }
            // Ascending, so each index is the one it had before any of them left.
            home.Sort((a, b) => a.HomeIndex.CompareTo(b.HomeIndex));
            foreach (var carried in home)
                editScene.BasicEntities.Insert(Math.Min(carried.HomeIndex, editScene.BasicEntities.Count), carried.Entity);
            Clear();
            return removed;
        }

        public void Clear()
        {
            _carried.Clear();
            _entities.Clear();
            _replaced.Clear();
        }

        private void Rebuild()
        {
            _entities.Clear();
            foreach (var carried in _carried) _entities.Add(carried.Entity);
        }

        /// <summary>The scene's Content entry ("Scenes/A.obsc"), so the source and output copies match; null when unsaved.</summary>
        private static string SceneKey(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            try { return SceneList.ToEntry(filePath) ?? Path.GetFullPath(filePath); }
            catch (Exception) { return filePath; }
        }
    }
}
