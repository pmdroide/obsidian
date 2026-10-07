using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

public enum ScriptFieldKind
{
    Bool,
    Int,
    Float,
    Double,
    String,
    Enum,
    Vector2,
    Vector3,
    Color,
}

/// <summary>One serialized field of a <see cref="ScriptBehaviour"/> type.</summary>
public sealed class ScriptFieldInfo
{
    /// <summary>The C# field name; the key in <see cref="Engine.Components.ScriptBehaviourComponent.Fields"/>.</summary>
    public string Name { get; init; }
    /// <summary>Inspector label: "_moveSpeed" shows as "Move Speed".</summary>
    public string DisplayName { get; init; }
    public ScriptFieldKind Kind { get; init; }
    public Type FieldType { get; init; }
    public bool HideInInspector { get; init; }
    public string Tooltip { get; init; }
    public bool HasRange { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
    /// <summary>Enum fields: the names the Inspector offers, in declaration order.</summary>
    public IReadOnlyList<string> EnumNames { get; init; } = Array.Empty<string>();
    /// <summary>Names from <see cref="FormerlySerializedAsAttribute"/>, tried when <see cref="Name"/> is not stored.</summary>
    public IReadOnlyList<string> FormerNames { get; init; } = Array.Empty<string>();
    /// <summary>
    /// The value the script's field initializer gives a fresh instance. The first read constructs one
    /// instance of the script (never attached or started), so keep script constructors free of side effects.
    /// </summary>
    public object DefaultValue
    {
        get
        {
            object prototype = Prototype?.Value;
            if (prototype != null) return Field.GetValue(prototype);
            return FieldType.IsValueType ? Activator.CreateInstance(FieldType) : null;
        }
    }
    internal FieldInfo Field { get; init; }
    internal Lazy<object> Prototype { get; init; }
}

/// <summary>
/// Unity-style serialized fields for script behaviours. A field is serialized when it is an instance
/// field of the script (or a base class below <see cref="ScriptBehaviour"/>), is not readonly, is either
/// public without [NonSerialized] or marked [SerializeField], and has a supported type: bool, int, float,
/// double, string, an enum, Vector2, Vector3 or Color. Values are stored as JSON per attachment
/// (<see cref="Engine.Components.ScriptBehaviourComponent.Fields"/>); fields without a stored value keep
/// their C# initializer.
/// </summary>
public static class ScriptFields
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<ScriptFieldInfo>> Cache = new();

    /// <summary>Serialized fields of a registered script, or none for an unknown id.</summary>
    public static IReadOnlyList<ScriptFieldInfo> For(string scriptId) =>
        ScriptRegistry.Find(scriptId)?.ScriptType is { } type ? For(type) : Array.Empty<ScriptFieldInfo>();

    /// <summary>Serialized fields of a script type, base class fields first, in declaration order.</summary>
    public static IReadOnlyList<ScriptFieldInfo> For(Type scriptType)
    {
        if (scriptType == null || !typeof(ScriptBehaviour).IsAssignableFrom(scriptType)) return Array.Empty<ScriptFieldInfo>();
        return Cache.GetOrAdd(scriptType, Discover);
    }

    public static ScriptFieldInfo Find(string scriptId, string fieldName) =>
        For(scriptId).FirstOrDefault(f => f.Name == fieldName);

