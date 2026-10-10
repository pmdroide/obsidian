using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Scripting;

/// <summary>
/// The AnimationTest sample's ragdoll controls. Runs on the Main Camera next to Freecam.
///   Left click   throw a ball where the mouse points (it knocks over characters with an Impact threshold)
///   G            every ragdoll goes limp
///   R            every ragdoll stands back up, and the thrown balls go
/// </summary>
public sealed class RagdollTestScript : ScriptBehaviour
{
    public const string ScriptId = "ragdoll-test";
    public const int MaxBalls = 10;

    private const float BallScale = 0.15f;

    [Range(1, 60)]
    [Tooltip("Speed thrown balls leave the camera at, in m/s.")]
    public float BallSpeed = 16;
    [Range(0.5f, 50)]
    [Tooltip("Mass of each thrown ball, in kg.")]
    public float BallMass = 6;

    private readonly Queue<BasicEntity> _balls = new();
    // Thrown balls get their body on the next physics update; their launch velocity waits for it.
    private readonly List<(BasicEntity Ball, Vector3 Velocity)> _launching = new();
    private int _thrown;

    public override void Start()
    {
        _balls.Clear();
        _launching.Clear();
        _thrown = 0;
        Log("Ragdoll Test: left click throws a ball, G drops every ragdoll, R stands them back up.");
    }

    public override void Update()
    {
        for (int i = _launching.Count - 1; i >= 0; i--)
        {
            var (ball, velocity) = _launching[i];
            if (ball.DynamicBody == null) continue;
            SetVelocity(ball, velocity);
            _launching.RemoveAt(i);
        }

        if (DebugScreen.ConsoleOpen) return;
        if (GameInput.MouseClicked) Throw(MouseRay);
        if (GameInput.WasPressed(Keys.G))
            foreach (RagdollComponent ragdoll in Ragdolls()) ragdoll.Activate();
        if (GameInput.WasPressed(Keys.R))
        {
            foreach (RagdollComponent ragdoll in Ragdolls()) ragdoll.Deactivate();
            _launching.Clear();
            while (_balls.Count > 0) Destroy(_balls.Dequeue());
        }
    }

    private void Throw(Ray ray)
    {
        BasicEntity ball = Spawn("IsoSphere", ray.Position + ray.Direction * 1.5f, $"Thrown Ball {++_thrown}");
        if (ball == null) return;
        ball.Scale = new Vector3(BallScale);
        ball.AddComponent(new PhysicsComponent { BodyType = PhysicsBodyType.Dynamic, Mass = BallMass });
        ball.AddComponent(new MaterialComponent { Red = 0.95f, Green = 0.45f, Blue = 0.2f, Roughness = 0.35f });
        _launching.Add((ball, ray.Direction * BallSpeed));
        _balls.Enqueue(ball);
        if (_balls.Count > MaxBalls) Destroy(_balls.Dequeue());
    }

    private static IEnumerable<RagdollComponent> Ragdolls()
    {
        List<BasicEntity> entities = GameFlow.SceneLogic?.BasicEntities;
        if (entities == null) yield break;
        foreach (BasicEntity entity in entities.ToArray())
            if (entity.IsEnabled && entity.GetComponent<RagdollComponent>() is { Enabled: true } ragdoll) yield return ragdoll;
    }
}
