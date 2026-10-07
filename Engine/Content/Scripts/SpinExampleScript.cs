using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>Double-click in Assets/Scripts to edit; attach via Add Component > Script Behaviour.</summary>
public sealed class SpinExampleScript : ScriptBehaviour
{
    public const string ScriptId = "spin-example";

    // Serialized fields show in the Inspector and are saved per attachment (see Script_Behaviours.md).
    [SerializeField, Range(-360f, 360f), Tooltip("Rotation speed around Z (the engine's up axis).")]
    private float _degreesPerSecond = 45f;

    private Matrix _initialRotation;
    private float _angle;

    public override void Start()
    {
        // Start runs once before this instance's first Update.
        _initialRotation = GameObject.RotationMatrix;
        _angle = 0f;
    }

    public override void Update()
    {
        // Rotate around Z (the engine's up axis) at Degrees Per Second.
        _angle = (_angle + MathHelper.ToRadians(_degreesPerSecond) * DeltaTime) % MathHelper.TwoPi;
        GameObject.RotationMatrix = _initialRotation * Matrix.CreateRotationZ(_angle);
    }
}
