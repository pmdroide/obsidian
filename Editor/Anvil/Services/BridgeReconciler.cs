using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Anvil.Models;
using Avalonia.Media;
using Engine.Editor;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Anvil.Services;

/// <summary>
/// Reconciles a snapshot list from the engine into the existing
/// <see cref="ObservableCollection{SceneObjectViewModel}"/> the UI binds to. Avoids
/// recreating VMs on each tick so XAML bindings stay alive and selection persists.
/// All write paths set <see cref="SceneObjectViewModel.SuppressPush"/> so VM setters
/// don't re-enqueue the values we just applied — that would create a feedback loop.
/// </summary>
public static class BridgeReconciler
{
    public static void Apply(
        IReadOnlyList<EditorObjectSnapshot> snapshot,
        ObservableCollection<SceneObjectViewModel> target,
        IEditorBridge bridge)
    {
        // Index existing VMs by engine id for O(N+M) reconciliation.
        var byId = new Dictionary<int, SceneObjectViewModel>(target.Count);
        for (int i = 0; i < target.Count; i++)
        {
            var vm = target[i];
            if (vm.EngineId.HasValue) byId[vm.EngineId.Value] = vm;
        }

        var seenIds = new HashSet<int>();
        // Update or insert
        for (int i = 0; i < snapshot.Count; i++)
        {
            var snap = snapshot[i];
            seenIds.Add(snap.Id);
            if (!byId.TryGetValue(snap.Id, out var vm))
            {
                vm = new SceneObjectViewModel { Id = "engine-" + snap.Id };
                vm.AttachBridge(bridge, snap.Id, snap.Kind);
                target.Add(vm);
            }
            CopySnapshotInto(vm, snap, bridge);
        }

        // Remove anything no longer in the snapshot.
        for (int i = target.Count - 1; i >= 0; i--)
        {
            var vm = target[i];
            if (!vm.EngineId.HasValue) continue;
            if (!seenIds.Contains(vm.EngineId.Value)) target.RemoveAt(i);
        }
    }

    // Epsilon for float-precision compares. NumericUpDown re-renders/loses focus
    // on every Value change notification — even when the snapshot delivers what
    // is effectively the same number with one bit of float noise. Skip writes
    // smaller than this so the user can keep typing without losing focus.
    private const double FloatEpsilon = 1e-4;

    private static void SetIfChanged(Action<double> setter, double current, double next)
    {
        if (Math.Abs(current - next) > FloatEpsilon) setter(next);
    }

    private static void CopySnapshotInto(SceneObjectViewModel vm, EditorObjectSnapshot snap, IEditorBridge bridge)
    {
        vm.BeginSuppressPush();
        try
        {
            if (vm.Name != snap.Name) vm.Name = snap.Name;
            var newObjectType = MapKindToType(snap.Kind);
            if (vm.Type != newObjectType) vm.Type = newObjectType;
            if (vm.Visible != snap.IsEnabled) vm.Visible = snap.IsEnabled;

            SetIfChanged(v => vm.PositionX = v, vm.PositionX, snap.Position.X);
            SetIfChanged(v => vm.PositionY = v, vm.PositionY, snap.Position.Y);
            SetIfChanged(v => vm.PositionZ = v, vm.PositionZ, snap.Position.Z);

            var (rx, ry, rz) = RotationConversion.MatrixToEuler(snap.Rotation);
            SetIfChanged(v => vm.RotationX = v, vm.RotationX, rx);
            SetIfChanged(v => vm.RotationY = v, vm.RotationY, ry);
            SetIfChanged(v => vm.RotationZ = v, vm.RotationZ, rz);

            SetIfChanged(v => vm.ScaleX = v, vm.ScaleX, snap.Scale.X);
            SetIfChanged(v => vm.ScaleY = v, vm.ScaleY, snap.Scale.Y);
            SetIfChanged(v => vm.ScaleZ = v, vm.ScaleZ, snap.Scale.Z);

            // Material
            if (snap.Material.HasValue)
            {
                var m = snap.Material.Value;
                vm.Material ??= new MaterialInfo();
                vm.Material.AttachToParent(vm);
                var newColor = FromVector3(m.DiffuseColor);
                if (vm.Material.Color != newColor) vm.Material.Color = newColor;
                SetIfChanged(v => vm.Material.Roughness = v, vm.Material.Roughness, m.Roughness);
                SetIfChanged(v => vm.Material.Metallic = v, vm.Material.Metallic, m.Metallic);
                SetIfChanged(v => vm.Material.EmissiveStrength = v, vm.Material.EmissiveStrength, m.EmissiveStrength);
                if (vm.Material.IsTransparent != m.IsTransparent) vm.Material.IsTransparent = m.IsTransparent;
                if (vm.Material.MaterialType != m.MaterialType) vm.Material.MaterialType = m.MaterialType;
                double newOpacity = m.IsTransparent ? 0.5 : 1.0;
                SetIfChanged(v => vm.Material.Opacity = v, vm.Material.Opacity, newOpacity);
            }
            else if (snap.Kind != EditorObjectKind.BasicEntity)
            {
                vm.Material = null;
            }

            // Light
            if (snap.Light.HasValue)
            {
                var l = snap.Light.Value;
                vm.Light ??= new LightInfo();
                vm.Light.AttachToParent(vm);
                var newType = l.IsDirectional ? LightType.Directional : LightType.Point;
                if (vm.Light.Type != newType) vm.Light.Type = newType;
                var newColor = FromXnaColor(l.Color);
                if (vm.Light.Color != newColor) vm.Light.Color = newColor;
                SetIfChanged(v => vm.Light.Intensity = v, vm.Light.Intensity, l.Intensity);
                SetIfChanged(v => vm.Light.Radius = v, vm.Light.Radius, l.Radius);
                if (vm.Light.CastShadows != l.CastShadows) vm.Light.CastShadows = l.CastShadows;
            }
            else
            {
                vm.Light = null;
            }

            // Camera info — only Camera kind shows the camera expander.
            if (snap.Kind == EditorObjectKind.Camera)
            {
                vm.Camera ??= new CameraInfo();
            }
            else
            {
                vm.Camera = null;
            }
        }
        finally
        {
            vm.EndSuppressPush();
        }
    }

    private static SceneObjectType MapKindToType(EditorObjectKind kind) => kind switch
    {
        EditorObjectKind.BasicEntity => SceneObjectType.Mesh,
        EditorObjectKind.PointLight => SceneObjectType.Light,
        EditorObjectKind.DirectionalLight => SceneObjectType.Light,
        EditorObjectKind.Camera => SceneObjectType.Camera,
        _ => SceneObjectType.Empty,
    };

    private static Color FromVector3(Microsoft.Xna.Framework.Vector3 v)
    {
        byte r = (byte)System.Math.Clamp((int)(v.X * 255f), 0, 255);
        byte g = (byte)System.Math.Clamp((int)(v.Y * 255f), 0, 255);
        byte b = (byte)System.Math.Clamp((int)(v.Z * 255f), 0, 255);
        return Color.FromRgb(r, g, b);
    }

    private static Color FromXnaColor(XnaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static XnaColor ToXnaColor(Color c) => new XnaColor(c.R, c.G, c.B, c.A);

    public static Microsoft.Xna.Framework.Vector3 ToVector3(Color c)
        => new Microsoft.Xna.Framework.Vector3(c.R / 255f, c.G / 255f, c.B / 255f);
}
