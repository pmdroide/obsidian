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
/// The SmokeTest sample scene (Content/Scenes/SmokeTest.obsc): Counter-Strike 2 style smoke grenades built from
/// Particle Systems with Volumetric > Source Voxels. One blooms in the open, one floods a corridor, one fills a
/// roofed room and leaks out of its door; a smoke stack shows the Particles source. Runs on the Main Camera next
/// to Freecam and aims through the crosshair:
///   G       throw a smoke grenade (it flies there, lands and pops "Thrown Smoke")
///   Click   shoot: a tunnel through every smoke along the shot (CarveTunnels)
///   H       HE grenade where you point: a hole blown into the smoke (CarveHoles) and a flash
///   R       restart every smoke        V   Volumetric on / off (the billboards alone, for comparison)
/// The HUD shows how much of the view the smoke hides along the crosshair (VolumeOpacity), what NPCs use.
/// </summary>
public sealed class SmokeTestScript : ScriptBehaviour
{
    public const string ScriptId = "smoke-test";

    private const string DocumentPath = "UI/SmokeTest";
    private const string SmokePrefix = "Smoke ";

    [Range(4, 40)]
    [Tooltip("Metres per second a thrown grenade flies.")]
    public float ThrowSpeed = 16;
    [Range(0.02f, 1)]
    [Tooltip("Radius of a bullet tunnel, metres.")]
    public float BulletRadius = 0.18f;
    [Range(0.5f, 8)]
    [Tooltip("Radius of the hole an HE grenade blows, metres.")]
    public float BlastRadius = 3f;

    private UIManager _ui;
    private readonly List<ParticleSystemComponent> _smokes = new();
    private ParticleSystemComponent _thrown, _blast;
    private BasicEntity _thrownHost, _grenade;
    private Vector3 _throwFrom, _throwTo;
    private float _throwTime, _throwDuration;
    private bool _volumetric = true;
    private int _shots, _blasts, _throws;
    private string _shown;

    public IReadOnlyList<ParticleSystemComponent> Smokes => _smokes;
    public bool IsThrowing => _grenade != null;

    public override void Start()
    {
        _ui = GameUI.Open(DocumentPath);
        _shown = null;
        _volumetric = true;
        _shots = _blasts = _throws = 0;
        _grenade = null;
        _smokes.Clear();
        foreach (BasicEntity entity in GameFlow.SceneLogic?.ActiveScene?.BasicEntities ?? new List<BasicEntity>())
            if (entity.Name?.StartsWith(SmokePrefix, StringComparison.OrdinalIgnoreCase) == true ||
                entity.Name == "Thrown Smoke")
                _smokes.AddRange(entity.GetComponents<ParticleSystemComponent>());
        _thrownHost = FindGameObject("Thrown Smoke");
        _thrown = _thrownHost?.GetComponent<ParticleSystemComponent>();
        _blast = FindGameObject("HE Blast")?.GetComponent<ParticleSystemComponent>();
    }

