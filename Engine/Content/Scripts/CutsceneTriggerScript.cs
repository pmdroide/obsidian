using Engine.Entities;
using Engine.Logic;

namespace Engine.Scripting;

/// <summary>
/// Plays a cutscene (Content/Cutscenes, made in Anvil > Window > Cutscenes) when Play starts, or when something
/// enters this gameobject's trigger (Physics > Is Trigger). Works on the Main Camera too (Play On Start only).
/// </summary>
public sealed class CutsceneTriggerScript : ScriptBehaviour
{
    public const string ScriptId = "cutscene-trigger";

    [Tooltip("Cutscene name in Content/Cutscenes (\"Intro\") or a Content path.")]
    public string Cutscene = "Intro";

    [Tooltip("Play as soon as Play starts.")]
    public bool PlayOnStart = true;

    [Tooltip("Play when a gameobject enters this trigger.")]
    public bool PlayOnTriggerEnter;

    [Tooltip("Only gameobjects with this tag start it (empty = any).")]
    public string RequiredTag = "Player";

    [Tooltip("Play it once per Play session.")]
    public bool Once = true;

    private bool _played;

    public override void Start()
    {
        _played = false;
        if (PlayOnStart) Trigger();
    }

    public override void OnTriggerEnter(BasicEntity other)
    {
        if (!PlayOnTriggerEnter) return;
        if (!string.IsNullOrWhiteSpace(RequiredTag) && other.Tag != RequiredTag) return;
        Trigger();
    }

    private void Trigger()
    {
        if (Once && _played) return;
        if (Cutscenes.Play(Cutscene)) _played = true;
        else Log($"Cutscene '{Cutscene}' couldn't be played (missing from Content/Cutscenes?)");
    }
}
