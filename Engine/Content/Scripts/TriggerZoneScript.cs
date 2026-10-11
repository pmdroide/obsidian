using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Counts the gameobjects inside this trigger and lights up while any are inside. Put it on a
/// gameobject whose Physics component is Static with Is Trigger ticked (CollisionTest: the Goal Zone,
/// a thin pad that crates standing on it overlap). The glow shows with the Emissive material type.
/// </summary>
public sealed class TriggerZoneScript : ScriptBehaviour
{
    public const string ScriptId = "trigger-zone";

    private static readonly Vector3 OccupiedColor = new(0.35f, 1f, 0.45f);

    private readonly List<BasicEntity> _inside = new();
    private Vector3 _idleColor;
    private float _idleGlow;

    public override void Start()
    {
        _inside.Clear();
        _idleColor = CollisionTestFeed.ColorOf(GameObject);
        _idleGlow = CollisionTestFeed.GlowOf(GameObject);
    }

    // Material changes are not rewound when Play stops, so put the idle look back.
    public override void Stop() => CollisionTestFeed.Paint(GameObject, _idleColor, _idleGlow);

    public override void OnTriggerEnter(BasicEntity other)
    {
        if (_inside.Contains(other)) return;
        _inside.Add(other);
        CollisionTestFeed.Post($"{GameObject.Name}: {other.Name} entered ({_inside.Count} inside)");
        Refresh();
    }

    public override void OnTriggerExit(BasicEntity other)
    {
        if (!_inside.Remove(other)) return;
        CollisionTestFeed.Post($"{GameObject.Name}: {other.Name} left ({_inside.Count} inside)");
        Refresh();
    }

    private void Refresh()
    {
        if (_inside.Count > 0) CollisionTestFeed.Paint(GameObject, OccupiedColor, Math.Max(_idleGlow * 2.5f, 1.5f));
        else CollisionTestFeed.Paint(GameObject, _idleColor, _idleGlow);
    }
}
