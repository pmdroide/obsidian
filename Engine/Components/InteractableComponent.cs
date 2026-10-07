using Engine.Entities;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>What a script's <c>OnInteract</c> hook (and <see cref="InteractableComponent.Interacted"/>) receives.</summary>
public struct Interaction
{
    /// <summary>The gameobject whose script interacted; null when it runs on the main camera.</summary>
    public BasicEntity Interactor;
    /// <summary>Where the interaction ray hit the interactable (Point/Normal are zero for a direct call).</summary>
    public RaycastHit Hit;
}

/// <summary>
/// Marks a gameobject as something the player can use. A script finds it with a ray
/// (<c>ScriptBehaviour.FindInteractable</c>) and uses it (<c>ScriptBehaviour.Interact</c>), which runs
/// <c>OnInteract</c> on the gameobject's own scripts. Rays only hit colliders, so the gameobject
/// also needs a Physics component (a non-trigger one).
/// </summary>
public sealed class InteractableComponent : GameComponent
{
    public const string TypeId = "interactable";
    public const float DefaultRange = 3f;

    /// <summary>What a HUD shows while the player aims at it, e.g. "Open the gate".</summary>
    public string Prompt { get; set; } = "Interact";
    /// <summary>Farthest distance from the ray's origin (normally the camera) it can be used from.</summary>
    public float Range { get; set; } = DefaultRange;

    public float ClampedRange => float.IsFinite(Range) ? Math.Clamp(Range, 0f, 1000f) : DefaultRange;

    /// <summary>Raised on the game thread after the gameobject's scripts ran OnInteract. Runtime only, never saved.</summary>
    public event Action<Interaction> Interacted;

    // Nothing runs on its own; settings are read when a ray finds it.
    public override void OnChanged(BasicEntity owner) { }

    /// <summary>The first enabled Interactable on an enabled gameobject, or null.</summary>
    public static InteractableComponent Of(BasicEntity entity) =>
        entity is { IsEnabled: true } ? entity.GetComponents<InteractableComponent>().FirstOrDefault(i => i.Enabled) : null;

    /// <summary>
    /// Closest interactable along a ray: the first collider hit must be an interactable within its
    /// <see cref="Range"/> (anything in front blocks it). Triggers are see-through.
    /// </summary>
    public static InteractableComponent Find(ScenePhysics physics, Vector3 origin, Vector3 direction, float maxDistance,
        out RaycastHit hit, BasicEntity ignore = null)
    {
        hit = default;
        if (physics == null || !physics.Raycast(origin, direction, maxDistance, out hit, ignore)) return null;
        InteractableComponent interactable = Of(hit.GameObject);
        return interactable != null && hit.Distance <= interactable.ClampedRange ? interactable : null;
    }

    /// <summary>
    /// Runs OnInteract on every running script of <paramref name="target"/>, then raises
    /// <see cref="Interacted"/>. False (and nothing runs) when the target has no enabled Interactable.
    /// </summary>
    public static bool Use(BasicEntity target, Interaction interaction)
    {
        InteractableComponent interactable = Of(target);
        if (interactable == null) return false;
        interaction.Hit.GameObject = target;
        foreach (var script in target.GetComponents<ScriptBehaviourComponent>().ToArray())
            script.Notify("OnInteract", s => s.OnInteract(interaction));
        interactable.Interacted?.Invoke(interaction);
        return true;
    }
}
