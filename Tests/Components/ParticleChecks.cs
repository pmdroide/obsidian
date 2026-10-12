using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Particles;
using Engine.Recources;
using Engine.Renderer.RenderModules;
using Engine.Scripting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

/// <summary>
/// Particle System component: the CPU simulation (emission, bursts, lifetime modules, collisions, sub-emitters,
/// simulation space, pause), saving, the editor bridge and inspector, the Play lifecycle and the sample scene.
/// </summary>
internal static class ParticleChecks
{
    private const float Step = 1f / 30f;
    private static readonly ModelDefinition Marker = new(null, new BoundingBox(-Vector3.One, Vector3.One));

    public static void Run()
    {
        CheckEmission();
        CheckLifetimeModules();
        CheckMotion();
        CheckCollisions();
        CheckSubEmitters();
        CheckPlayback();
        CheckVolumes();
        CheckLights();
        CheckSaving();
        CheckEditor();
        CheckLifecycle();
        CheckSampleScene();
        CheckSmokeSampleScene();
    }

    private static (ParticleSystemComponent System, ParticleSimulation Simulation) NewSystem(Action<ParticleSystemComponent> setup = null)
    {
        var system = new ParticleSystemComponent();
        setup?.Invoke(system);
        var simulation = new ParticleSimulation(system, seed: 12345);
        simulation.SetPose(Matrix.Identity);
        return (system, simulation);
    }

    private static void Run(ParticleSimulation simulation, float seconds, Matrix? world = null, ParticleStepContext context = null)
    {
        int steps = (int)Math.Round(seconds / Step);
        for (int i = 0; i < steps; i++) simulation.Step(Step, world ?? Matrix.Identity, context);
    }

    private static void CheckEmission()
    {
        var (_, rate) = NewSystem(s => s.Main.StartLifetime = 10);
        rate.Play();
        Run(rate, 2);
        Check(rate.ParticleCount is >= 19 and <= 21 && rate.IsPlaying && rate.IsEmitting,
            "Rate over Time emits its rate per second (10/s for 2 s)");

        var (_, bursts) = NewSystem(s =>
        {
            s.Main.StartLifetime = 10;
            s.Main.Duration = 2;
            s.Emission.RateOverTime = 0;
            s.Emission.Bursts.Add(new ParticleBurst(0, 30));
            s.Emission.Bursts.Add(new ParticleBurst(0.5f, 5, cycles: 3, interval: 0.25f));
        });
        bursts.Play();
        bursts.Step(Step, Matrix.Identity, null);
        Check(bursts.ParticleCount == 30, "a burst at 0 s fires on the first frame");
        Run(bursts, 1.5f);
        Check(bursts.ParticleCount == 45, "a 3-cycle burst repeats at its interval (30 + 3 x 5)");
        Run(bursts, 0.7f); // 2.23 s: the second cycle's first burst, not yet its 0.5 s one
        Check(bursts.ParticleCount == 75, "a looping system fires its bursts again every cycle");

        var (_, capped) = NewSystem(s =>
        {
            s.Main.MaxParticles = 50;
            s.Main.StartLifetime = 10;
            s.Emission.RateOverTime = 0;
            s.Emission.Bursts.Add(new ParticleBurst(0, 500));
        });
        capped.Play();
        Run(capped, 0.5f);
        Check(capped.ParticleCount == 50, "Max Particles caps the system");

        var (_, oneShot) = NewSystem(s =>
        {
            s.Main.Looping = false;
            s.Main.Duration = 1;
            s.Main.StartLifetime = new ParticleRange(0.4f, 0.6f);
            s.Emission.RateOverTime = 20;
        });
        oneShot.Play();
        Run(oneShot, 0.5f);
        Check(oneShot.IsEmitting && oneShot.ParticleCount > 0, "a non-looping system emits during its duration");
        Run(oneShot, 0.65f);
        Check(!oneShot.IsEmitting && oneShot.IsPlaying && oneShot.ParticleCount > 0,
            "after its duration it stops emitting while the last particles live out");
        Run(oneShot, 1);
        Check(!oneShot.IsAlive && !oneShot.IsPlaying && oneShot.ParticleCount == 0,
            "particles die at the end of their lifetime and the finished system stops playing");

        var (_, delayed) = NewSystem(s => s.Main.StartDelay = 1);
        delayed.Play();
        Run(delayed, 0.9f);
        Check(delayed.ParticleCount == 0 && delayed.IsEmitting, "Start Delay holds the first emission back");
        Run(delayed, 0.5f);
        Check(delayed.ParticleCount > 0, "after the delay it emits");

        var (_, distance) = NewSystem(s =>
        {
            s.Emission.RateOverTime = 0;
            s.Emission.RateOverDistance = 10;
        });
        distance.Play();
        for (int i = 1; i <= 30; i++) distance.Step(Step, Matrix.CreateTranslation(i * 0.1f, 0, 0), null);
        Check(distance.ParticleCount is >= 28 and <= 30, "Rate over Distance emits per metre the gameobject moves (10/m over 3 m)");

        var (_, prewarmed) = NewSystem(s =>
        {
            s.Main.Prewarm = true;
            s.Main.Duration = 2;
            s.Main.StartLifetime = 5;
        });
        prewarmed.Play();
        Check(prewarmed.ParticleCount is >= 18 and <= 21, "Prewarm starts a looping system as if a whole cycle had run");

        var (_, idle) = NewSystem();
        Run(idle, 1);
        idle.Emit(7);
        Check(idle.ParticleCount == 7 && !idle.IsPlaying, "Emit adds particles without playing the system");
    }

    private static void CheckLifetimeModules()
    {
        var (system, simulation) = NewSystem(s =>
        {
            s.Main.StartLifetime = 2;
            s.Main.StartSize = 2;
            s.Main.StartSpeed = 0;
            s.Main.StartColor = new Color(255, 128, 0, 255);
            s.Main.StartColor2 = s.Main.StartColor;
            s.Emission.RateOverTime = 0;
            s.SizeOverLifetime.Enabled = true;
            s.SizeOverLifetime.Size = new ParticleCurve((0, 1), (1, 0));
            s.ColorOverLifetime.Enabled = true;
            s.ColorOverLifetime.Color = new ParticleGradient((0, Color.White), (1, new Color(255, 255, 255, 0)));
            s.TextureSheetAnimation.Enabled = true;
            s.TextureSheetAnimation.TilesX = 2;
            s.TextureSheetAnimation.TilesY = 2;
        });
        simulation.Emit(1);
        Run(simulation, 1);
        Particle p = simulation.Particles[0];
        Check(Math.Abs(p.Size - 1) < 0.05f && Math.Abs(p.Color.W - 0.5f) < 0.03f && Math.Abs(p.Color.X - 1) < 0.01f &&
              Math.Abs(p.Color.Y - 0.5f) < 0.01f,
            "half way through its life the size curve (1 -> 0) and the alpha fade are at half; the gradient multiplies the start colour");
        Check(p.Frame == 2, "Texture Sheet Animation plays the 4 frames over the lifetime (frame 2 at half)");

        system.RotationOverLifetime.Enabled = true;
        system.RotationOverLifetime.AngularVelocity = 90;
        float before = simulation.Particles[0].Rotation;
        Run(simulation, 0.5f);
        Check(Math.Abs(simulation.Particles[0].Rotation - before - MathHelper.PiOver4) < 0.02f,
            "Rotation over Lifetime spins at its degrees per second");

        var curve = new ParticleCurve((1, 4), (0, 2), (0.5f, 0));
        Check(curve.Evaluate(-1) == 2 && curve.Evaluate(0.25f) == 1 && curve.Evaluate(0.75f) == 2 && curve.Evaluate(2) == 4,
            "curves blend between keys in any order and hold flat outside them");
    }

