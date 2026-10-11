using System.Text.Json.Serialization;
using Engine.Editor;
using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Components;

/// <summary>One lower-detail model of an <see cref="LodComponent"/> and the camera distance it starts at.</summary>
public sealed class LodLevel
{
    /// <summary>Model key, like the gameobject's own model ("StanfordDragonLowpoly"); empty or unknown skips the level.</summary>
    public string Model { get; set; } = "";
    /// <summary>Camera distance (metres, to the centre of the model's bounds) from which this model draws.</summary>
    public float Distance { get; set; } = 20f;
}

/// <summary>
/// Level of detail: draws simpler models as the camera moves away from the gameobject, and optionally
/// nothing beyond <see cref="CullDistance"/>. Level 0 is the gameobject's own model; each
/// <see cref="Levels"/> entry swaps in another model from its distance on. It works in the editor and
/// in Play, and changes only what is drawn (shadows and picking included): physics, SDFs and bounds
/// keep using the gameobject's own model. LOD models draw unposed, so give animated gameobjects only a
/// cull distance.
/// </summary>
public sealed class LodComponent : GameComponent
{
    public const string TypeId = "lod";
    /// <summary>
    /// Going back to a finer level (or reappearing) needs the camera this fraction closer than the switch
    /// distance, so a camera resting on a boundary doesn't flicker between two models.
    /// </summary>
    public const float Hysteresis = 0.05f;

    /// <summary>Lower-detail models, in any order (they are sorted by distance).</summary>
    public List<LodLevel> Levels { get; set; } = new() { new LodLevel() };
    /// <summary>Draws nothing from this camera distance on; 0 never culls.</summary>
    public float CullDistance { get; set; }

    /// <summary>Level drawing now: 0 the model, 1..n a resolved level (by distance), -1 culled.</summary>
    [JsonIgnore] public int CurrentLevel => _owner?.WorldTransform.LodLevel ?? 0;

    public float ClampedCullDistance => float.IsFinite(CullDistance) ? Math.Max(CullDistance, 0) : 0;

    /// <summary>Looks up model keys; set by <see cref="Logic.MainSceneLogic"/> (<see cref="Assets.FindModel"/>).</summary>
    internal static Func<string, ModelDefinition> FindModel;

    private BasicEntity _owner;

    public override void OnAdded(BasicEntity owner)
    {
        _owner = owner;
        base.OnAdded(owner);
    }

    // The library draws the levels: register them again with the new settings.
    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        owner?.RefreshMaterials();
    }

    public override void OnRemoved(BasicEntity owner)
    {
        base.OnRemoved(owner);
        owner?.RefreshMaterials();
        _owner = null;
    }

    /// <summary>The levels whose model loaded, nearest first.</summary>
    public IReadOnlyList<(Model Model, float Distance)> ResolveLevels(BasicEntity owner)
    {
        var resolved = new List<(Model Model, float Distance)>();
        foreach (LodLevel level in Levels ?? new List<LodLevel>())
        {
            if (level == null || string.IsNullOrWhiteSpace(level.Model)) continue;
            if (!float.IsFinite(level.Distance) || level.Distance <= 0)
            {
                EditorBridge.Log($"LOD: '{owner?.Name}' level '{level.Model}' needs a distance above 0.");
                continue;
            }
            Model model = FindModel?.Invoke(level.Model.Trim())?.Model;
            if (model == null)
            {
                EditorBridge.Log($"LOD: '{owner?.Name}' has no model '{level.Model}'.");
                continue;
            }
            resolved.Add((model, level.Distance));
        }
        resolved.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return resolved;
    }

    /// <summary>
    /// The level to draw at <paramref name="distance"/> from the camera, given the switch distances of
    /// levels 1..n (ascending), the cull distance (0 = none) and the level drawing now (for hysteresis).
    /// </summary>
    public static int SelectLevel(float distance, IReadOnlyList<float> distances, float cullDistance, int current)
    {
        if (cullDistance > 0 && distance >= (current == -1 ? cullDistance * (1 - Hysteresis) : cullDistance))
            return -1;
        int level = 0;
        for (int i = 0; i < distances.Count; i++)
            if (distance >= (current > i ? distances[i] * (1 - Hysteresis) : distances[i]))
                level = i + 1;
        return level;
    }
}