    private static IReadOnlyList<ScriptFieldInfo> Discover(Type scriptType)
    {
        var hierarchy = new List<Type>();
        for (var t = scriptType; t != null && t != typeof(ScriptBehaviour); t = t.BaseType) hierarchy.Insert(0, t);

        // Defaults come from one instance, built on first use; a throwing constructor falls back to default(T).
        var prototype = new Lazy<object>(() =>
        {
            try { return Activator.CreateInstance(scriptType); }
            catch { return null; }
        });

        var result = new List<ScriptFieldInfo>();
        foreach (var type in hierarchy)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .OrderBy(f => f.MetadataToken);
            foreach (var field in fields)
            {
                if (!IsSerialized(field) || !TryGetKind(field.FieldType, out var kind)) continue;
                var range = field.GetCustomAttribute<RangeAttribute>();
                result.Add(new ScriptFieldInfo
                {
                    Name = field.Name,
                    DisplayName = Nicify(field.Name),
                    Kind = kind,
                    FieldType = field.FieldType,
                    HideInInspector = field.IsDefined(typeof(HideInInspectorAttribute)),
                    Tooltip = field.GetCustomAttribute<TooltipAttribute>()?.Text,
                    HasRange = range != null && (kind == ScriptFieldKind.Int || kind == ScriptFieldKind.Float || kind == ScriptFieldKind.Double),
                    Min = range?.Min ?? 0f,
                    Max = range?.Max ?? 0f,
                    EnumNames = kind == ScriptFieldKind.Enum ? Enum.GetNames(field.FieldType) : Array.Empty<string>(),
                    FormerNames = field.GetCustomAttributes<FormerlySerializedAsAttribute>().Select(a => a.OldName).ToArray(),
                    Field = field,
                    Prototype = prototype,
                });
            }
        }
        return result;
    }

    private static bool IsSerialized(FieldInfo field)
    {
        if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.Name.Contains('<')) return false;
        if (field.IsNotSerialized) return false;
        return field.IsPublic || field.IsDefined(typeof(SerializeFieldAttribute));
    }

    private static bool TryGetKind(Type type, out ScriptFieldKind kind)
    {
        if (type == typeof(bool)) kind = ScriptFieldKind.Bool;
        else if (type == typeof(int)) kind = ScriptFieldKind.Int;
        else if (type == typeof(float)) kind = ScriptFieldKind.Float;
        else if (type == typeof(double)) kind = ScriptFieldKind.Double;
        else if (type == typeof(string)) kind = ScriptFieldKind.String;
        else if (type.IsEnum) kind = ScriptFieldKind.Enum;
        else if (type == typeof(Vector2)) kind = ScriptFieldKind.Vector2;
        else if (type == typeof(Vector3)) kind = ScriptFieldKind.Vector3;
        else if (type == typeof(Color)) kind = ScriptFieldKind.Color;
        else { kind = default; return false; }
        return true;
    }

    /// <summary>"_moveSpeed", "m_moveSpeed" and "moveSpeed" all become "Move Speed".</summary>
    public static string Nicify(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.StartsWith("m_", StringComparison.Ordinal)) name = name.Substring(2);
        else if (name.Length > 1 && name[0] == 'k' && char.IsUpper(name[1])) name = name.Substring(1);
        name = name.TrimStart('_');
        if (name.Length == 0) return name;

        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_') { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); continue; }
            if (i > 0 && sb.Length > 0 && sb[^1] != ' ')
            {
                char prev = name[i - 1];
                bool upperAfterLower = char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev));
                bool acronymEnd = char.IsUpper(c) && char.IsUpper(prev) && i + 1 < name.Length && char.IsLower(name[i + 1]);
                bool digitAfterLetter = char.IsDigit(c) && char.IsLetter(prev);
                if (upperAfterLower || acronymEnd || digitAfterLetter) sb.Append(' ');
            }
            sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    //  JSON encoding: numbers and bools as JSON values, enums by name,
    //  vectors as [x, y(, z)], colours as "#RRGGBBAA".
    // ------------------------------------------------------------------

    /// <summary>Converts <paramref name="value"/> (any compatible type, e.g. an int for a float field) to its stored JSON.</summary>
    public static JsonElement Encode(ScriptFieldInfo field, object value)
    {
        object v = Coerce(field, value);
        if ((v is float f && !float.IsFinite(f)) || (v is double d && !double.IsFinite(d)))
            throw new ArgumentException($"Field '{field.Name}' needs a finite number, not {v}.", nameof(value));
        switch (field.Kind)
        {
            case ScriptFieldKind.Enum:
                return JsonSerializer.SerializeToElement(v.ToString());
            case ScriptFieldKind.Vector2:
                var v2 = (Vector2)v;
                return JsonSerializer.SerializeToElement(new[] { v2.X, v2.Y });
            case ScriptFieldKind.Vector3:
                var v3 = (Vector3)v;
                return JsonSerializer.SerializeToElement(new[] { v3.X, v3.Y, v3.Z });
            case ScriptFieldKind.Color:
                var c = (Color)v;
                return JsonSerializer.SerializeToElement($"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}");
            default:
                return JsonSerializer.SerializeToElement(v, field.FieldType);
        }
    }

    /// <summary>Reads a stored value; false when it does not fit the field (e.g. the field's type changed).</summary>
    public static bool TryDecode(ScriptFieldInfo field, JsonElement json, out object value)
    {
        value = null;
        try
        {
            switch (field.Kind)
            {
                case ScriptFieldKind.Bool:
                    if (json.ValueKind != JsonValueKind.True && json.ValueKind != JsonValueKind.False) return false;
                    value = json.GetBoolean();
                    return true;
                case ScriptFieldKind.Int:
                    if (json.ValueKind != JsonValueKind.Number || !json.TryGetInt32(out int i)) return false;
                    value = i;
                    return true;
                case ScriptFieldKind.Float:
                    if (json.ValueKind != JsonValueKind.Number || !json.TryGetSingle(out float f)) return false;
                    value = f;
                    return true;
                case ScriptFieldKind.Double:
                    if (json.ValueKind != JsonValueKind.Number || !json.TryGetDouble(out double d)) return false;
                    value = d;
                    return true;
                case ScriptFieldKind.String:
                    if (json.ValueKind == JsonValueKind.Null) return true;
                    if (json.ValueKind != JsonValueKind.String) return false;
                    value = json.GetString();
                    return true;
                case ScriptFieldKind.Enum:
                    if (json.ValueKind != JsonValueKind.String ||
                        !Enum.TryParse(field.FieldType, json.GetString(), false, out object e)) return false;
                    value = e;
                    return true;
                case ScriptFieldKind.Vector2:
                case ScriptFieldKind.Vector3:
                    int n = field.Kind == ScriptFieldKind.Vector2 ? 2 : 3;
                    if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() != n) return false;
                    var parts = new float[n];
                    int k = 0;
                    foreach (var item in json.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Number || !item.TryGetSingle(out parts[k++])) return false;
                    }
                    value = n == 2 ? new Vector2(parts[0], parts[1]) : new Vector3(parts[0], parts[1], parts[2]);
                    return true;
                case ScriptFieldKind.Color:
                    if (json.ValueKind != JsonValueKind.String) return false;
                    string hex = json.GetString();
                    if (hex == null || hex.Length != 9 || hex[0] != '#' ||
                        !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba)) return false;
                    value = new Color((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
                    return true;
            }
        }
        catch (Exception) { /* malformed value: treat as missing */ }
        return false;
    }

    /// <summary>Converts a compatible value to the field's type, or throws <see cref="ArgumentException"/>.</summary>
    public static object Coerce(ScriptFieldInfo field, object value)
    {
        try
        {
            switch (field.Kind)
            {
                case ScriptFieldKind.String: return value?.ToString();
                case ScriptFieldKind.Enum:
                    return value is string s ? Enum.Parse(field.FieldType, s) : Enum.ToObject(field.FieldType, value);
                case ScriptFieldKind.Bool: return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                case ScriptFieldKind.Int: return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                case ScriptFieldKind.Float: return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                case ScriptFieldKind.Double: return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                default:
                    if (value != null && field.FieldType.IsInstanceOfType(value)) return value;
                    break;
            }
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException || ex is ArgumentException)
        {
            throw new ArgumentException($"{value ?? "null"} is not a valid {field.FieldType.Name} for field '{field.Name}'.", nameof(value), ex);
        }
        throw new ArgumentException($"{value ?? "null"} is not a valid {field.FieldType.Name} for field '{field.Name}'.", nameof(value));
    }

    /// <summary>The stored JSON for <paramref name="field"/>, trying its former names too.</summary>
    public static bool TryGetStored(ScriptFieldInfo field, IReadOnlyDictionary<string, JsonElement> values, out JsonElement json)
    {
        json = default;
        if (values == null) return false;
        if (values.TryGetValue(field.Name, out json)) return true;
        foreach (string former in field.FormerNames)
            if (values.TryGetValue(former, out json)) return true;
        return false;
    }

    /// <summary>The value an attachment gives the field: its stored value, otherwise the field's default.</summary>
    public static object GetValue(ScriptFieldInfo field, IReadOnlyDictionary<string, JsonElement> values) =>
        TryGetStored(field, values, out var json) && TryDecode(field, json, out var value) ? value : field.DefaultValue;

    /// <summary>
    /// Writes stored values into a script instance (all fields, or only <paramref name="names"/>).
    /// Fields named in <paramref name="names"/> without a stored value are reset to their default.
    /// Values that no longer fit their field are skipped and reported through <paramref name="warn"/>.
    /// </summary>
    public static void Apply(ScriptBehaviour instance, IReadOnlyDictionary<string, JsonElement> values,
        ICollection<string> names = null, Action<string> warn = null)
    {
        if (instance == null) return;
        foreach (var field in For(instance.GetType()))
        {
            bool requested = names != null && names.Contains(field.Name);
            if (names != null && !requested) continue;
            if (TryGetStored(field, values, out var json))
            {
                if (TryDecode(field, json, out var value)) field.Field.SetValue(instance, value);
                else warn?.Invoke($"Field '{field.Name}' of {instance.GetType().Name}: saved value {json.GetRawText()} is not a {field.FieldType.Name}; using the script's value.");
            }
            else if (requested)
            {
                field.Field.SetValue(instance, field.DefaultValue);
            }
        }
    }
}
