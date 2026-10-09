using Engine.Entities;
using Engine.Logic;

namespace Engine.Scripting;

/// <summary>
/// Loads another scene when the traveller walks in. Put it on a gameobject whose Physics component is
/// Static with Is Trigger ticked (PersistenceTest: the glowing Portal panels). The traveller should be a
/// persistent gameobject (Inspector > Persistent), so it arrives in the new scene still running.
/// </summary>
public sealed class ScenePortalScript : ScriptBehaviour
{
    public const string ScriptId = "scene-portal";

    [Tooltip("Scene to load: its name in the scene list (\"PersistenceTest2\") or a Content path (\"Scenes/X.obsc\").")]
    public string TargetScene = "";

    [Tooltip("Name of the gameobject that uses the portal; anything else passes through.")]
    public string Traveller = "Player";

    private bool _used;

    public override void Start() => _used = false;

    public override void OnTriggerEnter(BasicEntity other)
    {
        if (_used || other.Name != Traveller) return;
        if (!GameFlow.LoadScene(TargetScene))
        {
            PersistenceTestFeed.Post($"{GameObject.Name}: scene '{TargetScene}' not found");
            return;
        }
        // The load happens next frame; this scene (and this script) stop then.
        _used = true;
        PersistenceTestFeed.Post($"{other.Name} stepped through {GameObject.Name} to {TargetScene}");
    }
}
