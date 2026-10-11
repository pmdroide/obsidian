using System.Runtime.CompilerServices;
using Engine.Entities;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.AI;

/// <summary>
/// Shares one <see cref="NavGrid"/> per physics scene and agent size between every NPC (and script) that
/// asks. Once per physics frame it checks whether the static colliders changed (added, removed, moved,
/// resized: a gate sliding open, a scene load) and rebuilds the grid then, at most every
/// <see cref="RebuildFrames"/> frames so a static moved every frame doesn't rebuild it every frame.
/// Everything runs on the game thread.
/// </summary>
public static class Navigation
{
    /// <summary>Fewest physics frames between two rebuilds caused by moving statics.</summary>
    public const int RebuildFrames = 30;

    private sealed class Entry
    {
        public NavGrid Grid;
        public int Signature;
        public int CheckedFrame = int.MinValue;
        public int BuiltFrame = int.MinValue;
        public bool Built;
    }

    private static readonly ConditionalWeakTable<ScenePhysics, Dictionary<NavGridSettings, Entry>> Grids = new();

    /// <summary>
    /// The grid for these settings over the static colliders among <paramref name="entities"/> (the active scene's
    /// gameobjects), built on first use. Null while the scene has no static collider.
    /// </summary>
    public static NavGrid For(ScenePhysics physics, IReadOnlyList<BasicEntity> entities, NavGridSettings settings, Vector3 focus = default)
    {
        if (physics == null || entities == null) return null;
        settings = settings.Normalized();
        Dictionary<NavGridSettings, Entry> grids = Grids.GetOrCreateValue(physics);
        if (!grids.TryGetValue(settings, out Entry entry)) grids[settings] = entry = new Entry();

        if (entry.CheckedFrame == physics.Frame && entry.Built) return entry.Grid;
        entry.CheckedFrame = physics.Frame;
        int signature = Signature(entities);
        bool changed = !entry.Built || signature != entry.Signature;
        // Bodies attach on the first physics update: build again as soon as they appear.
        bool due = entry.Grid == null || physics.Frame - entry.BuiltFrame >= RebuildFrames || physics.Frame < entry.BuiltFrame;
        if (changed && due)
        {
            entry.Grid = NavGrid.Build(physics, entities, settings, focus);
            entry.Signature = signature;
            entry.BuiltFrame = physics.Frame;
            entry.Built = true;
        }
        return entry.Grid;
    }

    /// <summary>Drops every cached grid, so the next request rebuilds (e.g. after a script reshaped the level).</summary>
    public static void Invalidate()
    {
        Grids.Clear();
    }

    /// <summary>Changes whenever a static collider is added, removed, moved, turned or resized.</summary>
    private static int Signature(IReadOnlyList<BasicEntity> entities)
    {
        var hash = new HashCode();
        hash.Add(RuntimeHelpers.GetHashCode(entities));
        for (int i = 0; i < entities.Count; i++)
        {
            BasicEntity e = entities[i];
            if (!NavGrid.IsObstacle(e)) continue;
            hash.Add(e.Id);
            hash.Add(e.Position);
            hash.Add(e.RotationMatrix);
            hash.Add(e.Scale);
            hash.Add(e.Physics.ColliderStretchFor(e));
        }
        return hash.ToHashCode();
    }
}
