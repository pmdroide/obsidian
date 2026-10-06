using Engine.Logic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Scripting;

/// <summary>
/// Fly the camera in Play mode. Select Main Camera in the Hierarchy, then
/// Add Component > Script Behaviour and pick Freecam.
///   Right mouse (hold) - look around      W / A / S / D - move
///   E / Q              - up / down        Shift         - move faster
///   Scroll while holding right mouse      - change speed
/// </summary>
public sealed class FreecamScript : ScriptBehaviour
{
    public const string ScriptId = "freecam";

    private const float LookDegreesPerPixel = 0.15f;
    private const float MinSpeed = 0.5f, MaxSpeed = 200f;
    private const float FastMultiplier = 4f;
    // How quickly movement reaches full speed and stops; higher is snappier.
    private const float Smoothing = 12f;

    private float _speed;     // metres per second
    private float _yaw;       // degrees around Z, 0 = looking along +X
    private float _pitch;     // degrees above the horizon
    private Vector3 _velocity;

    public override void Start()
    {
        // Continue from wherever the camera is looking, so Play starts from the saved view.
        Vector3 forward = Forward;
        _yaw = MathHelper.ToDegrees(MathF.Atan2(forward.Y, forward.X));
        _pitch = Math.Clamp(MathHelper.ToDegrees(MathF.Asin(Math.Clamp(forward.Z, -1f, 1f))), -89f, 89f);
        _speed = 8f;
        _velocity = Vector3.Zero;
        ApplyLook();
    }

    public override void Update()
    {
        if (DebugScreen.ConsoleOpen) return;

        MouseState mouse = Input.mouseState, last = Input.mouseLastState;
        // Both frames held, so pressing the button doesn't jump the view.
        if (mouse.RightButton == ButtonState.Pressed && last.RightButton == ButtonState.Pressed)
        {
            _yaw -= (mouse.X - last.X) * LookDegreesPerPixel;
            _pitch = Math.Clamp(_pitch - (mouse.Y - last.Y) * LookDegreesPerPixel, -89f, 89f);
            ApplyLook();

            // Only while looking, so scrolling an editor panel doesn't change the speed.
            float notches = (mouse.ScrollWheelValue - last.ScrollWheelValue) / 120f;
            if (notches != 0) _speed = Math.Clamp(_speed * MathF.Pow(1.2f, notches), MinSpeed, MaxSpeed);
        }

        Vector3 move = Vector3.Zero;
        if (Input.IsKeyDown(Keys.W)) move += Forward;
        if (Input.IsKeyDown(Keys.S)) move -= Forward;
        if (Input.IsKeyDown(Keys.D)) move += Right;
        if (Input.IsKeyDown(Keys.A)) move -= Right;
        if (Input.IsKeyDown(Keys.E)) move += Vector3.UnitZ;
        if (Input.IsKeyDown(Keys.Q)) move -= Vector3.UnitZ;
        if (move != Vector3.Zero) move.Normalize();

        bool fast = Input.IsKeyDown(Keys.LeftShift) || Input.IsKeyDown(Keys.RightShift);
        Vector3 target = move * _speed * (fast ? FastMultiplier : 1f);
        // Ease towards the target velocity; the exponent keeps it frame-rate independent.
        _velocity = Vector3.Lerp(_velocity, target, 1f - MathF.Exp(-Smoothing * DeltaTime));
        if (_velocity.LengthSquared() > 1e-6f) Position += _velocity * DeltaTime;
    }

    private void ApplyLook()
    {
        float yaw = MathHelper.ToRadians(_yaw), pitch = MathHelper.ToRadians(_pitch);
        var direction = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
        LookAt(Position + direction);
    }
}
