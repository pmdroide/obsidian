using Engine.Components;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Shows what an NPC (NPC component on the same gameobject) is doing: tints it by state, posts its sightings,
/// attacks and give-ups to the NpcTest feed, and (with an Alarm Target) sends another NPC to search where this
/// one spotted the player, like a sentry calling a guard. Uses the NPC hooks OnTargetSpotted, OnTargetLost,
/// OnNpcStateChanged and OnNpcAttack.
/// </summary>
public sealed class NpcReporterScript : ScriptBehaviour
{
    public const string ScriptId = "npc-reporter";

    [Tooltip("Another NPC sent to search where this one spots its target (a sentry raising the alarm).")]
    public BasicEntity AlarmTarget;
    [Tooltip("Tint the gameobject by what the NPC is doing (yellow alerted, red chasing, violet searching...).")]
    public bool TintByState = true;

    private Vector3 _color;

    public override void Start()
    {
        _color = ColorOf(GameObject);
        Paint(NpcState.Idle);
    }

    public override void Stop() => SetColor(GameObject, _color);

    public override void OnTargetSpotted(BasicEntity target)
    {
        NpcTestFeed.Post($"{GameObject.Name} spotted {target?.Name ?? "something"}");
        NpcComponent alarm = AlarmTarget?.GetComponent<NpcComponent>();
        if (alarm == null || target == null || alarm.IsAware) return;
        alarm.Investigate(target.Position);
        NpcTestFeed.Post($"{GameObject.Name} raised the alarm: {AlarmTarget.Name} is on the way");
    }

    public override void OnTargetLost(BasicEntity target) =>
        NpcTestFeed.Post($"{GameObject.Name} lost track of {target?.Name ?? "its target"}");

    public override void OnNpcAttack(BasicEntity target) => NpcTestFeed.Post($"{GameObject.Name} hits {target.Name}!");

    public override void OnNpcStateChanged(NpcState previous, NpcState current)
    {
        Paint(current);
        string verb = current switch
        {
            NpcState.Chasing when previous == NpcState.Alerted => "gives chase",
            NpcState.Fleeing => "runs away",
            NpcState.Following => "follows you",
            NpcState.Watching => "keeps an eye on you",
            NpcState.Searching => "searches the area",
            _ => null,
        };
        if (verb != null) NpcTestFeed.Post($"{GameObject.Name} {verb}");
    }

    private void Paint(NpcState state)
    {
        if (!TintByState) return;
        Vector3 tint = state switch
        {
            NpcState.Alerted => new Vector3(1f, 0.85f, 0.2f),
            NpcState.Chasing or NpcState.Attacking => new Vector3(1f, 0.18f, 0.12f),
            NpcState.Fleeing => new Vector3(0.35f, 0.75f, 1f),
            NpcState.Following => new Vector3(0.45f, 1f, 0.45f),
            NpcState.Watching => new Vector3(1f, 0.55f, 0.15f),
            NpcState.Searching => new Vector3(0.72f, 0.45f, 1f),
            _ => _color,
        };
        SetColor(GameObject, tint);
    }

    private static Vector3 ColorOf(BasicEntity entity) =>
        entity?.GetComponent<MaterialComponent>() is { } material ? new Vector3(material.Red, material.Green, material.Blue) : Vector3.One;

    /// <summary>Changes the Material component's base colour; rebuilds the material, so only on a change.</summary>
    private static void SetColor(BasicEntity entity, Vector3 color)
    {
        if (entity?.GetComponent<MaterialComponent>() is not { } material) return;
        if (material.Red == color.X && material.Green == color.Y && material.Blue == color.Z) return;
        material.Red = color.X;
        material.Green = color.Y;
        material.Blue = color.Z;
        material.OnChanged(entity);
    }
}
