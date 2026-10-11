using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// The glowing orb of the PersistenceTest sample. It isn't marked Persistent in the Inspector: it bobs in
/// place until the leader walks up to it, then calls <see cref="ScriptBehaviour.DontDestroyOnLoad()"/> and
/// follows the leader, through portals into other scenes. Leave it and it stays behind with its scene.
/// No Physics component, so it never gets in the way.
/// </summary>
public sealed class CompanionOrbScript : ScriptBehaviour
{
    public const string ScriptId = "companion-orb";
    public const string OrbName = "Companion Orb";

    [Tooltip("Name of the gameobject to follow.")]
    public string Leader = "Player";
    [Range(0.5f, 6)]
    [Tooltip("How close the leader must come for the orb to join it.")]
    public float JoinDistance = 2.2f;
    [Range(0.5f, 6)] public float FollowDistance = 1.6f;
    [Range(0.5f, 12)] public float FollowSharpness = 4f;

    /// <summary>True once it joined the leader (it then survives scene loads for this Play session).</summary>
    public bool IsFollowing { get; private set; }

    private const float BobHeight = 0.15f, BobSpeed = 2f, TeleportDistance = 12f;
    private Vector3 _home;
    private float _time;

    public override void Start()
    {
        _home = Position;
        _time = 0;
        IsFollowing = IsPersistent;
    }

    public override void Update()
    {
        _time += DeltaTime;
        var bob = new Vector3(0f, 0f, MathF.Sin(_time * BobSpeed) * BobHeight);
        BasicEntity leader = FindGameObject(Leader);

        if (!IsFollowing)
        {
            Position = _home + bob;
            if (leader == null || Vector3.Distance(leader.Position, _home) > JoinDistance) return;
            IsFollowing = true;
            DontDestroyOnLoad();
            PersistenceTestFeed.Post($"{GameObject.Name} joined {leader.Name}: it will follow you through portals");
            return;
        }
        if (leader == null) return;

        // Behind the leader's right shoulder, as seen by the camera.
        Vector3 back = -(MainCamera?.Forward ?? Vector3.UnitY);
        back.Z = 0;
        back = back.LengthSquared() > 1e-4f ? Vector3.Normalize(back) : -Vector3.UnitY;
        Vector3 right = Vector3.Cross(-back, Vector3.UnitZ);
        Vector3 target = leader.Position + back * FollowDistance + right * 0.8f + new Vector3(0f, 0f, 0.9f) + bob;
        // After a scene load the leader appears at the spawn: jump there instead of flying across the map.
        if (Vector3.Distance(Position, target) > TeleportDistance) Position = target;
        else Position = Vector3.Lerp(Position, target, 1f - MathF.Exp(-FollowSharpness * DeltaTime));
    }

    public override void OnSceneLoaded() =>
        PersistenceTestFeed.Post($"{GameObject.Name} came along to {Logic.GameFlow.ActiveSceneName}");
}
