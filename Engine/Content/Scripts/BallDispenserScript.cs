using Engine.Components;
using Engine.Entities;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Interact (E) to drop a physics ball from the gameobject named "Ball Chute" (or 3 m above this
/// one). Needs an Interactable and a Physics component. Keeps at most <see cref="MaxBalls"/>
/// balls, removing the oldest; all of them go when Play stops.
/// </summary>
public sealed class BallDispenserScript : ScriptBehaviour
{
    public const string ScriptId = "ball-dispenser";
    public const string ChuteName = "Ball Chute";
    public const int MaxBalls = 8;

    private const float BallScale = 0.3f, BallMass = 12f;
    private static readonly Vector3[] Colors =
    {
        new(0.95f, 0.36f, 0.25f), new(0.25f, 0.61f, 1f), new(0.98f, 0.77f, 0.25f), new(0.62f, 0.4f, 1f),
    };

    private readonly Queue<BasicEntity> _balls = new();
    private int _dispensed;

    public override void Start()
    {
        _balls.Clear();
        _dispensed = 0;
    }

    public override void OnInteract(Interaction interaction)
    {
        BasicEntity chute = FindGameObject(ChuteName);
        Vector3 at = chute != null ? chute.Position - new Vector3(0f, 0f, 0.8f) : Position + new Vector3(0f, 0f, 3f);
        // A little jitter so a stack of balls doesn't balance perfectly.
        at += new Vector3(Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f, 0f) * 0.2f;

        BasicEntity ball = Spawn("IsoSphere", at, $"Ball {++_dispensed}");
        if (ball == null) return;
        ball.Scale = new Vector3(BallScale);
        ball.AddComponent(new PhysicsComponent { BodyType = PhysicsBodyType.Dynamic, Mass = BallMass });
        Vector3 color = Colors[(_dispensed - 1) % Colors.Length];
        ball.AddComponent(new MaterialComponent { Red = color.X, Green = color.Y, Blue = color.Z, Roughness = 0.3f });
        _balls.Enqueue(ball);
        if (_balls.Count > MaxBalls) Destroy(_balls.Dequeue());
        CollisionTestFeed.Post($"{GameObject.Name} dropped {ball.Name}");
    }

    public override void Update()
    {
        // Balls that rolled off the edge are gone for good.
        if (_balls.Count > 0 && _balls.Peek().Position.Z < -15f) Destroy(_balls.Dequeue());
    }
}
