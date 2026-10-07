using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Shared by the CollisionTest sample scripts: the event feed the player's HUD shows (also written
/// to the engine/editor log) and colour helpers on the Material component. Colour changes rebuild
/// the gameobject's material instances, so make them when something happens, not every frame.
/// </summary>
public static class CollisionTestFeed
{
    public const int MaxLines = 7;
    private const float HighlightAmount = 0.4f;

    private static readonly List<string> Lines = new();
    private static BasicEntity _highlighted;
    private static Vector3 _highlightBase;

    /// <summary>Newest last.</summary>
    public static IReadOnlyList<string> Recent => Lines;
    /// <summary>Changes with every post, so the HUD only rebuilds its text when needed.</summary>
    public static int Version { get; private set; }

    public static void Post(string message)
    {
        Lines.Add(message);
        if (Lines.Count > MaxLines) Lines.RemoveAt(0);
        Version++;
        EditorBridge.Log("[Collision Test] " + message);
    }

    public static void Clear()
    {
        Lines.Clear();
        Version++;
        _highlighted = null;
    }

    /// <summary>
    /// Sets the base colour (0..1) and, when given, the glow (seen only on the Emissive material type).
    /// Works on the highlighted gameobject too: it stays brightened in the new colour.
    /// </summary>
    public static void Paint(BasicEntity entity, Vector3 color, float? emissive = null)
    {
        if (entity != null && entity == _highlighted)
        {
            _highlightBase = color;
            color = Brighten(color);
        }
        Apply(entity, color, emissive);
    }

    /// <summary>Changes only the glow (Emissive material type).</summary>
    public static void Glow(BasicEntity entity, float emissive) =>
        Apply(entity, MaterialColor(entity), emissive);

    /// <summary>Brightens one gameobject (what the player aims at) and restores the previous one.</summary>
    public static void Highlight(BasicEntity entity)
    {
        if (entity == _highlighted) return;
        if (_highlighted != null) Apply(_highlighted, _highlightBase, null);
        _highlighted = entity?.GetComponent<MaterialComponent>() != null ? entity : null;
        if (_highlighted == null) return;
        _highlightBase = MaterialColor(entity);
        Apply(entity, Brighten(_highlightBase), null);
    }

    /// <summary>The gameobject's own colour (without the highlight).</summary>
    public static Vector3 ColorOf(BasicEntity entity) => entity != null && entity == _highlighted ? _highlightBase : MaterialColor(entity);

    public static float GlowOf(BasicEntity entity) => entity?.GetComponent<MaterialComponent>()?.EmissiveStrength ?? 0f;

    private static Vector3 Brighten(Vector3 color) => Vector3.Lerp(color, Vector3.One, HighlightAmount);

    private static Vector3 MaterialColor(BasicEntity entity) =>
        entity?.GetComponent<MaterialComponent>() is { } material ? new Vector3(material.Red, material.Green, material.Blue) : Vector3.One;

    private static void Apply(BasicEntity entity, Vector3 color, float? emissive)
    {
        if (entity?.GetComponent<MaterialComponent>() is not { } material) return;
        float glow = emissive ?? material.EmissiveStrength;
        if (material.Red == color.X && material.Green == color.Y && material.Blue == color.Z && material.EmissiveStrength == glow) return;
        material.Red = color.X;
        material.Green = color.Y;
        material.Blue = color.Z;
        material.EmissiveStrength = glow;
        material.OnChanged(entity);
    }
}
