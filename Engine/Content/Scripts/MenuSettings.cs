using Engine.Logic;
using Engine.Recources;

namespace Engine.Scripting;

/// <summary>
/// One option row of a game menu (<see cref="MainMenuScript"/>, <see cref="PauseMenuScript"/>): a name, its
/// values and how to read and change the setting. Change it with <see cref="Step"/>, so a value changed inside
/// Anvil is put back when Play stops.
/// </summary>
public sealed class MenuSetting
{
    public string Name, Description;
    public string[] Values;
    public Func<int> Get;
    public Action<int> Set;
    /// <summary>Changes engine-wide state such as <see cref="GameSettings"/> (restored when Play stops inside Anvil).</summary>
    public bool EngineWide = true;

    public string ValueText => Values[Get()];

    /// <summary>Moves to the next (+1) or previous (-1) value, wrapping around. Returns the new index.</summary>
    public int Step(int direction)
    {
        MenuSettings.RememberForEditor(this);
        int count = Values.Length;
        int value = ((Get() + direction) % count + count) % count;
        Set(value);
        return value;
    }
}

/// <summary>Settings shared by the sample menus. They apply immediately and are not saved.</summary>
public static class MenuSettings
{
    // Engine-wide values changed from a menu inside Anvil, put back when Play stops. One registry for every
    // menu, so the value from before Play is the one restored, whichever menu changed it last.
    private static readonly Dictionary<string, Action> EditorRestore = new();
    private static bool _restoreHooked;
    private static readonly int[] FpsCaps = { 0, 30, 60, 120, 144 };

    /// <summary>Display and rendering options. A new list each call; the rows read the live values.</summary>
    public static List<MenuSetting> Graphics() => new()
    {
        // The engine's debug stats (DebugScreen); 3 is their default detail level.
        Toggle("Performance overlay", "FPS, frame time, memory and draw stats in the top-left corner.",
            () => GameSettings.u_showdisplayinfo > 0, v => GameSettings.u_showdisplayinfo = v ? 3 : 0),
        Toggle("VSync", "Wait for the display's refresh. A frame rate cap takes precedence.",
            () => GameSettings.g_vsync, v => GameSettings.g_vsync = v),
        new MenuSetting
        {
            Name = "Frame rate cap",
            Description = "Limits frames per second. Unlimited lets the GPU run as fast as it can.",
            Values = new[] { "Unlimited", "30", "60", "120", "144" },
            Get = () => Math.Max(0, Array.IndexOf(FpsCaps, GameSettings.g_fixedfps)),
            Set = i => GameSettings.g_fixedfps = FpsCaps[i],
        },
        Toggle("Anti-aliasing", "Temporal anti-aliasing (TAA) smooths edges and shimmer.",
            () => GameSettings.g_taa, v => GameSettings.g_taa = v),
        Toggle("Ambient occlusion", "Screen-space ambient occlusion darkens creases and contact shadows.",
            () => GameSettings.g_ssao_draw, v => GameSettings.g_ssao_draw = v),
        Toggle("Bloom", "The glow around bright lights and emissive surfaces.",
            () => GameSettings.g_BloomEnable, v => GameSettings.g_BloomEnable = v),
        Toggle("Volumetric fog", "Froxel fog, with shafts where the sun breaks through.",
            () => GameSettings.g_FroxelFogEnabled, v => GameSettings.g_FroxelFogEnabled = v),
        Toggle("Reflections", "Screen-space reflections on water and glossy surfaces.",
            () => GameSettings.g_SSReflection, v => GameSettings.g_SSReflection = v),
    };

    /// <summary>The engine's master volume, in steps of 10%.</summary>
    public static MenuSetting MasterVolume() => new()
    {
        Name = "Master volume",
        Description = "Every sound and music track the game plays.",
        Values = Enumerable.Range(0, 11).Select(i => $"{i * 10}%").ToArray(),
        Get = () => Math.Clamp((int)MathF.Round(Audio.MasterVolume * 10f), 0, 10),
        Set = i => Audio.MasterVolume = i / 10f,
    };

    public static MenuSetting Toggle(string name, string description, Func<bool> get, Action<bool> set) => new()
    {
        Name = name,
        Description = description,
        Values = new[] { "Off", "On" },
        Get = () => get() ? 1 : 0,
        Set = i => set(i == 1),
    };

    /// <summary>Inside Anvil, records the setting's value from before its first change this Play session.</summary>
    internal static void RememberForEditor(MenuSetting setting)
    {
        if (!GameFlow.IsEditor || !setting.EngineWide || EditorRestore.ContainsKey(setting.Name)) return;
        int original = setting.Get();
        Action<int> set = setting.Set;
        EditorRestore[setting.Name] = () => set(original);
        if (_restoreHooked) return;
        _restoreHooked = true;
        GameFlow.PlayStopped += () =>
        {
            foreach (Action restore in EditorRestore.Values) restore();
            EditorRestore.Clear();
        };
    }
}
