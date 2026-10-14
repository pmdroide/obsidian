using Engine.Logic;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

public abstract partial class ScriptBehaviour
{
    ////////////////////////////////////////////////////////////////////////////////
    //  INPUT ACTIONS
    //  Named actions from Anvil > Window > Input Actions (Content/System/InputActions.json),
    //  each bound to keys, mouse and gamepad controls, so scripts never name a key:
    //    if (WasActionPressed("Jump")) Jump();
    //    Vector2 move = GetVector("Move");          // W/A/S/D or the left stick, +Y forward
    //    _yaw -= GetVector("Look").X;               // mouse drag, arrows or the right stick, in degrees
    //  Names are case-insensitive; an unknown name logs once and never fires.
    //  Raw keys and buttons stay available through GameInput.
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>The action itself: state, prompts (<c>BindingText()</c>), input buffering and rebinding.</summary>
    public InputAction GetAction(string name) => InputActions.Get(name);

    /// <summary>The action is held this frame (a stick or trigger pushed at least halfway).</summary>
    public bool IsActionDown(string name) => InputActions.Get(name).IsPressed;

    /// <summary>The action went down this frame.</summary>
    public bool WasActionPressed(string name) => InputActions.Get(name).WasPressed;

    /// <summary>The action came up this frame.</summary>
    public bool WasActionReleased(string name) => InputActions.Get(name).WasReleased;

    /// <summary>An Axis action's value, e.g. -1..1 from Q/E or a stick axis (a Button gives 0..1).</summary>
    public float GetAxis(string name) => InputActions.Get(name).Value;

    /// <summary>A Vector2 action's value, e.g. movement (length up to 1) or look deltas.</summary>
    public Vector2 GetVector(string name) => InputActions.Get(name).Vector;

    /// <summary>The binding to show for an action on the device the player is using: "E" or "X", "W/A/S/D".</summary>
    public string BindingText(string actionName) => InputActions.Get(actionName).BindingText();
}