    public override void Stop()
    {
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void Update()
    {
        if (!DebugScreen.ConsoleOpen) HandleInput();
        Fly();
        UpdateHud();
    }

    /// <summary>Throws a smoke grenade from the camera to <paramref name="target"/>; it pops on landing.</summary>
    public void Throw(Vector3 target)
    {
        if (_thrown == null) return;
        if (_grenade != null) Destroy(_grenade);
        _throwFrom = (MainCamera?.Position ?? Position) + new Vector3(0, 0, -0.3f);
        _throwTo = target;
        _throwTime = 0;
        _throwDuration = Math.Max(0.25f, Vector3.Distance(_throwFrom, _throwTo) / Math.Max(ThrowSpeed, 1));
        _grenade = Spawn("IsoSphere", _throwFrom, "Smoke Grenade");
        if (_grenade != null) _grenade.Scale = new Vector3(0.09f);
        else Land();
    }

    /// <summary>A shot along a ray: tunnels through every smoke up to what it hits (what a click does).</summary>
    public void Shoot(Ray ray)
    {
        _shots++;
        float distance = Raycast(ray, 200, out RaycastHit hit) ? hit.Distance : 200;
        ParticleSystemComponent.CarveTunnels(ray.Position, ray.Position + ray.Direction * distance, BulletRadius);
    }

    /// <summary>An HE grenade going off at a point: blows a hole into every smoke around it (what H does).</summary>
    public void Blast(Vector3 point)
    {
        _blasts++;
        ParticleSystemComponent.CarveHoles(point, BlastRadius);
        _blast?.EmitAt(point, 40);
    }

    private void HandleInput()
    {
        Ray aim = CameraRay;
        if (GameInput.WasPressed(Keys.G) && Raycast(aim, 120, out RaycastHit target)) Throw(target.Point);
        if (GameInput.MouseClicked) Shoot(aim);
        if (GameInput.WasPressed(Keys.H) && Raycast(aim, 120, out RaycastHit blast)) Blast(blast.Point + blast.Normal * 0.3f);
        if (GameInput.WasPressed(Keys.R))
            foreach (ParticleSystemComponent smoke in _smokes)
                if (smoke.Main.PlayOnAwake) smoke.Restart();
                else smoke.Stop(clear: true);
        if (GameInput.WasPressed(Keys.V))
        {
            _volumetric = !_volumetric;
            foreach (ParticleSystemComponent smoke in _smokes)
            {
                smoke.Volumetric.Enabled = _volumetric;
                // Without the volume the jet of puffs is all there is; with it they draw again only if asked.
                if (smoke.Main.PlayOnAwake || smoke == _thrown && _throws > 0) smoke.Restart();
            }
        }
    }

    // The grenade flies an arc to its target, then the smoke pops where it landed.
    private void Fly()
    {
        if (_grenade == null) return;
        _throwTime += DeltaTime;
        float t = Math.Clamp(_throwTime / _throwDuration, 0, 1);
        float arc = Math.Min(4f, Vector3.Distance(_throwFrom, _throwTo) * 0.2f);
        _grenade.Position = Vector3.Lerp(_throwFrom, _throwTo, t) + new Vector3(0, 0, 4 * arc * t * (1 - t));
        if (t >= 1) Land();
    }

    private void Land()
    {
        if (_grenade != null) Destroy(_grenade);
        _grenade = null;
        _throws++;
        if (_thrownHost == null) return;
        _thrownHost.Position = _throwTo + new Vector3(0, 0, 0.08f);
        _thrown.Restart();
    }

    private void UpdateHud()
    {
        if (_ui == null) return;
        var c = CultureInfo.InvariantCulture;
        int voxels = 0, blooming = 0;
        foreach (ParticleSystemComponent smoke in _smokes)
        {
            if (smoke.Volume == null || !smoke.HasVolume) continue;
            voxels += smoke.Volume.FilledVoxels;
            blooming++;
        }
        Ray aim = CameraRay;
        float distance = Raycast(aim, 200, out RaycastHit hit) ? hit.Distance : 60;
        float hidden = ParticleSystemComponent.VolumeOpacity(aim.Position, aim.Position + aim.Direction * distance);
        string state = !_volumetric ? "Volumetric off  ·  billboards only  ·  V to turn on"
            : IsThrowing ? "Grenade in the air..."
            : "Volumetric voxel smoke  ·  V to compare";
        string sight = string.Format(c, "Crosshair: {0:0}% hidden by smoke{1}", hidden * 100,
            hidden > Components.NpcComponent.SmokeBlocksSight ? "  ·  NPCs can't see through" : "");
        string shots = string.Format(c, "Shots {0}  ·  HE blasts {1}", _shots, _blasts);
        string thrown = _thrown == null ? "" : _thrown.HasVolume
            ? string.Format(c, "Thrown smoke: {0:N0} of {1:N0} voxels{2}", _thrown.Volume.FilledVoxels, _thrown.Volume.TargetVoxels,
                _thrown.Volume.IsDissipating ? "  ·  dissipating" : "")
            : string.Format(c, "Thrown smokes {0}  ·  G to throw", _throws);
        string text = string.Join("|", voxels / 10, blooming, state, sight, shots, thrown);
        if (text == _shown) return;
        _shown = text;
        _ui.SetText("#smokes", string.Format(c, "{0} smokes  ·  {1:N0} voxels filled", blooming, voxels));
        _ui.SetText("#state", state);
        _ui.SetText("#sight", sight);
        _ui.SetText("#shots", shots);
        _ui.SetText("#thrown", thrown);
    }
}