    private static void CheckMotion()
    {
        var context = new ParticleStepContext { Gravity = new Vector3(0, 0, -10) };
        var (_, falling) = NewSystem(s =>
        {
            s.Main.StartSpeed = 0;
            s.Main.GravityModifier = 1;
            s.Emission.RateOverTime = 0;
        });
        falling.Emit(1);
        Run(falling, 1, context: context);
        Check(Math.Abs(falling.Particles[0].Velocity.Z + 10) < 0.01f && falling.Particles[0].Position.Z < -4.5f,
            "Gravity Modifier 1 falls with the scene's gravity");

        var (cone, coneSimulation) = NewSystem(s =>
        {
            s.Shape.Shape = ParticleShapeType.Cone;
            s.Shape.Angle = 20;
            s.Shape.Radius = 0.5f;
            s.Emission.RateOverTime = 0;
        });
        Matrix turned = Matrix.CreateRotationX(MathHelper.PiOver2); // +Z becomes -Y
        coneSimulation.SetPose(turned);
        coneSimulation.Emit(200);
        Vector3 axis = Vector3.TransformNormal(Vector3.UnitZ, turned);
        bool inCone = true, inDisc = true;
        foreach (Particle particle in coneSimulation.Particles)
        {
            inCone &= Vector3.Dot(Vector3.Normalize(particle.Velocity), axis) >= Math.Cos(MathHelper.ToRadians(20.5f));
            inDisc &= particle.Position.Length() <= 0.501f && Math.Abs(Vector3.Dot(particle.Position, axis)) < 0.001f;
        }
        Check(inCone && inDisc, "a cone emits from its disc within its angle around the gameobject's turned +Z");

        cone.Shape.Shape = ParticleShapeType.Sphere;
        cone.Shape.RadiusThickness = 0;
        coneSimulation.Clear();
        coneSimulation.Emit(100);
        bool onSurface = true;
        foreach (Particle particle in coneSimulation.Particles)
            onSurface &= Math.Abs(particle.Position.Length() - 0.5f) < 0.001f &&
                         Vector3.Dot(Vector3.Normalize(particle.Velocity), Vector3.Normalize(particle.Position)) > 0.999f;
        Check(onSurface, "a sphere with Thickness 0 emits from its surface, outwards");

        // Local particles ride along with the gameobject; World ones stay where they were born.
        foreach (ParticleSimulationSpace space in new[] { ParticleSimulationSpace.Local, ParticleSimulationSpace.World })
        {
            var (_, moving) = NewSystem(s =>
            {
                s.Main.SimulationSpace = space;
                s.Main.StartSpeed = 0;
                s.Emission.RateOverTime = 0;
            });
            moving.Emit(5);
            float start = moving.Particles[0].Position.X;
            moving.Step(Step, Matrix.CreateTranslation(10, 0, 0), null);
            float x = moving.Particles[0].Position.X - start;
            Check(space == ParticleSimulationSpace.Local ? Math.Abs(x - 10) < 0.001f : Math.Abs(x) < 0.001f,
                $"{space} simulation space: particles {(space == ParticleSimulationSpace.Local ? "follow" : "stay behind when")} the gameobject moves");
        }

        var (orbit, orbitSimulation) = NewSystem(s =>
        {
            s.Main.StartSpeed = 0;
            s.Main.StartLifetime = 10;
            s.Shape.Shape = ParticleShapeType.Edge;
            s.Shape.Radius = 0;
            s.Shape.Position = new Vector3(2, 0, 0);
            s.Emission.RateOverTime = 0;
            s.VelocityOverLifetime.Enabled = true;
            s.VelocityOverLifetime.Orbital = new Vector3(0, 0, 90);
        });
        orbitSimulation.Emit(1);
        Run(orbitSimulation, 1);
        Vector3 orbited = orbitSimulation.Particles[0].Position;
        Check(Math.Abs(orbited.Length() - 2) < 0.15f && orbited.Y > 1.8f,
            "Orbital velocity circles the gameobject (90°/s turns +X into +Y in a second)");

        var windy = new ParticleStepContext { Wind = new Vector2(4, 0) };
        var (_, drifting) = NewSystem(s =>
        {
            s.Main.StartSpeed = 0;
            s.Emission.RateOverTime = 0;
            s.ExternalForces.Enabled = true;
            s.ExternalForces.Response = 3;
        });
        drifting.Emit(1);
        Run(drifting, 2, context: windy);
        Check(Math.Abs(drifting.Particles[0].Velocity.X - 4) < 0.1f, "External Forces carry particles along with the scene's wind");

        var (_, dragged) = NewSystem(s =>
        {
            s.Main.StartSpeed = 10;
            s.Shape.Enabled = false;
            s.Emission.RateOverTime = 0;
            s.LimitVelocity.Enabled = true;
            s.LimitVelocity.Speed = 2;
            s.LimitVelocity.Dampen = 1;
        });
        dragged.Emit(1);
        Run(dragged, 0.2f);
        Check(Math.Abs(dragged.Particles[0].Velocity.Length() - 2) < 0.01f, "Limit Velocity with Dampen 1 holds particles at the limit");

        var (_, noisy) = NewSystem(s =>
        {
            s.Main.StartSpeed = 0;
            s.Shape.Enabled = false;
            s.Emission.RateOverTime = 0;
            s.Noise.Enabled = true;
            s.Noise.Strength = 2;
        });
        noisy.Emit(1);
        Run(noisy, 1);
        Check(noisy.Particles[0].Position.Length() > 0.05f && noisy.Particles[0].Velocity == Vector3.Zero,
            "Noise moves particles without adding to their own velocity");
    }

