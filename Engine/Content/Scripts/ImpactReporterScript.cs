using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Reports every hit on this gameobject with its impact speed, flashes towards white (more for
/// harder hits) and plays a click at the contact point. Needs a Physics component and a Material
/// component (CollisionTest: the Impact Plate).
/// </summary>
public sealed class ImpactReporterScript : ScriptBehaviour
{
    public const string ScriptId = "impact-reporter";

    // Slower touches (something settling against it) aren't worth reporting.
    private const float MinImpactSpeed = 0.75f;
    private const float FlashSeconds = 0.3f;

    private Vector3 _idleColor;
    private float _flash;
    private int _hits;

    public override void Start()
    {
        _idleColor = CollisionTestFeed.ColorOf(GameObject);
        _flash = 0f;
        _hits = 0;
    }

    // Material changes are not rewound when Play stops.
    public override void Stop() => CollisionTestFeed.Paint(GameObject, _idleColor);

    public override void OnCollisionEnter(Collision collision)
    {
        if (collision.ImpactSpeed < MinImpactSpeed) return;
        _hits++;
        CollisionTestFeed.Post($"{GameObject.Name}: hit #{_hits} by {collision.GameObject.Name} at {collision.ImpactSpeed:0.0} m/s");
        float strength = Math.Clamp(collision.ImpactSpeed / 8f, 0.3f, 0.9f);
        CollisionTestFeed.Paint(GameObject, Vector3.Lerp(_idleColor, Vector3.One, strength));
        _flash = FlashSeconds;
        PlaySoundAt("Audio/blip.wav", collision.Point, Math.Clamp(collision.ImpactSpeed / 10f, 0.15f, 1f));
    }

    public override void Update()
    {
        if (_flash <= 0f) return;
        _flash -= DeltaTime;
        if (_flash <= 0f) CollisionTestFeed.Paint(GameObject, _idleColor);
    }
}
