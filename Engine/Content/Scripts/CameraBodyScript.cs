using Engine.Components;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Gives the camera a body in Play mode: a character gameobject (Body) stands under the camera, turned the way it
/// looks, so mirrors and glass with a Planar Reflection component show the player. Put it on Main Camera after the
/// script that moves the camera (Freecam). The Body's Animator plays Idle, Walk or Run with the camera's speed
/// when it has those actions. The Mixamo bots face their local -Y.
/// </summary>
public sealed class CameraBodyScript : ScriptBehaviour
{
    public const string ScriptId = "camera-body";

    [Tooltip("The character that follows the camera.")]
    public BasicEntity Body;
    [Range(0.5f, 3)] [Tooltip("Camera height above the body's feet, in metres.")]
    public float EyeHeight = 1.62f;
    [Range(0, 1)] [Tooltip("How far the body stands behind the camera, so the camera sits at the face.")]
    public float BackOffset = 0.12f;
    [Tooltip("Animator actions played by speed (empty = don't animate).")]
    public string IdleAction = "Idle";
    public string WalkAction = "Walk";
    public string RunAction = "Run";
    [Range(0.1f, 20)] [Tooltip("Speed in metres per second from which Run plays.")]
    public float RunFrom = 3f;

    private Vector3 _lastCamera;
    private float _speed;

    public override void Start()
    {
        if (Body == null)
        {
            Log("Camera Body: pick the Body gameobject in the Inspector.");
            return;
        }
        _lastCamera = Position;
        _speed = 0;
        Follow();
    }

    public override void Update()
    {
        if (Body == null) return;
        Vector3 camera = Position;
        Vector3 moved = camera - _lastCamera;
        moved.Z = 0;
        _lastCamera = camera;
        if (DeltaTime > 0) _speed = MathHelper.Lerp(_speed, moved.Length() / DeltaTime, 1f - MathF.Exp(-10f * DeltaTime));
        Follow();
        Animate();
    }

    private void Follow()
    {
        Vector3 forward = Forward;
        var flat = new Vector2(forward.X, forward.Y);
        if (flat.LengthSquared() < 1e-6f) flat = new Vector2(-Body.RotationMatrix.Up.X, -Body.RotationMatrix.Up.Y);
        if (flat.LengthSquared() < 1e-6f) flat = Vector2.UnitY;
        flat.Normalize();

        // Local -Y turned onto the view direction: rotating -Y by yaw gives (sin yaw, -cos yaw)
        float yaw = MathF.Atan2(flat.X, -flat.Y);
        Body.RotationMatrix = Matrix.CreateRotationZ(yaw);
        Body.Position = Position - new Vector3(flat * BackOffset, EyeHeight);
    }

    private void Animate()
    {
        AnimatorComponent animator = Body.GetComponent<AnimatorComponent>();
        if (animator == null) return;
        string action = _speed < 0.3f ? IdleAction : _speed < RunFrom ? WalkAction : RunAction;
        if (!string.IsNullOrEmpty(action)) animator.Play(action);
    }
}
