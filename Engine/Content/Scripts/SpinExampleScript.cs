using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>Double-click in Assets/Scripts to edit; attach via Add Component > Script Behaviour.</summary>
public sealed class SpinExampleScript : ScriptBehaviour
{
    public const string ScriptId = "spin-example";
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
        // Rotate around Z (the engine's up axis) at 45 degrees per second.
        _angle = (_angle + MathHelper.ToRadians(45f) * DeltaTime) % MathHelper.TwoPi;
        GameObject.RotationMatrix = _initialRotation * Matrix.CreateRotationZ(_angle);
    }
}