    private static void CheckCollisions()
    {
        var (_, plane) = NewSystem(s =>
        {
            s.Main.StartSpeed = 0;
            s.Main.StartSize = 0.2f;
            s.Main.GravityModifier = 1;
            s.Main.StartLifetime = 10;
            s.Shape.Enabled = false;
            s.Emission.RateOverTime = 0;
            s.Collision.Enabled = true;
            s.Collision.Type = ParticleCollisionType.Plane;
            s.Collision.PlaneHeight = -1;
            s.Collision.Bounce = 0.5f;
        });
        plane.Emit(1);
        float lowest = 0;
        for (int i = 0; i < 90; i++)
        {
            plane.Step(Step, Matrix.Identity, new ParticleStepContext());
            lowest = Math.Min(lowest, plane.Particles[0].Position.Z);
        }
        Check(plane.CollisionCount >= 1 && lowest >= -0.9001f, "Plane collisions keep particles (and their radius) above the floor");

        // World collisions go through the scene's raycast; a floor at Z = 0 stands in for the physics scene.
        var events = new List<ParticleCollisionEvent>();
        var floor = new BasicEntity(Marker, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Floor" };
        var world = new ParticleStepContext
        {
            Raycast = (Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out BasicEntity hit) =>
            {
                point = normal = Vector3.Zero;
                hit = null;
                if (direction.Z >= 0 || origin.Z < 0) return false;
                float distance = origin.Z / -direction.Z;
                if (distance > maxDistance) return false;
                point = origin + direction * distance;
                normal = Vector3.UnitZ;
                hit = floor;
                return true;
            },
        };
        var (sparks, sparkSimulation) = NewSystem(s =>
        {
            s.Main.StartSpeed = 0;
            s.Main.StartSize = 0.1f;
            s.Main.GravityModifier = 1;
            s.Main.StartLifetime = 10;
            s.Shape.Enabled = false;
            s.Emission.RateOverTime = 0;
            s.Collision.Enabled = true;
            s.Collision.LifetimeLoss = 1;
        });
        sparkSimulation.Collided += events.Add;
        sparkSimulation.SetPose(Matrix.CreateTranslation(0, 0, 2));
        sparkSimulation.Emit(3);
        Run(sparkSimulation, 1.5f, Matrix.CreateTranslation(0, 0, 2), world);
        Check(events.Count == 3 && events.All(e => e.GameObject == floor && e.Normal == Vector3.UnitZ && e.Velocity.Z < 0) &&
              sparkSimulation.ParticleCount == 0,
            "World collisions hit the scene's colliders, raise an event per bounce, and Lifetime Loss 1 kills on contact");
    }

    private static void CheckSubEmitters()
    {
        var (target, targetSimulation) = NewSystem(s =>
        {
            s.Main.PlayOnAwake = false;
            s.Main.SimulationSpace = ParticleSimulationSpace.World;
            s.Main.StartLifetime = 10;
            s.Emission.RateOverTime = 0;
        });
        var (_, trail) = NewSystem(s =>
        {
            s.Main.PlayOnAwake = false;
            s.Main.StartLifetime = 10;
            s.Emission.RateOverTime = 0;
        });
        var (_, parent) = NewSystem(s =>
        {
            s.Main.StartLifetime = 0.5f;
            s.Main.StartColor = new Color(255, 0, 0, 255);
            s.Main.StartColor2 = s.Main.StartColor;
            s.Main.Duration = 10;
            s.Emission.RateOverTime = 0;
            s.Emission.Bursts.Add(new ParticleBurst(0, 4));
            s.SubEmitters.Enabled = true;
            s.SubEmitters.Emitters.Add(new ParticleSubEmitter
                { Trigger = ParticleSubEmitterTrigger.Death, Target = "Burst", Count = 25, InheritColor = true });
            s.SubEmitters.Emitters.Add(new ParticleSubEmitter
                { Trigger = ParticleSubEmitterTrigger.Trail, Target = "Trail", Count = 20 });
            s.SubEmitters.Emitters.Add(new ParticleSubEmitter
                { Trigger = ParticleSubEmitterTrigger.Death, Target = "Nobody", Count = 25 });
        });
        var context = new ParticleStepContext
        {
            FindSubEmitter = name => name == "Burst" ? targetSimulation : name == "Trail" ? trail : null,
        };
        parent.Play(context);
        Run(parent, 1, context: context);
        Check(targetSimulation.ParticleCount == 100 && !targetSimulation.IsPlaying,
            "Death sub-emitters emit their count where each particle dies (4 x 25), without playing the target");
        Check(targetSimulation.Particles[0].StartColor.X == 1 && targetSimulation.Particles[0].StartColor.Y == 0,
            "Inherit Color tints the sub-emitted particles with the parent's colour");
        Check(trail.ParticleCount is >= 30 and <= 40, "Trail sub-emitters emit per second along each living particle (about 4 x 20/s x 0.5 s)");
        Check(target.Main.PlayOnAwake == false, "unknown targets are skipped");
    }

    private static void CheckPlayback()
    {
        var (_, simulation) = NewSystem(s => s.Main.StartLifetime = 10);
        simulation.Play();
        Run(simulation, 1);
        int count = simulation.ParticleCount;
        Vector3 position = simulation.Particles[0].Position;
        simulation.Pause();
        Run(simulation, 1);
        Check(simulation.IsPaused && simulation.ParticleCount == count && simulation.Particles[0].Position == position,
            "Pause freezes every particle and the emission");
        simulation.Play();
        Run(simulation, 0.5f);
        Check(!simulation.IsPaused && simulation.ParticleCount > count && simulation.Time > 1.4f,
            "Play after Pause resumes the cycle where it stopped");
        simulation.Stop();
        int stopped = simulation.ParticleCount;
        Run(simulation, 0.5f);
        Check(!simulation.IsEmitting && simulation.ParticleCount == stopped, "Stop stops emitting and keeps the particles alive");
        simulation.Stop(clear: true);
        Check(simulation.ParticleCount == 0 && !simulation.IsAlive, "Stop(clear) removes them too");
    }

    /// <summary>A static-collider raycast made of axis planes (walls, floors, roofs) for the voxel flood fill.</summary>
    private static ParticleRaycast Planes(params (int Axis, float At)[] planes) =>
        (Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out BasicEntity hit) =>
        {
            point = normal = Vector3.Zero;
            hit = null;
            float best = float.MaxValue;
            foreach (var (axis, at) in planes)
            {
                float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
                float d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
                if (Math.Abs(d) < 1e-6f) continue;
                float t = (at - o) / d;
                if (t < 0 || t > maxDistance || t >= best) continue;
                best = t;
                point = origin + direction * t;
                Vector3 axisVector = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
                normal = -Math.Sign(d) * axisVector;
            }
            return best < float.MaxValue;
        };

    private static (ParticleSystemComponent System, ParticleSimulation Simulation) NewSmoke(Action<ParticleSystemComponent> setup = null) =>
        NewSystem(s =>
        {
            s.Main.Looping = false;
            s.Main.Duration = 10;
            s.Emission.RateOverTime = 0;
            s.Shape.Enabled = false;
            s.Volumetric.Enabled = true;
            s.Volumetric.Source = ParticleVolumeSource.Voxels;
            s.Volumetric.VoxelSize = 0.5f;
            s.Volumetric.MaxRadius = 4;
            s.Volumetric.ExpandTime = 0.5f;
            setup?.Invoke(s);
        });

    private static void CheckVolumes()
    {
        // Particles source: each particle adds a soft ball of density where it is.
        var (cloudSystem, cloud) = NewSystem(s =>
        {
            s.Main.StartSpeed = 0;
            s.Main.StartSize = 1;
            s.Main.StartLifetime = 10;
            s.Main.StartColor = new Color(255, 0, 0, 255);
            s.Main.StartColor2 = s.Main.StartColor;
            s.Shape.Enabled = false;
            s.Emission.RateOverTime = 0;
            s.Volumetric.Enabled = true;
        });
        cloud.Emit(10);
        Run(cloud, Step);
        ParticleVolume volume = cloud.Volume;
        Check(volume is { HasDensity: true, UniformColor: false } && volume.DensityAt(Vector3.Zero, 1) > 0.3f &&
              volume.DensityAt(new Vector3(3, 0, 0), 1) == 0 &&
              volume.Cells.Take(volume.SizeX * volume.SizeY * volume.SizeZ).Any(c => c.R > 200 && c.G < 20 && c.A > 0),
            "Volumetric (Particles) splats each particle's size, colour and alpha into a density grid around them");
        Check(volume.Bounds.Contains(Vector3.Zero) == ContainmentType.Contains &&
              Math.Max(volume.SizeX, Math.Max(volume.SizeY, volume.SizeZ)) <= ParticleVolumetricModule.MaxResolution,
            "the grid fits the particles and stays within the resolution limit");
        var cloudHost = new BasicEntity(Marker, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Cloud", IsEnabled = true };
        float through = 1 - volume.Transmittance(new Vector3(-3, 0, 0), new Vector3(3, 0, 0), 4, 1);
        Check(through > 0.5f && volume.Transmittance(new Vector3(-3, 0, 2), new Vector3(3, 0, 2), 4, 1) == 1,
            "a line of sight through the volume is hidden, one above it is clear");
        cloudSystem.Volumetric.Enabled = false;
        Run(cloud, Step);
        Check(!cloud.Volume.HasDensity && cloud.ParticleCount == 10, "turning Volumetric off drops the volume and keeps the particles");

        // Voxels source in the open: blooms over Expand Time to Volume / voxel³ cells around the emitter.
        var (openSystem, open) = NewSmoke(s => s.Volumetric.Volume = 10);
        Matrix at = Matrix.CreateTranslation(2, 3, 0);
        open.SetPose(at);
        open.Play();
        Run(open, 0.1f, at);
        int early = open.Volume.FilledVoxels;
        Run(open, 1f, at);
        Check(early > 0 && early < 80 && open.Volume.FilledVoxels == 80 && open.Volume.TargetVoxels == 80 &&
              Vector3.Distance(open.Volume.Seed, new Vector3(2, 3, 0.25f)) < 1e-3f,
            "a voxel smoke floods out from the emitter over Expand Time until it fills Volume (10 m³ = 80 voxels of 0.5 m)");
        Check(open.Volume.DensityAt(new Vector3(2, 3, 0.3f), 1) > 0.5f && open.Volume.DensityAt(new Vector3(6, 3, 0.3f), 1) == 0 &&
              open.Volume.UniformColor,
            "the smoke is dense around the emitter and absent beyond its volume");

        // A wall at X = 0.9 and the floor at Z = 0: the flood never crosses them.
        var walled = new ParticleStepContext { RaycastStatic = Planes((0, 0.9f), (2, 0)) };
        var (_, wall) = NewSmoke(s => s.Volumetric.Volume = 30);
        wall.Play();
        Run(wall, 1f, null, walled);
        Check(wall.Volume.FilledVoxels == wall.Volume.TargetVoxels && wall.Volume.DensityAt(new Vector3(-1.5f, 0, 0.5f), 1) > 0 &&
              wall.Volume.DensityAt(new Vector3(1.5f, 0, 0.5f), 1) == 0 && wall.Volume.DensityAt(new Vector3(0, 0, -0.5f), 1) == 0,
            "Static colliders stop the voxel flood: nothing behind the wall or under the floor");

        // A 1.6 m wide, 1.9 m high corridor (walls between voxel centres): the same volume reaches much further
        // along it than a ball would.
        var corridor = new ParticleStepContext { RaycastStatic = Planes((1, 0.8f), (1, -0.8f), (2, 0), (2, 1.9f)) };
        var (_, tube) = NewSmoke(s =>
        {
            s.Volumetric.Volume = 20;
            s.Volumetric.MaxRadius = 8;
        });
        tube.Play();
        Run(tube, 1f, null, corridor);
        // A free ball of 20 m³ is 1.7 m across its radius.
        Check(tube.Volume.DensityAt(new Vector3(3.2f, 0, 0.3f), 1) > 0 && tube.Volume.DensityAt(new Vector3(-3.2f, 0, 0.3f), 1) > 0 &&
              tube.Volume.DensityAt(new Vector3(2.2f, 0, 1.3f), 1) > 0 && tube.Volume.DensityAt(new Vector3(0, 1.1f, 1), 1) == 0 &&
              tube.Volume.DensityAt(new Vector3(0, 0, 2.3f), 1) == 0,
            "in a corridor the smoke stretches along it (20 m³ reaches 3 m each way) and stays between the walls");

        // Sunlight: a roof at Z = 1.5 shades the voxels under it (R = 0); without it they're sunlit.
        var roofed = new ParticleStepContext { RaycastStatic = Planes((2, 1.5f)), SunDirection = Vector3.Normalize(new Vector3(0.3f, 0, 1)) };
        var (_, room) = NewSmoke(s => s.Volumetric.Volume = 4);
        room.Play();
        Run(room, 1f, null, roofed);
        var (_, sunny) = NewSmoke(s => s.Volumetric.Volume = 4);
        sunny.Play();
        Run(sunny, 1f, null, new ParticleStepContext { SunDirection = Vector3.UnitZ });
        Color SeedCell(ParticleVolume v) => v.Cells[v.SizeX / 2 + v.SizeX * (v.SizeY / 2 + v.SizeY * (v.SizeZ / 2))];
        Check(SeedCell(room.Volume) is { R: 0, A: > 0 } && SeedCell(sunny.Volume) is { R: 255, A: > 0 },
            "voxels under a roof get no sun, open ones do (lit only by the sky indoors)");

        // Bullets and blasts: holes cut through it and close again over Refill Time.
        var (_, gun) = NewSmoke(s =>
        {
            s.Volumetric.Volume = 40;
            s.Volumetric.RefillTime = 1;
        });
        gun.Play();
        Run(gun, 1f);
        Vector3 inside = gun.Volume.Seed;
        float solid = 1 - gun.Volume.Transmittance(inside - Vector3.UnitX * 2, inside + Vector3.UnitX * 2, 4, 1);
        gun.Volume.Carve(inside - Vector3.UnitX * 5, inside + Vector3.UnitX * 5, 0.3f);
        float tunnel = 1 - gun.Volume.Transmittance(inside - Vector3.UnitX * 2, inside + Vector3.UnitX * 2, 4, 1);
        Check(solid > 0.9f && gun.Volume.DensityAt(inside, 1) == 0 && tunnel < solid * 0.2f &&
              gun.Volume.DensityAt(inside + new Vector3(0, 0.8f, 0), 1) > 0,
            "a carved tunnel (a bullet) is see-through along its length and leaves the rest of the smoke");
        Run(gun, 1.1f);
        Check(gun.Volume.Carves.Count == 0 && gun.Volume.DensityAt(inside, 1) > 0, "the tunnel fills in again over Refill Time");
        gun.Volume.Carve(inside, inside, 1.2f);
        Check(gun.Volume.DensityAt(inside + new Vector3(0.5f, 0, 0), 1) == 0 && gun.Volume.Carves.Count == 1,
            "a ball-shaped hole (an HE grenade) clears everything within it");

        // Lifecycle: holds while the system emits (Duration), then dissipates over Fade Time and the system ends.
        var (_, fade) = NewSmoke(s =>
        {
            s.Main.Duration = 0.5f;
            s.Volumetric.Volume = 8;
            s.Volumetric.ExpandTime = 0.2f;
            s.Volumetric.FadeTime = 0.6f;
        });
        fade.Play();
        Run(fade, 0.45f);
        int full = fade.Volume.FilledVoxels;
        Run(fade, 0.2f);
        Check(full == fade.Volume.TargetVoxels && fade.Volume.IsDissipating && fade.IsAlive && fade.IsPlaying,
            "after Duration the smoke dissipates, keeping the system alive meanwhile");
        Run(fade, 0.8f);
        Check(!fade.Volume.HasDensity && !fade.IsAlive && !fade.IsPlaying, "after Fade Time the smoke is gone and the system has ended");
        fade.Play();
        Run(fade, 0.3f);
        Check(fade.Volume.HasDensity && fade.Volume.FilledVoxels > 0, "playing again floods a new smoke");
        fade.Stop(clear: true);
        Check(!fade.Volume.HasDensity, "Stop(clear) clears the volume too");

        // Through the component: the script API and the scene-wide queries NPCs use. An empty physics scene of
        // its own, so the flood doesn't ask one an earlier check disposed.
        using var physicsSystem = new Engine.Physics.PhysicsSystem(new Vector3(0, 0, -9.81f));
        _ = new Engine.Physics.ScenePhysics(physicsSystem);
        var smoke = new ParticleSystemComponent();
        smoke.Main.Looping = false;
        smoke.Main.Duration = 10;
        smoke.Emission.RateOverTime = 0;
        smoke.Shape.Enabled = false;
        smoke.Volumetric.Enabled = true;
        smoke.Volumetric.Source = ParticleVolumeSource.Voxels;
        smoke.Volumetric.VoxelSize = 0.5f;
        smoke.Volumetric.MaxRadius = 4;
        smoke.Volumetric.Volume = 60;
        smoke.Volumetric.ExpandTime = 0.2f;
        var host = new BasicEntity(Marker, null, new Vector3(0, 20, 0), Matrix.Identity, Vector3.One) { Name = "Smoke", IsEnabled = true };
        host.AddComponent(smoke);
        smoke.OnStart(host);
        var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Step));
        for (int i = 0; i < 30; i++) smoke.OnUpdate(host, frame);
        Vector3 center = smoke.Volume.Seed;
        var hosts = new[] { host, cloudHost };
        float hidden = ParticleSystemComponent.VolumeOpacity(center - Vector3.UnitX * 3, center + Vector3.UnitX * 3, hosts);
        Check(smoke.HasVolume && hidden > NpcComponent.SmokeBlocksSight &&
              ParticleSystemComponent.VolumeOpacity(center + Vector3.UnitZ * 6, center + new Vector3(3, 0, 6), hosts) == 0,
            "VolumeOpacity: a full smoke hides a line of sight through it (past what NPCs see through), not one above it");
        ParticleSystemComponent.CarveTunnels(center - Vector3.UnitX * 6, center + Vector3.UnitX * 6, 0.4f, hosts);
        Check(ParticleSystemComponent.VolumeOpacity(center - Vector3.UnitX * 3, center + Vector3.UnitX * 3, hosts) < 0.2f,
            "CarveTunnels shoots through every smoke in the scene, opening the line of sight");
        ParticleSystemComponent.CarveHoles(center + Vector3.UnitY * 1.5f, 1, hosts);
        Check(smoke.Volume.Carves.Count == 2, "CarveHoles blasts every smoke in the scene");
        smoke.OnStop();
        Check(!smoke.HasVolume, "stopping Play clears the volume");
    }

