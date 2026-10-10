using Engine.Components;
using Engine.Entities;

namespace Engine.Scripting;

/// <summary>
/// Posts to the CollisionTest feed when something starts touching this gameobject's cloth (Cloth component):
/// the player pushing through the curtain, a thrown crate, the sheet landing on the table.
/// </summary>
public sealed class ClothReporterScript : ScriptBehaviour
{
    public const string ScriptId = "cloth-reporter";

    // A gameobject has to be clear of the cloth this long before touching it again is news.
    private const float ForgetSeconds = 0.75f;

    [Tooltip("Gameobject never reported (the floor everything ends up on).")]
    public string Ignore = "Ground";

    private readonly Dictionary<BasicEntity, float> _lastTouched = new();
    private ClothComponent _cloth;
    private float _time;

    public override void Start()
    {
        _cloth = GetComponent<ClothComponent>();
        _lastTouched.Clear();
        _time = 0;
    }

    public override void Update()
    {
        _time += DeltaTime;
        if (_cloth?.Cloth == null) return;
        foreach (BasicEntity other in _cloth.Touching)
        {
            bool touching = _lastTouched.TryGetValue(other, out float last) && _time - last < ForgetSeconds;
            _lastTouched[other] = _time;
            if (touching || other.Name == Ignore) continue;
            CollisionTestFeed.Post(_cloth.Cloth.PinnedCount == 0
                ? $"{GameObject.Name} landed on {other.Name}"
                : $"{other.Name} pushed into the {GameObject.Name}");
        }
    }
}
