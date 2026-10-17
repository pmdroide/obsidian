using System;
using System.Collections.Generic;
using Engine.Components;
using Engine.Entities;
using Engine.Editor;
using Engine.Logic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// Runtime state of the scene's Reflection Probe components (Source 2 look, phase 3.2): their prefiltered cubemaps,
    /// which one needs a capture next, and which (at most <see cref="MaxPerView"/>) the environment pass blends for the
    /// current view. Owned by the Renderer; captures go through Renderer.CaptureCube one probe per frame.
    /// </summary>
    public sealed class ReflectionProbes : IDisposable
    {
        /// <summary>Probes blended per pixel (MAX_PROBES in DeferredEnvironmentMap.fx).</summary>
        public const int MaxPerView = 4;

        public sealed class Probe
        {
            public BasicEntity Owner;
            public ReflectionProbeComponent Component;
            public TextureCube Cube;
            public Matrix CapturedBox;
            public int CapturedRequest = -1;
            public int CapturedResolution;
            public bool Seen;

            public bool NeedsCapture =>
                Cube == null || CapturedRequest != Component.CaptureRequest || CapturedResolution != Component.ClampedResolution ||
                !ReflectionProbeData.SameBox(CapturedBox, ReflectionProbeComponent.BoxWorld(Owner));
        }

        private readonly Dictionary<ReflectionProbeComponent, Probe> _probes = new Dictionary<ReflectionProbeComponent, Probe>();
        private readonly List<Probe> _active = new List<Probe>();
        private readonly List<(Probe Probe, float Distance)> _candidates = new List<(Probe, float)>();

        /// <summary>Probes chosen for this view, highest priority first.</summary>
        public readonly List<Probe> Visible = new List<Probe>();

        public IReadOnlyList<Probe> Active => _active;

        /// <summary>Finds the enabled probes of <paramref name="entities"/> and frees the cubes of removed ones.</summary>
        public void Update(IReadOnlyList<BasicEntity> entities, Scene scene = null, GraphicsDevice graphics = null)
        {
            foreach (Probe probe in _probes.Values) probe.Seen = false;
            _active.Clear();
            if (entities != null)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    BasicEntity entity = entities[i];
                    ReflectionProbeComponent component = entity?.GetComponent<ReflectionProbeComponent>();
                    if (component == null) continue;
                    bool enabled = component.Enabled && entity.IsEnabled;
                    PostProcessVolumeComponent.SetMeshHidden(entity, enabled);
                    if (!enabled) continue;
                    Vector3 scale = entity.Scale;
                    if (Math.Abs(scale.X) < 1e-3f || Math.Abs(scale.Y) < 1e-3f || Math.Abs(scale.Z) < 1e-3f) continue;
                    if (!_probes.TryGetValue(component, out Probe probe))
                        _probes[component] = probe = new Probe { Component = component };
                    probe.Owner = entity;
                    if (probe.Cube == null && graphics != null &&
                        scene?.ReflectionCaptures.TryGetValue(component.InstanceId, out var saved) == true && saved.Matches(entity, component))
                    {
                        try
                        {
                            probe.Cube = saved.Upload(graphics);
                            probe.CapturedBox = saved.Box;
                            probe.CapturedResolution = saved.Resolution;
                            probe.CapturedRequest = component.CaptureRequest;
                        }
                        catch (Exception ex)
                        {
                            EditorBridge.Log("Reflection probe upload failed; recapturing: " + ex.Message);
                        }
                    }
                    probe.Seen = true;
                    _active.Add(probe);
                }
            }

            List<ReflectionProbeComponent> gone = null;
            foreach (var pair in _probes)
                if (!pair.Value.Seen) (gone ??= new List<ReflectionProbeComponent>()).Add(pair.Key);
            if (gone != null)
                foreach (ReflectionProbeComponent key in gone)
                {
                    _probes[key].Cube?.Dispose();
                    _probes.Remove(key);
                }
        }

        /// <summary>Retains a CPU copy for scene saves, including captures made before the first Save As.</summary>
        public void StoreCapture(Probe probe, Scene scene)
        {
            if (scene == null || probe.Owner.IsRuntimeSpawned) return;
            scene.ReflectionCaptures[probe.Component.InstanceId] = ReflectionProbeData.Capture(probe);
            scene.IsDirty = true;
        }

        /// <summary>The probe to capture this frame: the nearest one that needs it, or null.</summary>
        public Probe NextCapture(Vector3 camera)
        {
            Probe best = null;
            float bestDistance = float.MaxValue;
            foreach (Probe probe in _active)
            {
                if (!probe.NeedsCapture) continue;
                float distance = Vector3.DistanceSquared(probe.Owner.Position, camera);
                if (distance < bestDistance) { best = probe; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>Picks the captured probes whose box (grown by its blend distance) the view sees: priority, then nearness.</summary>
        public void SelectVisible(BoundingFrustum frustum, Vector3 camera)
        {
            Visible.Clear();
            _candidates.Clear();
            foreach (Probe probe in _active)
            {
                if (probe.Cube == null) continue;
                Matrix box = ReflectionProbeComponent.BoxWorld(probe.Owner);
                var bounds = BoundingBox.CreateFromPoints(Corners(box));
                if (frustum != null && frustum.Contains(bounds) == ContainmentType.Disjoint) continue;
                Vector3 closest = Vector3.Clamp(camera, bounds.Min, bounds.Max);
                _candidates.Add((probe, Vector3.Distance(closest, camera)));
            }
            _candidates.Sort((a, b) => a.Probe.Component.Priority != b.Probe.Component.Priority
                ? b.Probe.Component.Priority.CompareTo(a.Probe.Component.Priority)
                : a.Distance.CompareTo(b.Distance));
            for (int i = 0; i < _candidates.Count && Visible.Count < MaxPerView; i++)
                Visible.Add(_candidates[i].Probe);
        }

        private static readonly Vector3[] UnitCorners =
        {
            new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
            new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1),
        };

        private static IEnumerable<Vector3> Corners(Matrix box)
        {
            foreach (Vector3 corner in UnitCorners) yield return Vector3.Transform(corner, box);
        }

        public void Dispose()
        {
            foreach (Probe probe in _probes.Values) probe.Cube?.Dispose();
            _probes.Clear();
            _active.Clear();
            Visible.Clear();
        }
    }
}