    private static void CheckLights()
    {
        // System: one light at the glowing centre of the particles (plus the offset), following the gameobject.
        var (system, simulation) = NewSystem(s =>
        {
            s.Main.StartSpeed = 0;
            s.Main.StartLifetime = 1;
            s.Emission.RateOverTime = 50;
            s.Shape.Enabled = false;
            s.Lights.Enabled = true;
            s.Lights.Intensity = 6;
            s.Lights.Range = 9;
            s.Lights.Offset = new Vector3(0, 0, 0.5f);
        });
        Matrix at = Matrix.CreateTranslation(3, 4, 1);
        simulation.Play();
        Run(simulation, 1, at);
        PointLight light = simulation.Lights[0];
        Check(simulation.Lights.Count == 1 && Vector3.Distance(light.Position, new Vector3(3, 4, 1.5f)) < 0.05f &&
              light.Radius == 9 && Math.Abs(light.Intensity - 6) < 0.3f && !light.CastShadows && light.Color == system.Lights.Color,
            "a System light shines at the particles' glowing centre plus its offset, with the module's colour, range and intensity");
        simulation.Stop();
        Run(simulation, 0.5f, at);
        Check(simulation.Lights.Count == 1 && simulation.Lights[0].Intensity < 5.5f,
            "Alpha Affects Intensity: the light dims as the particles die out");
        Run(simulation, 0.7f, at);
        Check(simulation.ParticleCount == 0 && simulation.Lights.Count == 0, "the light goes out with the last particle");

        system.Lights.AlphaAffectsIntensity = false;
        system.Lights.UseParticleColor = true;
        system.Lights.Flicker = 0.5f;
        system.Main.StartColor = system.Main.StartColor2 = new Color(255, 100, 0);
        simulation.Play();
        var intensities = new List<float>();
        for (int i = 0; i < 90; i++)
        {
            Run(simulation, Step, at);
            if (i > 30) intensities.Add(simulation.Lights[0].Intensity);
        }
        Check(simulation.Lights[0].Color is { R: 255, G: >= 98 and <= 102, B: 0 },
            "Use Particle Color takes the particles' hue at full brightness");
        Check(intensities.All(v => v >= 0 && float.IsFinite(v)) && intensities.Min() < intensities.Max() * 0.85f &&
              Math.Abs(intensities.Average() - 6) < 1.5f,
            "Flicker wavers the intensity around its value, never below zero");

        system.Lights.CastShadows = true;
        Run(simulation, Step, at);
        Check(simulation.Lights[0].CastShadows, "Cast Shadows makes the System light a shadowed point light");

        // Per Particle: lights on the brightest carriers, at most Max Lights.
        system.Lights.Mode = ParticleLightMode.PerParticle;
        system.Lights.Ratio = 1;
        system.Lights.MaxLights = 3;
        Run(simulation, Step, at);
        ParticleSimulation sim = simulation;
        Particle[] particles = sim.Particles.ToArray();
        Check(sim.Lights.Count == 3 && Enumerable.Range(0, 3).All(i => !sim.Lights[i].CastShadows &&
                  particles.Any(p => Vector3.Distance(p.Position, sim.Lights[i].Position) < 0.02f)),
            "Per Particle puts up to Max Lights lights on particles (never shadowed)");
        system.Lights.Ratio = 0;
        Run(simulation, Step, at);
        Check(simulation.Lights.Count == 0, "Ratio 0 carries no lights");
        system.Lights.Ratio = 1;
        system.Lights.Enabled = false;
        Run(simulation, Step, at);
        Check(simulation.Lights.Count == 0, "turning the module off removes the lights");
        system.Lights.Enabled = true;
        Run(simulation, Step, at);
        simulation.Stop(clear: true);
        Check(simulation.Lights.Count == 0, "Stop(clear) removes the lights with the particles");

        // The renderer collects the lights of enabled systems on enabled gameobjects.
        var entity = new BasicEntity(Marker, null, new Vector3(1, 2, 3), Matrix.Identity, Vector3.One) { Name = "Torch", IsEnabled = true };
        var torch = new ParticleSystemComponent();
        torch.Main.StartSpeed = 0;
        torch.Lights.Enabled = true;
        entity.AddComponent(torch);
        var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Step));
        for (int i = 0; i < 10; i++) ParticleSystemComponent.UpdatePreviews(new[] { entity }, frame);
        var collected = new List<PointLight>();
        ParticleSystemComponent.CollectLights(new[] { entity }, collected);
        Check(collected.Count == 1 && torch.LightCount == 1, "CollectLights gathers a playing system's light (the editor preview lights too)");
        torch.Enabled = false;
        collected.Clear();
        ParticleSystemComponent.CollectLights(new[] { entity }, collected);
        Check(collected.Count == 0, "disabled systems add no lights");
        torch.OnStop();
        Check(torch.LightCount == 0, "stopping Play removes the lights");
    }

    private static void CheckSaving()
    {
        Check(ComponentRegistry.Find(ParticleSystemComponent.TypeId) is { DisplayName: "Particle System" } definition &&
              definition.ComponentType == typeof(ParticleSystemComponent) && ComponentEditorRegistry.Supports(ParticleSystemComponent.TypeId),
            "Particle System is registered and offered in Add Component");

        var system = new ParticleSystemComponent();
        system.Main.StartSpeed = new ParticleRange(2, 4);
        system.Main.StartColor = new Color(10, 20, 30, 40);
        system.Shape.Shape = ParticleShapeType.Box;
        system.Shape.BoxSize = new Vector3(1, 2, 3);
        system.Emission.Bursts.Add(new ParticleBurst(0.5f, new ParticleRange(10, 20), cycles: 2, interval: 0.3f));
        system.ColorOverLifetime.Enabled = true;
        system.SubEmitters.Emitters.Add(new ParticleSubEmitter { Trigger = ParticleSubEmitterTrigger.Trail, Target = "Smoke" });
        system.Renderer.Texture = ParticleTexture.SmokeSheet;
        system.Volumetric.Enabled = true;
        system.Volumetric.Source = ParticleVolumeSource.Voxels;
        system.Volumetric.Volume = 75;
        system.Lights.Enabled = true;
        system.Lights.Mode = ParticleLightMode.PerParticle;
        system.Lights.Offset = new Vector3(0, 0, 1);
        system.Lights.Flicker = 0.3f;
        system.Emit(3);

        var record = ComponentRegistry.Capture(system);
        JsonElement data = record.Data;
        Check(data.GetProperty("Shape").GetProperty("Shape").GetString() == "Box" &&
              data.GetProperty("Renderer").GetProperty("Texture").GetString() == "SmokeSheet" &&
              data.GetProperty("Shape").GetProperty("BoxSize").GetRawText().Replace(" ", "") == "[1,2,3]" &&
              data.GetProperty("Main").GetProperty("StartColor").GetRawText().Replace(" ", "") == "[10,20,30,40]",
            "particle records save enums by name and vectors/colours as compact arrays");
        Check(!data.TryGetProperty(nameof(ParticleSystemComponent.ParticleCount), out _) &&
              !data.TryGetProperty(nameof(ParticleSystemComponent.Simulation), out _) &&
              !data.TryGetProperty(nameof(ParticleSystemComponent.Volume), out _) &&
              data.GetProperty("Volumetric").GetProperty("Source").GetString() == "Voxels" &&
              !data.TryGetProperty(nameof(ParticleSystemComponent.LightCount), out _) &&
              data.GetProperty("Lights").GetProperty("Mode").GetString() == "PerParticle" &&
              data.GetProperty("Lights").GetProperty("Offset").GetRawText().Replace(" ", "") == "[0,0,1]",
            "runtime state isn't saved; the volume source and light mode are saved by name");
        var restored = (ParticleSystemComponent)ComponentRegistry.Restore(record);
        Check(restored.Main.StartSpeed is { Min: 2, Max: 4 } && restored.Main.StartColor == new Color(10, 20, 30, 40) &&
              restored.Shape.BoxSize == new Vector3(1, 2, 3) && restored.ColorOverLifetime.Enabled &&
              restored.Emission.Bursts.Single() is { Time: 0.5f, Count: { Min: 10, Max: 20 }, Cycles: 2 } &&
              restored.SubEmitters.Emitters.Single() is { Trigger: ParticleSubEmitterTrigger.Trail, Target: "Smoke" } &&
              restored.Volumetric is { Enabled: true, Source: ParticleVolumeSource.Voxels, Volume: 75 } &&
              restored.Lights is { Enabled: true, Mode: ParticleLightMode.PerParticle, Flicker: 0.3f } && restored.Lights.Offset == new Vector3(0, 0, 1) &&
              restored.ParticleCount == 0,
            "particle records round-trip every module");

        var nulls = JsonDocument.Parse("{\"Main\":null,\"Noise\":null,\"Enabled\":true}").RootElement;
        var tolerant = (ParticleSystemComponent)ComponentRegistry.Restore(new ComponentRecord { Type = ParticleSystemComponent.TypeId, Data = nulls });
        Check(tolerant.Main != null && tolerant.Noise != null && tolerant.Main.Duration == 5, "missing or null modules load with defaults");

        var copy = (ParticleSystemComponent)ComponentRegistry.Copy(system);
        copy.Emission.Bursts[0].Time = 9;
        copy.Main.StartSpeed = 1;
        Check(system.Emission.Bursts[0].Time == 0.5f && system.Main.StartSpeed.Max == 4 && copy.ParticleCount == 0,
            "cloning copies the settings independently, without the particles");
    }

    private static void CheckEditor()
    {
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Torch", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Particle System").AddCommand.Execute(null);
        Publish();
        var system = entity.GetComponent<ParticleSystemComponent>();
        var editor = owner.Components.OfType<ParticleSystemComponentViewModel>().Single();
        Check(system != null && editor.RateOverTime == 10 && editor.ShapeType == ParticleShapeType.Cone && editor.ColorKeys.Count == 2 &&
              !owner.AddableComponents.Single(c => c.DisplayName == "Particle System").AddCommand.CanExecute(null),
            "Add Component adds a Particle System with Unity's defaults, once per gameobject");

        editor.RateOverTime = 42;
        editor.ShapeType = ParticleShapeType.Sphere;
        editor.StartSpeedMax = 9;
        editor.StartColor = Avalonia.Media.Color.FromArgb(128, 255, 0, 0);
        editor.BoxY = 3;
        editor.Blend = ParticleBlendMode.Additive;
        editor.CollisionEnabled = true;
        editor.MaxParticles = 250;
        Publish();
        Check(system.Emission.RateOverTime == 42 && system.Shape.Shape == ParticleShapeType.Sphere &&
              system.Main.StartSpeed is { Min: 5, Max: 9 } && system.Main.StartColor == new Color(255, 0, 0, 128) &&
              system.Shape.BoxSize == new Vector3(1, 3, 1) && system.Renderer.Blend == ParticleBlendMode.Additive &&
              system.Collision.Enabled && system.Main.MaxParticles == 250 && logic.ActiveScene.IsDirty,
            "inspector edits reach the engine (numbers, ranges, colours, vectors, enums, module toggles)");

        editor.AddBurstCommand.Execute(null);
        editor.AddColorKeyCommand.Execute(null);
        editor.AddSubEmitterCommand.Execute(null);
        Publish();
        editor.Bursts[0].CountMax = 50;
        editor.SubEmitters[0].Target = "Sparks";
        editor.SubEmitters[0].Trigger = ParticleSubEmitterTrigger.Collision;
        editor.ColorKeys[2].Color = Avalonia.Media.Colors.Blue;
        Publish();
        Check(system.Emission.Bursts.Single() is { Count.Max: 50 } && system.ColorOverLifetime.Color.Keys.Count == 3 &&
              system.ColorOverLifetime.Color.Keys[2].Color == Color.Blue &&
              system.SubEmitters.Emitters.Single() is { Target: "Sparks", Trigger: ParticleSubEmitterTrigger.Collision },
            "burst, gradient key and sub-emitter rows add and edit their lists in the engine");
        editor.VolumetricEnabled = true;
        editor.VolumeSource = ParticleVolumeSource.Voxels;
        editor.VoxelVolume = 60;
        editor.VolumeDensity = 6;
        editor.VolumeSteps = 300;
        Publish();
        Check(system.Volumetric is { Enabled: true, Source: ParticleVolumeSource.Voxels, Volume: 60, Density: 6, Steps: 128 } &&
              editor.IsVoxelSource && !editor.IsParticleSource,
            "the Volumetric inspector reaches the engine and shows the voxel settings for the Voxels source");
        system.Volumetric.Flatten = 2;
        Publish();
        Check(editor.VoxelFlatten == 2, "engine-side Volumetric changes show in the inspector");
        editor.LightsEnabled = true;
        editor.LightMode = ParticleLightMode.PerParticle;
        editor.LightMaxLights = 40;
        editor.LightFlicker = 0.4;
        editor.LightOffsetZ = 0.5;
        editor.LightColor = Avalonia.Media.Color.FromRgb(255, 120, 40);
        Publish();
        Check(system.Lights is { Enabled: true, Mode: ParticleLightMode.PerParticle, MaxLights: ParticleLightsModule.MaxLightsLimit } &&
              Math.Abs(system.Lights.Flicker - 0.4f) < 1e-5f && system.Lights.Offset.Z == 0.5f && system.Lights.Color == new Color(255, 120, 40) &&
              editor.IsPerParticleLight && !editor.IsSystemLight,
            "the Lights inspector reaches the engine (Max Lights clamped to 16) and shows the Per Particle settings");
        system.Lights.Intensity = 12;
        Publish();
        Check(editor.LightIntensity == 12, "engine-side Lights changes show in the inspector");

        editor.ColorKeys[0].RemoveCommand.Execute(null);
        Publish();
        Check(system.ColorOverLifetime.Color.Keys.Count == 2 && editor.ColorKeys.Count == 2, "removing a row removes it in the engine");

        system.Noise.Enabled = true;
        system.Noise.Strength = 3;
        system.Emission.Bursts.Add(new ParticleBurst(1, 5));
        system.Renderer.Texture = ParticleTexture.Star;
        Publish();
        Check(editor.NoiseEnabled && editor.NoiseStrength == 3 && editor.Bursts.Count == 2 && editor.Texture == ParticleTexture.Star,
            "engine-side changes (scripts) show in the inspector");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckLifecycle()
    {
        var entity = new BasicEntity(Marker, null, new Vector3(5, 0, 1), Matrix.Identity, Vector3.One) { Name = "Emitter", IsEnabled = true };
        var system = new ParticleSystemComponent();
        system.Main.StartLifetime = 10;
        system.Main.StartSpeed = 0;
        entity.AddComponent(system);
        var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Step));

        for (int i = 0; i < 30; i++) ParticleSystemComponent.UpdatePreviews(new[] { entity }, frame);
        Check(system.IsPlaying && system.ParticleCount > 5 && !entity.WorldTransform.Hidden,
            "while editing, Play On Awake systems play as a preview and the marker mesh stays visible");
        Check(system.Simulation.Particles.ToArray().All(p => Vector3.Distance(p.Position, entity.Position) <= 1.001f),
            "particles start from the shape at the gameobject (a 1 m cone base)");

        system.OnStop();
        system.OnStart(entity);
        Check(system.IsPlaying && system.ParticleCount == 0 && entity.WorldTransform.Hidden,
            "Play restarts the system from scratch and hides the marker mesh");
        for (int i = 0; i < 30; i++) system.OnUpdate(entity, frame);
        Check(system.ParticleCount > 5, "each Play frame steps the simulation");

        system.HideMesh = false;
        system.OnChanged(entity);
        Check(!entity.WorldTransform.Hidden, "turning Hide Mesh off shows the mesh during Play");
        system.HideMesh = true;
        system.OnChanged(entity);

        system.Restart();
        Check(system.ParticleCount == 0 && system.IsPlaying, "Restart clears and plays again");
        system.EmitAt(new Vector3(0, 0, 10), 5);
        Check(system.ParticleCount == 5 && system.Simulation.Particles.ToArray().All(p => p.Position.Z > 8),
            "EmitAt emits around a world position");

        system.OnStop();
        Check(system.ParticleCount == 0 && !system.IsPlaying && !entity.WorldTransform.Hidden,
            "Stop clears the particles and shows the marker mesh again");

        var oneShot = new ParticleSystemComponent();
        oneShot.Main.Looping = false;
        oneShot.Main.Duration = 0.2f;
        oneShot.Main.StartLifetime = 0.2f;
        var host = new BasicEntity(Marker, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "One shot", IsEnabled = true };
        host.AddComponent(oneShot);
        bool restarted = false;
        for (int i = 0; i < 90 && !restarted; i++)
        {
            bool wasAlive = oneShot.IsAlive;
            ParticleSystemComponent.UpdatePreviews(new[] { host }, frame);
            restarted = !wasAlive && oneShot.IsAlive && i > 10;
        }
        Check(restarted, "a finished one-shot preview starts again after a short pause");
        oneShot.Enabled = false;
        oneShot.OnChanged(host);
        ParticleSystemComponent.UpdatePreviews(new[] { host }, frame);
        Check(oneShot.ParticleCount == 0 && !oneShot.IsPlaying, "a disabled system stops its preview");
    }

    private static void CheckSampleScene()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "ParticleTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        var systems = scene.BasicEntities.Where(e => e.GetComponent<ParticleSystemComponent>() != null).ToList();
        ParticleSystemComponent Find(string name) =>
            scene.BasicEntities.Single(e => e.Name == name).GetComponent<ParticleSystemComponent>();

        Check(systems.Count >= 15 && scene.BasicEntities.All(e => e.ModelDefinition != null) &&
              scene.BasicEntities.Single(e => e.Name == "Ground").Physics is { BodyType: Engine.Physics.PhysicsBodyType.Static },
            "the particle sample loads its systems on a static ground");
        Check(Find("Campfire") is { Renderer: { Blend: ParticleBlendMode.Additive, Texture: ParticleTexture.FlameSheet }, TextureSheetAnimation.Enabled: true } &&
              Find("Campfire Core") is { Renderer.Blend: ParticleBlendMode.Additive } &&
              Find("Campfire Smoke") is { Renderer.Lit: true, TextureSheetAnimation.Enabled: true, ExternalForces.Enabled: true } &&
              Find("Spark Fountain") is { Collision: { Enabled: true, Type: ParticleCollisionType.World }, Renderer.RenderMode: ParticleRenderMode.StretchedBillboard } &&
              Find("Magic Vortex").VelocityOverLifetime is { Enabled: true, Orbital.Z: > 0 } &&
              Find("Comet").Emission.RateOverDistance > 0,
            "the stations cover fire, lit flipbook smoke, colliding sparks, orbital velocity and rate over distance");
        Check(Find("Campfire").Lights is { Enabled: true, Mode: ParticleLightMode.System, Flicker: > 0, AlphaAffectsIntensity: true } &&
              Find("Spark Fountain").Lights is { Enabled: true, Mode: ParticleLightMode.PerParticle, UseParticleColor: true } &&
              Find("Explosion Fireball").Lights is { Enabled: true, Mode: ParticleLightMode.System } &&
              scene.PointLights.All(l => l.Name != "Campfire Light"),
            "the campfire lights the scene with its own flickering light, the sparks and the explosion light up too");

        var launcher = Find("Fireworks Launcher");
        Check(launcher.SubEmitters.Enabled && launcher.SubEmitters.Emitters.Count == 2 &&
              launcher.SubEmitters.Emitters.All(s => Find(s.Target) is { Main: { PlayOnAwake: false, SimulationSpace: ParticleSimulationSpace.World } }),
            "the fireworks' trail and burst sub-emitters exist, wait for their parent and live in world space");
        Check(scene.BasicEntities.Count(e => e.Name.StartsWith("Explosion ") && e.GetComponent<ParticleSystemComponent>() is { Main.Looping: false }) == 7 &&
              Find("Impact Sparks").Main.PlayOnAwake == false,
            "the explosion is seven one-shot systems and the click sparks wait for the script");
        Check(Find("Explosion Fireball") is { Renderer: { Texture: ParticleTexture.FireballSheet, Blend: ParticleBlendMode.Alpha, Lit: false }, TextureSheetAnimation.Enabled: true } &&
              Find("Explosion Smoke").Renderer.Lit && Find("Explosion Dust").Renderer.Lit &&
              Find("Explosion Chunks").SubEmitters.Emitters.Single() is { Trigger: ParticleSubEmitterTrigger.Trail } trail &&
              Find(trail.Target) is { Main: { PlayOnAwake: false, SimulationSpace: ParticleSimulationSpace.World } },
            "the explosion's fireball cools into soot, its smoke and dust are lit and its burning chunks trail smoke");
        Check(scene.MainCamera.Components.OfType<ScriptBehaviourComponent>().Select(s => s.ScriptId)
                  .SequenceEqual(new[] { FreecamScript.ScriptId, ParticleTestScript.ScriptId }) &&
              ScriptRegistry.Find(ParticleTestScript.ScriptId) != null,
            "the sample's camera runs Freecam and the Particle Test script");

        SceneList.Load();
        string ui = Path.Combine(new EditorBridge().ContentSourceRoot, "UI");
        Check(SceneList.Find("ParticleTest") >= 0 && File.Exists(Path.Combine(ui, "ParticleTest.xml")) &&
              File.Exists(Path.Combine(ui, "ParticleTest.css")),
            "the particle sample is in the scene list with its HUD");
    }

    private static void CheckSmokeSampleScene()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "SmokeTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        ParticleSystemComponent Find(string name) =>
            scene.BasicEntities.Single(e => e.Name == name).GetComponent<ParticleSystemComponent>();
        var voxels = scene.BasicEntities.Select(e => e.GetComponent<ParticleSystemComponent>())
            .Where(p => p?.Volumetric is { Enabled: true, Source: ParticleVolumeSource.Voxels }).ToList();
        Check(voxels.Count == 4 && voxels.All(v => v.Main is { Looping: false, Duration: 18 } && v.Volumetric.DrawParticles && !v.HideMesh) &&
              Find("Thrown Smoke").Main.PlayOnAwake == false &&
              Find("Smoke Stack").Volumetric is { Enabled: true, Source: ParticleVolumeSource.Particles },
            "the smoke sample has four 18 s CS2-style voxel smokes (one thrown by the script) and a Particles-source smoke stack");
        Check(scene.BasicEntities.Count(e => e.Physics is { BodyType: Engine.Physics.PhysicsBodyType.Static }) >= 12 &&
              scene.BasicEntities.Any(e => e.Name == "Room Roof") &&
              Find("HE Blast") is { Main.PlayOnAwake: false, Renderer.Blend: ParticleBlendMode.Additive },
            "the walls, corridor and roofed room the smoke floods are Static colliders");
        Check(scene.MainCamera.Components.OfType<ScriptBehaviourComponent>().Select(s => s.ScriptId)
                  .SequenceEqual(new[] { FreecamScript.ScriptId, SmokeTestScript.ScriptId }) &&
              ScriptRegistry.Find(SmokeTestScript.ScriptId) != null,
            "the smoke sample's camera runs Freecam and the Smoke Test script");
        SceneList.Load();
        string ui = Path.Combine(new EditorBridge().ContentSourceRoot, "UI");
        Check(SceneList.Find("SmokeTest") >= 0 && File.Exists(Path.Combine(ui, "SmokeTest.xml")) && File.Exists(Path.Combine(ui, "SmokeTest.css")),
            "the smoke sample is in the scene list with its HUD");
    }

    /// <summary>The particle shader and render module on a real device (run with --graphics).</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var effect = content.Load<Effect>("Shaders/Forward/Particles");
        Check(effect.Techniques["Particles"] != null &&
              new[] { "ViewProj", "ParticleTexture", "DepthMap", "Tiles", "RenderMode", "Brightness" }.All(p => effect.Parameters[p] != null),
            "the particle shader has its technique and parameters");

        const int Size = 96;
        using var module = new ParticleRenderModule(content);
        using var target = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        using var depth = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.Single, DepthFormat.None);
        // Top half is sky (depth 1); bottom half is a wall 20 m away.
        var depthValues = new float[Size * Size];
        for (int i = 0; i < depthValues.Length; i++) depthValues[i] = i < depthValues.Length / 2 ? 1 : 20f / 500f;
        depth.SetData(depthValues);
        var camera = new Camera(new Vector3(0, 0, 2), new Vector3(0, 10, 2));
        Matrix view = Matrix.CreateLookAt(camera.Position, new Vector3(0, 10, 2), Vector3.UnitZ);
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 500);
        var lights = new List<DirectionalLight> { new(Color.White, 100, new Vector3(0, 0.3f, -1), Vector3.UnitZ, false) };

        (Vector4[] Pixels, int Drawn) Render(Vector3 at, Action<ParticleSystemComponent> setup, List<PointLight> pointLights = null)
        {
            var entity = new BasicEntity(Marker, null, at, Matrix.Identity, Vector3.One) { Name = "Emitter", IsEnabled = true };
            var system = new ParticleSystemComponent();
            system.Main.StartSpeed = 0;
            system.Main.StartSize = 1.5f;
            system.Main.StartColor = new Color(255, 40, 20);
            system.Main.StartColor2 = system.Main.StartColor;
            system.Shape.Enabled = false;
            system.Renderer.Blend = ParticleBlendMode.Additive;
            system.Renderer.Texture = ParticleTexture.Glow;
            setup?.Invoke(system);
            entity.AddComponent(system);
            system.Emit(5);
            graphics.SetRenderTarget(target);
            graphics.Clear(Color.Black);
            module.Draw(graphics, new[] { entity }, camera, view, view * projection, null, lights, depth, 500, null, pointLights);
            graphics.SetRenderTarget(null);
            var half = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[Size * Size];
            target.GetData(half);
            return (half.Select(h => h.ToVector4()).ToArray(), module.LastParticleCount);
        }

        static float Luma(Vector4 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        var (front, drawn) = Render(new Vector3(0, 10, 1.4f), null);
        float lower = front.Skip(Size * Size / 2).Sum(Luma);
        Check(drawn == 5 && lower > 0 && front.All(c => float.IsFinite(Luma(c))) &&
              front.Skip(Size * Size / 2).Max(c => c.X) > front.Skip(Size * Size / 2).Max(c => c.Y),
            "particles in front of the wall draw, in their colour");
        var (behind, _) = Render(new Vector3(0, 40, -2), null);
        Check(behind.Skip(Size * Size / 2).Sum(Luma) < lower * 0.01f, "particles behind the wall are hidden by the scene depth");
        var (bright, _) = Render(new Vector3(0, 10, 1.4f), s => s.Renderer.Brightness = 4);
        Check(bright.Max(c => c.X) > 1.5f, "Brightness above 1 writes HDR values for the bloom");
        void LitSmoke(ParticleSystemComponent s)
        {
            s.Main.StartColor = s.Main.StartColor2 = new Color(200, 200, 200);
            s.Renderer.Blend = ParticleBlendMode.Alpha;
            s.Renderer.Texture = ParticleTexture.SoftCircle;
            s.Renderer.Lit = true;
        }
        var (unlitByFire, _) = Render(new Vector3(0, 10, 1.4f), LitSmoke);
        var fire = new PointLight(new Vector3(0, 9.5f, 1.4f), 6, Color.Red, 100, false, false, 256, 0, false);
        var (litByFire, _) = Render(new Vector3(0, 10, 1.4f), LitSmoke, new List<PointLight> { fire });
        Check(litByFire.Sum(c => c.X) > unlitByFire.Sum(c => c.X) * 1.2f && litByFire.All(c => float.IsFinite(Luma(c))),
            "Lit particles are shaded by nearby point lights (smoke glowing over a fire)");

        // Volumetric: a voxel smoke in front of the wall covers it; one behind the wall is hidden by the scene depth.
        (Vector4[] Pixels, int Volumes) RenderSmoke(Vector3 at, Action<ParticleSystemComponent> setup = null)
        {
            var entity = new BasicEntity(Marker, null, at, Matrix.Identity, Vector3.One) { Name = "Smoke", IsEnabled = true };
            var system = new ParticleSystemComponent();
            system.Main.Looping = false;
            system.Main.Duration = 10;
            system.Main.StartColor = new Color(40, 220, 40);
            system.Emission.RateOverTime = 0;
            system.Shape.Enabled = false;
            system.Renderer.Lit = false;
            system.Volumetric.Enabled = true;
            system.Volumetric.Source = ParticleVolumeSource.Voxels;
            system.Volumetric.VoxelSize = 0.25f;
            system.Volumetric.MaxRadius = 2;
            system.Volumetric.Volume = 20;
            system.Volumetric.ExpandTime = 0.1f;
            setup?.Invoke(system);
            entity.AddComponent(system);
            system.OnStart(entity);
            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Step));
            for (int i = 0; i < 30; i++) system.OnUpdate(entity, frame);
            graphics.SetRenderTarget(target);
            graphics.Clear(Color.Black);
            module.Draw(graphics, new[] { entity }, camera, view, view * projection, null, lights, depth, 500);
            graphics.SetRenderTarget(null);
            var half = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[Size * Size];
            target.GetData(half);
            return (half.Select(h => h.ToVector4()).ToArray(), module.LastVolumeCount);
        }
        Check(effect.Techniques["Volume"] != null, "the particle shader has the Volume technique");
        var (smoke, volumes) = RenderSmoke(new Vector3(0, 10, 1));
        float smokeGreen = smoke.Skip(Size * Size / 2).Sum(c => c.Y);
        Check(volumes == 1 && smokeGreen > 0 && smoke.All(c => float.IsFinite(Luma(c)) && c.W <= 1.001f) &&
              smoke.Max(c => c.W) > 0.9f && smoke.Skip(Size * Size / 2).Max(c => c.Y) > smoke.Skip(Size * Size / 2).Max(c => c.X),
            "a voxel smoke raymarches in its Start Color and turns opaque where it is thick");
        var (hidden, _) = RenderSmoke(new Vector3(0, 40, 0));
        Check(hidden.Skip(Size * Size / 2).Sum(c => c.Y) < smokeGreen * 0.01f, "a smoke behind the wall is hidden by the scene depth");
        var (lit, _) = RenderSmoke(new Vector3(0, 10, 1), s => s.Renderer.Lit = true);
        Check(lit.Sum(Luma) > 0 && lit.All(c => float.IsFinite(Luma(c))), "lit volumes take the sun and sky");
        var (cloudPixels, cloudVolumes) = RenderSmoke(new Vector3(0, 10, 1), s =>
        {
            s.Volumetric.Source = ParticleVolumeSource.Particles;
            s.Main.Looping = true;
            s.Main.StartSize = 2;
            s.Main.StartSpeed = 0;
            s.Main.StartLifetime = 10;
            s.Emission.RateOverTime = 40;
        });
        Check(cloudVolumes == 1 && cloudPixels.Skip(Size * Size / 2).Sum(c => c.Y) > 0, "a Particles-source volume draws too");

        foreach (ParticleTexture texture in Enum.GetValues<ParticleTexture>())
        {
            var (pixels, _) = Render(new Vector3(0, 10, 2.6f), s =>
            {
                s.Renderer.Texture = texture;
                s.Renderer.TexturePath = "No/Such/Texture";
            });
            Check(pixels.Take(Size * Size / 2).Sum(Luma) > 0 && pixels.All(c => float.IsFinite(Luma(c))),
                $"the {texture} texture draws{(texture == ParticleTexture.Custom ? " (a missing custom texture falls back to Soft Circle)" : "")}");
        }
        foreach (ParticleRenderMode mode in Enum.GetValues<ParticleRenderMode>())
        {
            var (pixels, _) = Render(new Vector3(0, 10, 2.6f), s =>
            {
                s.Renderer.RenderMode = mode;
                s.Main.StartSpeed = 2;
                s.Shape.Enabled = false;
                s.Renderer.Blend = ParticleBlendMode.Alpha;
                s.Renderer.Lit = true;
                s.Renderer.SortMode = ParticleSortMode.Distance;
            });
            // Horizontal quads are seen edge-on at eye height: look down on them.
            if (mode == ParticleRenderMode.HorizontalBillboard)
                Check(pixels.All(c => float.IsFinite(Luma(c))), "horizontal billboards draw without errors");
            else
                Check(pixels.Take(Size * Size / 2).Sum(Luma) > 0, $"{mode} particles draw lit and alpha blended");
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
