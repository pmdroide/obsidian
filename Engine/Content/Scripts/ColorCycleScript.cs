using Engine.Components;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Interact (E) to repaint this gameobject in the next colour. Needs an Interactable, a Physics
/// component and a Material component. Works on Dynamic objects too, so you can still carry it.
/// </summary>
public sealed class ColorCycleScript : ScriptBehaviour
{
    public const string ScriptId = "color-cycle";

    private static readonly (string Name, Vector3 Color)[] Palette =
    {
        ("gold", new Vector3(0.98f, 0.77f, 0.25f)),
        ("teal", new Vector3(0.19f, 0.8f, 0.77f)),
        ("violet", new Vector3(0.62f, 0.4f, 1f)),
        ("coral", new Vector3(1f, 0.47f, 0.4f)),
    };

    private Vector3 _original;
    private int _index;

    public override void Start()
    {
        _original = CollisionTestFeed.ColorOf(GameObject);
        _index = -1;
    }

    public override void Stop() => CollisionTestFeed.Paint(GameObject, _original);

    public override void OnInteract(Interaction interaction)
    {
        _index = (_index + 1) % Palette.Length;
        // Through the feed, so it stays brightened while the player aims at it.
        CollisionTestFeed.Paint(GameObject, Palette[_index].Color);
        CollisionTestFeed.Post($"{GameObject.Name} turned {Palette[_index].Name}");
    }
}
