using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

public partial class NpcComponentViewModel : ComponentViewModel
{
    public static NpcIdleBehaviour[] IdleBehaviours { get; } = Enum.GetValues<NpcIdleBehaviour>();
    public static NpcReaction[] Reactions { get; } = Enum.GetValues<NpcReaction>();

    // Movement
    [ObservableProperty] private double _walkSpeed = 2.5;
    [ObservableProperty] private double _runSpeed = 5.5;
    [ObservableProperty] private double _turnSpeed = 360;
    [ObservableProperty] private double _acceleration = 20;
    [ObservableProperty] private double _radius;

    // Idle
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWander), nameof(IsPatrol))]
    private NpcIdleBehaviour _idle = NpcIdleBehaviour.Wander;
    [ObservableProperty] private double _wanderRadius = 8;
    [ObservableProperty] private double _waitTime = 2;
    [ObservableProperty] private string _patrolPoints = "";

    // Perception
    [ObservableProperty] private string _target = "Player";
    [ObservableProperty] private double _sightRange = 15;
    [ObservableProperty] private double _fieldOfView = 110;
    [ObservableProperty] private double _hearingRange = 2.5;
    [ObservableProperty] private double _eyeHeight = 0.7;
    [ObservableProperty] private double _reactionTime = 0.5;
    [ObservableProperty] private double _memoryTime = 4;

    // Reaction
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChase), nameof(IsFollow), nameof(IsFlee), nameof(SearchesAfterLosing))]
    private NpcReaction _reaction = NpcReaction.Chase;
    [ObservableProperty] private double _attackRange = 1.6;
    [ObservableProperty] private double _attackCooldown = 1.2;
    [ObservableProperty] private double _attackForce = 350;
    [ObservableProperty] private double _followDistance = 2.5;
    [ObservableProperty] private double _fleeDistance = 10;
    [ObservableProperty] private double _searchTime = 4;
    [ObservableProperty] private bool _showDebug;

    public bool IsWander => Idle == NpcIdleBehaviour.Wander;
    public bool IsPatrol => Idle == NpcIdleBehaviour.Patrol;
    public bool IsChase => Reaction == NpcReaction.Chase;
    public bool IsFollow => Reaction == NpcReaction.Follow;
    public bool IsFlee => Reaction == NpcReaction.Flee;
    public bool SearchesAfterLosing => Reaction is NpcReaction.Chase or NpcReaction.Follow;

    public NpcComponentViewModel(SceneObjectViewModel owner) : base(owner, NpcComponent.TypeId) { }

    /// <summary>Clamps a numeric field and pushes it; NaN (an emptied box) is ignored.</summary>
    private void PushNumber(double value, double min, double max, Action<NpcComponent, float> set)
    {
        if (!double.IsFinite(value)) return;
        float clamped = (float)Math.Clamp(value, min, max);
        Push(c => set((NpcComponent)c, clamped));
    }

    partial void OnWalkSpeedChanged(double value) => PushNumber(value, 0, 50, (n, v) => n.WalkSpeed = v);
    partial void OnRunSpeedChanged(double value) => PushNumber(value, 0, 50, (n, v) => n.RunSpeed = v);
    partial void OnTurnSpeedChanged(double value) => PushNumber(value, 1, 3600, (n, v) => n.TurnSpeed = v);
    partial void OnAccelerationChanged(double value) => PushNumber(value, 0.1, 500, (n, v) => n.Acceleration = v);
    partial void OnRadiusChanged(double value) => PushNumber(value, 0, 3, (n, v) => n.Radius = v);

    partial void OnIdleChanged(NpcIdleBehaviour value)
    {
        if (!Enum.IsDefined(value)) return;
        Push(c => ((NpcComponent)c).Idle = value);
    }

    partial void OnWanderRadiusChanged(double value) => PushNumber(value, 0, 500, (n, v) => n.WanderRadius = v);
    partial void OnWaitTimeChanged(double value) => PushNumber(value, 0, 120, (n, v) => n.WaitTime = v);

    partial void OnPatrolPointsChanged(string value)
    {
        string points = value?.Trim() ?? "";
        Push(c => ((NpcComponent)c).PatrolPoints = points);
    }

    partial void OnTargetChanged(string value)
    {
        string target = value?.Trim() ?? "";
        Push(c => ((NpcComponent)c).Target = target);
    }

    partial void OnSightRangeChanged(double value) => PushNumber(value, 0, 500, (n, v) => n.SightRange = v);
    partial void OnFieldOfViewChanged(double value) => PushNumber(value, 0, 360, (n, v) => n.FieldOfView = v);
    partial void OnHearingRangeChanged(double value) => PushNumber(value, 0, 500, (n, v) => n.HearingRange = v);
    partial void OnEyeHeightChanged(double value) => PushNumber(value, -100, 100, (n, v) => n.EyeHeight = v);
    partial void OnReactionTimeChanged(double value) => PushNumber(value, 0, 10, (n, v) => n.ReactionTime = v);
    partial void OnMemoryTimeChanged(double value) => PushNumber(value, 0, 60, (n, v) => n.MemoryTime = v);

    partial void OnReactionChanged(NpcReaction value)
    {
        if (!Enum.IsDefined(value)) return;
        Push(c => ((NpcComponent)c).Reaction = value);
    }

    partial void OnAttackRangeChanged(double value) => PushNumber(value, 0, 50, (n, v) => n.AttackRange = v);
    partial void OnAttackCooldownChanged(double value) => PushNumber(value, 0.05, 30, (n, v) => n.AttackCooldown = v);
    partial void OnAttackForceChanged(double value) => PushNumber(value, 0, 100000, (n, v) => n.AttackForce = v);
    partial void OnFollowDistanceChanged(double value) => PushNumber(value, 0.5, 100, (n, v) => n.FollowDistance = v);
    partial void OnFleeDistanceChanged(double value) => PushNumber(value, 1, 200, (n, v) => n.FleeDistance = v);
    partial void OnSearchTimeChanged(double value) => PushNumber(value, 0, 60, (n, v) => n.SearchTime = v);
    partial void OnShowDebugChanged(bool value) => Push(c => ((NpcComponent)c).ShowDebug = value);

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var npc = (NpcComponent)component;
        static bool Differs(double shown, float value) => Math.Abs(shown - value) > 0.0001;

        if (Differs(WalkSpeed, npc.WalkSpeed)) WalkSpeed = npc.WalkSpeed;
        if (Differs(RunSpeed, npc.RunSpeed)) RunSpeed = npc.RunSpeed;
        if (Differs(TurnSpeed, npc.TurnSpeed)) TurnSpeed = npc.TurnSpeed;
        if (Differs(Acceleration, npc.Acceleration)) Acceleration = npc.Acceleration;
        if (Differs(Radius, npc.Radius)) Radius = npc.Radius;
        Idle = npc.Idle;
        if (Differs(WanderRadius, npc.WanderRadius)) WanderRadius = npc.WanderRadius;
        if (Differs(WaitTime, npc.WaitTime)) WaitTime = npc.WaitTime;
        PatrolPoints = npc.PatrolPoints ?? "";
        Target = npc.Target ?? "";
        if (Differs(SightRange, npc.SightRange)) SightRange = npc.SightRange;
        if (Differs(FieldOfView, npc.FieldOfView)) FieldOfView = npc.FieldOfView;
        if (Differs(HearingRange, npc.HearingRange)) HearingRange = npc.HearingRange;
        if (Differs(EyeHeight, npc.EyeHeight)) EyeHeight = npc.EyeHeight;
        if (Differs(ReactionTime, npc.ReactionTime)) ReactionTime = npc.ReactionTime;
        if (Differs(MemoryTime, npc.MemoryTime)) MemoryTime = npc.MemoryTime;
        Reaction = npc.Reaction;
        if (Differs(AttackRange, npc.AttackRange)) AttackRange = npc.AttackRange;
        if (Differs(AttackCooldown, npc.AttackCooldown)) AttackCooldown = npc.AttackCooldown;
        if (Differs(AttackForce, npc.AttackForce)) AttackForce = npc.AttackForce;
        if (Differs(FollowDistance, npc.FollowDistance)) FollowDistance = npc.FollowDistance;
        if (Differs(FleeDistance, npc.FleeDistance)) FleeDistance = npc.FleeDistance;
        if (Differs(SearchTime, npc.SearchTime)) SearchTime = npc.SearchTime;
        ShowDebug = npc.ShowDebug;
    }
}
