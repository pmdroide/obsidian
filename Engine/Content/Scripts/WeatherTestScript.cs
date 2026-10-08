using System.Globalization;
using Engine.Logic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// The WeatherTest sample scene: switches the scene's weather (Inspector > Environment > Weather) during Play.
/// Runs on the Main Camera next to Freecam (Add Component > Script Behaviour > Weather Test).
///   1 clear  ·  2 rain  ·  3 sandstorm  ·  4 snow   (cross-fades; each brings its own wind)
///   Up / Down     intensity           Left / Right  wind direction
///   Z / X         wind speed          H             haze amount
///   T             time of day +3 h    C             auto-cycle the weathers
/// Everything it changes is put back when Play stops.
/// </summary>
public sealed class WeatherTestScript : ScriptBehaviour
{
    public const string ScriptId = "weather-test";

    private const string DocumentPath = "UI/WeatherTest";
    private const float FadeSeconds = 0.8f;
    private static readonly WeatherType[] Order =
        { WeatherType.None, WeatherType.Rain, WeatherType.Sandstorm, WeatherType.Snow };
    private static readonly float[] HazeSteps = { 0, 0.3f, 0.6f, 1 };

    [Tooltip("Step through rain, sandstorm, snow and clear skies on a timer.")]
    public bool AutoCycle;
    [Range(5, 120)]
    [Tooltip("Seconds each weather lasts while auto-cycling.")]
    public float SecondsPerWeather = 20;

    private UIManager _ui;
    private WeatherType _target;
    // The intensity the user picked; the scene's intensity fades towards it.
    private float _intensity;
    // 0..1 while cross-fading from one weather to the next.
    private float _fade = 1;
    private float _cycleTimer;
    private string _shown;

    /// <summary>Wind each weather brings when it fades in, m/s.</summary>
    public static float PresetWind(WeatherType type) => type switch
    {
        WeatherType.Rain => 5,
        WeatherType.Sandstorm => 14,
        WeatherType.Snow => 2,
        _ => 3,
    };

    public WeatherType Target => _target;

    public override void Start()
    {
        _ui = GameUI.Open(DocumentPath);
        _shown = null;
        _cycleTimer = 0;
        EnvironmentSettings environment = SceneEnvironment;
        if (environment == null) return;
        _target = environment.Weather;
        _intensity = environment.WeatherIntensity > 0 ? environment.WeatherIntensity : 0.7f;
        _fade = 1;
    }

    public override void Stop()
    {
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void Update()
    {
        EnvironmentSettings environment = SceneEnvironment;
        if (environment == null) return;
        if (!DebugScreen.ConsoleOpen) HandleKeys(environment);

        if (AutoCycle)
        {
            _cycleTimer += DeltaTime;
            if (_cycleTimer >= SecondsPerWeather)
                Select(Order[(Array.IndexOf(Order, _target) + 1) % Order.Length]);
        }

        // Fade the old weather out, swap, then fade the new one in. Clear skies swap at once.
        if (environment.Weather != _target)
        {
            _fade = environment.Weather == WeatherType.None ? 0 : _fade - DeltaTime / FadeSeconds;
            if (_fade <= 0)
            {
                _fade = 0;
                environment.Weather = _target;
                environment.WindSpeed = PresetWind(_target);
            }
        }
        else _fade = Math.Min(1, _fade + DeltaTime / FadeSeconds);
        environment.WeatherIntensity = _intensity * _fade * _fade * (3 - 2 * _fade);

        UpdateHud(environment);
    }

    /// <summary>Fade to <paramref name="type"/>; also what the 1-4 keys and auto-cycle call.</summary>
    public void Select(WeatherType type)
    {
        _cycleTimer = 0;
        _target = type;
    }

    private void HandleKeys(EnvironmentSettings environment)
    {
        for (int i = 0; i < Order.Length; i++)
            if (GameInput.WasPressed(Keys.D1 + i) || GameInput.WasPressed(Keys.NumPad1 + i)) Select(Order[i]);
        if (GameInput.WasPressed(Keys.C))
        {
            AutoCycle = !AutoCycle;
            _cycleTimer = 0;
        }

        float step = DeltaTime;
        if (GameInput.IsDown(Keys.Up)) _intensity = Math.Min(1, _intensity + 0.5f * step);
        if (GameInput.IsDown(Keys.Down)) _intensity = Math.Max(0.05f, _intensity - 0.5f * step);
        if (GameInput.IsDown(Keys.Right)) environment.WindDirection = ((environment.WindDirection + 60 * step) % 360 + 360) % 360;
        if (GameInput.IsDown(Keys.Left)) environment.WindDirection = ((environment.WindDirection - 60 * step) % 360 + 360) % 360;
        if (GameInput.IsDown(Keys.X)) environment.WindSpeed = Math.Min(40, environment.WindSpeed + 6 * step);
        if (GameInput.IsDown(Keys.Z)) environment.WindSpeed = Math.Max(0, environment.WindSpeed - 6 * step);
        if (GameInput.WasPressed(Keys.H))
        {
            int next = Array.FindIndex(HazeSteps, h => h > environment.WeatherHaze + 0.01f);
            environment.WeatherHaze = next < 0 ? HazeSteps[0] : HazeSteps[next];
        }
        // A new starting hour restarts the day/night cycle there.
        if (GameInput.WasPressed(Keys.T) && environment.DayNightCycle)
            environment.TimeOfDay = (float.Floor(environment.TimeOfDay) + 3) % 24;
    }

    private void UpdateHud(EnvironmentSettings environment)
    {
        if (_ui == null) return;
        var c = CultureInfo.InvariantCulture;
        string weather = _target == WeatherType.None ? "Clear" : _target.ToString();
        string fading = environment.Weather != _target ? $"  (fading out {environment.Weather})" : "";
        string time = environment.DayNightCycle
            ? string.Format(c, "Starting hour {0:00}:00  ·  T +3 h", environment.TimeOfDay)
            : "Custom skybox (no day/night cycle)";
        string cycle = AutoCycle
            ? string.Format(c, "Auto-cycle on  ·  next in {0:0} s", Math.Max(0, SecondsPerWeather - _cycleTimer))
            : "Auto-cycle off  ·  C to start";
        string text = string.Join("|", weather, fading, _intensity, environment.WeatherHaze, environment.WindSpeed,
            environment.WindDirection, time, cycle);
        if (text == _shown) return;
        _shown = text;
        _ui.SetText("#weather", weather + fading);
        _ui.SetText("#intensity", string.Format(c, "Intensity {0:0}%  ·  Haze {1:0.0}", _intensity * 100, environment.WeatherHaze));
        Vector2 wind = WeatherProfile.Wind(environment);
        _ui.SetText("#wind", string.Format(c, "Wind {0:0.0} m/s towards {1:0}°", wind.Length(), environment.WindDirection));
        _ui.SetText("#time", time);
        _ui.SetText("#cycle", cycle);
    }
}
