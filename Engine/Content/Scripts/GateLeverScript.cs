using Engine.Components;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Interact (E) to open or close the gameobject named "Gate": it slides into the floor and back.
/// The gate is a Static collider moved by script, so it shoves whatever stands in its way.
/// Needs an Interactable and a Physics component on the lever; the prompt follows the gate's state.
/// </summary>
public sealed class GateLeverScript : ScriptBehaviour
{
    public const string ScriptId = "gate-lever";
    public const string GateName = "Gate";

    private const float Travel = 2.1f;       // metres the gate sinks
    private const float SecondsToOpen = 1.2f;
    private static readonly Vector3 OpenColor = new(0.38f, 0.79f, 0.41f), ClosedColor = new(0.91f, 0.36f, 0.25f);

    private BasicEntity _gate;
    private Vector3 _closedPosition;
    private bool _open;
    private float _amount;                   // 0 closed .. 1 open
    private Vector3 _idleColor;
    private string _idlePrompt;

    public override void Start()
    {
        _gate = FindGameObject(GateName);
        if (_gate == null) Log($"No gameobject named '{GateName}' to open.");
        _closedPosition = _gate?.Position ?? Vector3.Zero;
        _open = false;
        _amount = 0f;
        _idleColor = CollisionTestFeed.ColorOf(GameObject);
        _idlePrompt = GetComponent<InteractableComponent>()?.Prompt;
        SetPrompt("Open the gate");
    }

    public override void Stop()
    {
        // Transforms are rewound when Play stops, but component settings are not.
        CollisionTestFeed.Paint(GameObject, _idleColor);
        if (_idlePrompt != null) SetPrompt(_idlePrompt);
    }

    public override void OnInteract(Interaction interaction)
    {
        if (_gate == null) return;
        _open = !_open;
        CollisionTestFeed.Post(_open ? "Gate opening" : "Gate closing");
        CollisionTestFeed.Paint(GameObject, _open ? OpenColor : ClosedColor);
        SetPrompt(_open ? "Close the gate" : "Open the gate");
    }

    public override void Update()
    {
        if (_gate == null) return;
        float target = _open ? 1f : 0f;
        if (_amount == target) return;
        float step = DeltaTime / SecondsToOpen;
        _amount = _open ? Math.Min(target, _amount + step) : Math.Max(target, _amount - step);
        // Ease in and out; ScenePhysics moves the static collider with the gameobject.
        float eased = _amount * _amount * (3f - 2f * _amount);
        _gate.Position = _closedPosition - new Vector3(0f, 0f, Travel * eased);
    }

    private void SetPrompt(string prompt)
    {
        if (GetComponent<InteractableComponent>() is { } interactable) interactable.Prompt = prompt;
    }
}
