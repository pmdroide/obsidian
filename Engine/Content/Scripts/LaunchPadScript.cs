using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Throws every Dynamic body that enters this trigger up and along the pad's Forward (+Y).
/// Put it on a gameobject whose Physics component is Static with Is Trigger ticked. It flashes
/// when it fires (visible with the Emissive material type).
/// </summary>
public sealed class LaunchPadScript : ScriptBehaviour
{
    public const string ScriptId = "launch-pad";

    [SerializeField, Range(0f, 30f), Tooltip("Vertical launch speed in m/s.")]
    private float _upSpeed = 9f;
    [SerializeField, Range(0f, 30f), Tooltip("Speed added along the pad's Forward, in m/s.")]
    private float _forwardSpeed = 4f;
    private const float FlashSeconds = 0.25f;

    private float _idleGlow;
    private float _flash;

    public override void Start()
    {
        _idleGlow = CollisionTestFeed.GlowOf(GameObject);
        _flash = 0f;
    }

    public override void Stop() => CollisionTestFeed.Glow(GameObject, _idleGlow);

    public override void OnTriggerEnter(BasicEntity other)
    {
        // Static colliders have no body to launch.
        if (other.DynamicBody == null) return;
        Vector3 forward = Forward;
        forward.Z = 0f;
        if (forward.LengthSquared() > 1e-6f) forward.Normalize();
        Vector3 velocity = GetVelocity(other);
        velocity.Z = _upSpeed;
        velocity += forward * _forwardSpeed;
        SetVelocity(other, velocity);
        CollisionTestFeed.Post($"{GameObject.Name} launched {other.Name}");
        CollisionTestFeed.Glow(GameObject, 3f);
        _flash = FlashSeconds;
    }

    public override void Update()
    {
        if (_flash <= 0f) return;
        _flash -= DeltaTime;
        if (_flash <= 0f) CollisionTestFeed.Glow(GameObject, _idleGlow);
    }
}
