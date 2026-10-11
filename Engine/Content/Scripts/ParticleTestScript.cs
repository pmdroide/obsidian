using System.Globalization;
using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// The ParticleTest sample scene (Content/Scenes/ParticleTest.obsc): a campfire, a spark fountain bouncing off the
/// ground, fireworks built from sub-emitters, a magic vortex with a comet and an explosion. Runs on the Main Camera
/// next to Freecam and drives the systems through the Particle System script API:
///   Space   explosion (Restart on every "Explosion ..." system)
///   Click   a burst of sparks where the mouse points (EmitAt on "Impact Sparks")
///   F       fireworks on / off (Play / Stop)         P   pause / resume every system
///   R       restart every system
/// The comet circles the vortex, leaving a trail through Rate over Distance.
/// </summary>
public sealed class ParticleTestScript : ScriptBehaviour
{
    public const string ScriptId = "particle-test";

    private const string DocumentPath = "UI/ParticleTest";
    private const string ExplosionPrefix = "Explosion ";

    [Tooltip("Set off the explosion on a timer.")]
    public bool AutoExplode = true;
    [Range(1, 30)]
    [Tooltip("Seconds between automatic explosions.")]
    public float ExplosionInterval = 6;
    [Tooltip("Gameobject the comet circles around.")]
    public BasicEntity CometCenter;
    [Range(0.5f, 10)]
    public float CometRadius = 2.6f;
    [Range(0, 5)]
    [Tooltip("Radians per second around the centre.")]
    public float CometSpeed = 1.4f;

    private UIManager _ui;
    private readonly List<ParticleSystemComponent> _systems = new();
    private readonly List<ParticleSystemComponent> _explosion = new();
    private ParticleSystemComponent _impact, _fireworks, _sparks;
    private BasicEntity _comet;
    private Vector3 _cometCenter;
    private float _cometAngle, _explosionTimer;
    private bool _paused;
    private int _explosions, _clicks;
    private string _shown;

    public IReadOnlyList<ParticleSystemComponent> Systems => _systems;
    public int Explosions => _explosions;

    public override void Start()
    {
        _ui = GameUI.Open(DocumentPath);
        _shown = null;
        _paused = false;
        _explosions = _clicks = 0;
        _explosionTimer = 0;
        _systems.Clear();
        _explosion.Clear();
        foreach (BasicEntity entity in GameFlow.SceneLogic?.ActiveScene?.BasicEntities ?? new List<BasicEntity>())
            foreach (ParticleSystemComponent system in entity.GetComponents<ParticleSystemComponent>())
            {
                _systems.Add(system);
                if (entity.Name?.StartsWith(ExplosionPrefix, StringComparison.OrdinalIgnoreCase) == true) _explosion.Add(system);
            }
        _impact = FindGameObject("Impact Sparks")?.GetComponent<ParticleSystemComponent>();
        _fireworks = FindGameObject("Fireworks Launcher")?.GetComponent<ParticleSystemComponent>();
        _sparks = FindGameObject("Spark Fountain")?.GetComponent<ParticleSystemComponent>();
        _comet = FindGameObject("Comet");
        BasicEntity center = CometCenter ?? FindGameObject("Magic Vortex");
        _cometCenter = center != null ? center.Position + new Vector3(0, 0, 1.6f) : _comet?.Position ?? Vector3.Zero;
    }

    public override void Stop()
    {
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void Update()
    {
        if (!DebugScreen.ConsoleOpen) HandleInput();

        if (!_paused)
        {
            MoveComet();
            if (AutoExplode)
            {
                _explosionTimer += DeltaTime;
                if (_explosionTimer >= ExplosionInterval) Explode();
            }
        }
        UpdateHud();
    }

    /// <summary>Sets off every "Explosion ..." system at once (what Space and the timer do).</summary>
    public void Explode()
    {
        _explosionTimer = 0;
        _explosions++;
        foreach (ParticleSystemComponent system in _explosion) system.Restart();
    }

    /// <summary>A burst of sparks at a point, bouncing off the surface (what a click does).</summary>
    public void Impact(Vector3 point, Vector3 normal)
    {
        _clicks++;
        _impact?.EmitAt(point + normal * 0.05f, 40, normal * 3f);
    }

    private void HandleInput()
    {
        if (GameInput.WasPressed(Keys.Space)) Explode();
        if (GameInput.WasPressed(Keys.F) && _fireworks != null)
        {
            if (_fireworks.IsEmitting) _fireworks.Stop();
            else _fireworks.Play();
        }
        if (GameInput.WasPressed(Keys.P))
        {
            _paused = !_paused;
            foreach (ParticleSystemComponent system in _systems)
            {
                if (_paused) system.Pause();
                else if (system.IsPaused) system.Play();
            }
        }
        if (GameInput.WasPressed(Keys.R))
        {
            _paused = false;
            foreach (ParticleSystemComponent system in _systems)
                if (system.Main.PlayOnAwake) system.Restart();
                else system.Stop(clear: true);
        }
        if (GameInput.MouseClicked && !_paused && Raycast(MouseRay, 200, out RaycastHit hit))
            Impact(hit.Point, hit.Normal);
    }

    private void MoveComet()
    {
        if (_comet == null) return;
        _cometAngle = (_cometAngle + CometSpeed * DeltaTime) % MathHelper.TwoPi;
        _comet.Position = _cometCenter + new Vector3((float)Math.Cos(_cometAngle) * CometRadius,
            (float)Math.Sin(_cometAngle) * CometRadius, (float)Math.Sin(_cometAngle * 2) * 0.6f);
    }

    private void UpdateHud()
    {
        if (_ui == null) return;
        var c = CultureInfo.InvariantCulture;
        int particles = 0, playing = 0;
        foreach (ParticleSystemComponent system in _systems)
        {
            particles += system.ParticleCount;
            if (system.IsAlive) playing++;
        }
        string state = _paused ? "Paused  ·  P to resume" : "Running";
        string fireworks = _fireworks == null ? "" : _fireworks.IsEmitting ? "Fireworks on  ·  F to stop" : "Fireworks off  ·  F to launch";
        string explosion = AutoExplode
            ? string.Format(c, "Next explosion in {0:0} s  ·  Space now", Math.Max(0, ExplosionInterval - _explosionTimer))
            : "Space sets off the explosion";
        string bounces = _sparks == null ? "" : string.Format(c, "Spark bounces {0:N0}  ·  clicks {1}", _sparks.CollisionCount, _clicks);
        string text = string.Join("|", particles / 10, playing, state, fireworks, explosion, bounces);
        if (text == _shown) return;
        _shown = text;
        _ui.SetText("#count", string.Format(c, "{0:N0} particles  ·  {1} of {2} systems alive", particles, playing, _systems.Count));
        _ui.SetText("#state", state);
        _ui.SetText("#fireworks", fireworks);
        _ui.SetText("#explosion", explosion);
        _ui.SetText("#bounces", bounces);
    }
}
