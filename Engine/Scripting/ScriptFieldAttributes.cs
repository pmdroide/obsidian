namespace Engine.Scripting;

// Unity-style serialization attributes for ScriptBehaviour fields. Public instance fields are
// saved and shown in the Inspector automatically; see ScriptFields for the rules.

/// <summary>Saves a private or protected field with the scene and shows it in the Inspector.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeFieldAttribute : Attribute { }

/// <summary>Keeps a serialized field out of the Inspector. It is still saved and applied.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HideInInspectorAttribute : Attribute { }

/// <summary>Shows a numeric field as a slider between <see cref="Min"/> and <see cref="Max"/>.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class RangeAttribute : Attribute
{
    public float Min { get; }
    public float Max { get; }
    public RangeAttribute(float min, float max)
    {
        Min = min;
        Max = max;
    }
}

/// <summary>Hover text for the field's Inspector label.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class TooltipAttribute : Attribute
{
    public string Text { get; }
    public TooltipAttribute(string text) => Text = text;
}

/// <summary>
/// Loads values saved under an older field name, so renaming a field keeps scene data.
/// The value is saved under the new name the next time the field is edited.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public sealed class FormerlySerializedAsAttribute : Attribute
{
    public string OldName { get; }
    public FormerlySerializedAsAttribute(string oldName) => OldName = oldName;
}
