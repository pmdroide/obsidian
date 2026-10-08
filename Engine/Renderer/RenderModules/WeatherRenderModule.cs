using Engine.Entities;
using Engine.Logic;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Renderer.RenderModules;

/// <summary>
/// Draws the scene's weather (rain, sandstorm, snow) from <see cref="EnvironmentSettings"/>: a distance haze,
/// then a box of GPU-animated particles around the camera. Runs after water, before TAA, on the HDR image.
/// The particle buffers are built once; intensity picks how many of them are drawn.
/// </summary>
public sealed class WeatherRenderModule : IDisposable
{
    // Speed of each particle group relative to the average, so the particles don't fall in lockstep.
    public static readonly float[] GroupSpeeds = { 0.82f, 0.94f, 1.06f, 1.18f };
    // Offsets and the sway clock are wrapped so they never lose float precision.
    private const double TimePeriod = 3600;

    private readonly Effect _shader;
    private VertexBuffer _vertices;
    private IndexBuffer _indices;
    private int _capacity;
    private readonly double[] _offsets = new double[GroupSpeeds.Length * 3];
    private double _time;

    private struct ParticleVertex : IVertexType
    {
        public Vector4 Seed;
        public Vector2 Corner;

        public static readonly VertexDeclaration Declaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.Position, 0),
            new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

        VertexDeclaration IVertexType.VertexDeclaration => Declaration;
    }

    public WeatherRenderModule(ContentManager content) =>
        _shader = content.Load<Effect>("Shaders/Forward/Weather");

    /// <summary>Particles drawn this frame (0 when the weather is off); for tests and stats.</summary>
    public int LastParticleCount { get; private set; }

    /// <summary>Accumulated fall and wind of one speed group, wrapped to the profile's box.</summary>
    public Vector3 GroupOffset(int group) =>
        new Vector3((float)_offsets[group * 3], (float)_offsets[group * 3 + 1], (float)_offsets[group * 3 + 2]);

    /// <summary>Advance the particles; called once per frame, also while the weather is off so it resumes smoothly.</summary>
    public void Update(EnvironmentSettings settings, double elapsedSeconds)
    {
        double delta = Math.Clamp(elapsedSeconds, 0, 0.25);
        _time = (_time + delta) % TimePeriod;
        WeatherProfile profile = WeatherProfile.For(settings?.Weather ?? WeatherType.None);
        if (profile == null) return;
        Vector3 velocity = profile.Velocity(settings);
        for (int g = 0; g < GroupSpeeds.Length; g++)
        {
            _offsets[g * 3] = Wrap(_offsets[g * 3] + velocity.X * GroupSpeeds[g] * delta, profile.Box.X);
            _offsets[g * 3 + 1] = Wrap(_offsets[g * 3 + 1] + velocity.Y * GroupSpeeds[g] * delta, profile.Box.Y);
            _offsets[g * 3 + 2] = Wrap(_offsets[g * 3 + 2] + velocity.Z * GroupSpeeds[g] * delta, profile.Box.Z);
        }
    }

    private static double Wrap(double value, double period) => ((value % period) + period) % period;

    /// <param name="depthMap">Linear G-buffer depth (view Z / -farClip): fades particles into geometry, thickens the haze.</param>
    public void Draw(GraphicsDevice graphics, EnvironmentSettings settings, Camera camera, Matrix view,
        Matrix viewProjection, TextureCube environment, List<DirectionalLight> lights, Texture2D depthMap,
        float farClip, FullScreenTriangle fullScreenTriangle)
    {
        LastParticleCount = 0;
        if (!WeatherProfile.IsActive(settings) || camera == null) return;
        WeatherProfile profile = WeatherProfile.For(settings.Weather);
        float intensity = WeatherProfile.Intensity(settings);
        EnsureBuffers(graphics);

        Matrix inverseView = Matrix.Invert(view);
        Set("ViewProj", viewProjection);
        Set("View", view);
        Set("InverseViewProjection", Matrix.Invert(viewProjection));
        Set("CameraPosition", camera.Position);
        Set("CameraRight", inverseView.Right);
        Set("CameraUp", inverseView.Up);
        bool hasDepth = depthMap != null && farClip > 0;
        Set("HasDepth", hasDepth);
        _shader.Parameters["DepthMap"]?.SetValue(hasDepth ? depthMap : null);
        Set("FarClip", farClip);
        Set("HasEnvironment", environment != null);
        _shader.Parameters["EnvironmentMap"]?.SetValue(environment);
        var light = lights?.FirstOrDefault(l => l.IsEnabled && l.Intensity > 0);
        Set("LightDirection", light == null ? Vector3.UnitZ : -light.Direction);
        // Same 0.1 scale the deferred light shaders apply, so weather matches the lit scene.
        Set("LightColor", light == null ? Vector3.Zero : light.ColorV3 * light.Intensity * 0.1f);

        graphics.DepthStencilState = DepthStencilState.None;
        graphics.RasterizerState = RasterizerState.CullNone;
        graphics.BlendState = BlendState.NonPremultiplied;

        // Haze first, so the particles in front of the camera stay crisp on top of it.
        float strength = intensity * Math.Clamp(float.IsFinite(settings.WeatherHaze) ? settings.WeatherHaze : 0, 0, 1);
        if (strength > 0 && fullScreenTriangle != null)
        {
            Set("HazeColor", Linear(profile.HazeColor));
            Set("HazeAmount", profile.HazeMax * Math.Min(1, strength / WeatherProfile.ReferenceHazeStrength));
            Set("HazeDensity", strength / (WeatherProfile.ReferenceHazeStrength * profile.HazeDistance));
            _shader.CurrentTechnique = _shader.Techniques["Haze"];
            _shader.CurrentTechnique.Passes[0].Apply();
            fullScreenTriangle.Draw(graphics);
        }

        int count = Math.Min(_capacity, (int)Math.Round(profile.MaxParticles * intensity));
        if (count > 0)
        {
            var offsets = new Vector3[GroupSpeeds.Length];
            for (int g = 0; g < offsets.Length; g++) offsets[g] = GroupOffset(g);
            Set("Box", profile.Box);
            _shader.Parameters["Offsets"]?.SetValue(offsets);
            Set("GroupSpeed", new Vector4(GroupSpeeds[0], GroupSpeeds[1], GroupSpeeds[2], GroupSpeeds[3]));
            Set("Velocity", profile.Velocity(settings));
            Set("Time", (float)_time);
            Set("IsStreak", profile.StreakSeconds > 0);
            Set("StreakSeconds", profile.StreakSeconds);
            Set("Size", profile.Size);
            Set("Sway", profile.Sway);
            float fov = camera.FieldOfView > 0 ? camera.FieldOfView : MathHelper.PiOver4;
            Set("PixelAngle", 2 * (float)Math.Tan(fov / 2) / Math.Max(1, graphics.Viewport.Height));
            Set("Opacity", profile.Opacity);
            Set("Tint", Linear(profile.Tint));
            Set("LightResponse", profile.LightResponse);
            _shader.CurrentTechnique = _shader.Techniques["Particles"];
            _shader.CurrentTechnique.Passes[0].Apply();
            graphics.SetVertexBuffer(_vertices);
            graphics.Indices = _indices;
            graphics.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, count * 2);
            LastParticleCount = count;
        }

        _shader.Parameters["DepthMap"]?.SetValue((Texture2D)null);
        graphics.DepthStencilState = DepthStencilState.Default;
        graphics.RasterizerState = RasterizerState.CullCounterClockwise;
        graphics.BlendState = BlendState.Opaque;
    }

    private void EnsureBuffers(GraphicsDevice graphics)
    {
        if (_vertices != null) return;
        _capacity = Math.Max(WeatherProfile.Rain.MaxParticles,
            Math.Max(WeatherProfile.Sandstorm.MaxParticles, WeatherProfile.Snow.MaxParticles));
        // Fixed seed: the same particle layout every run. Seeds are shuffled, so any prefix is evenly spread.
        var random = new Random(1337);
        var vertices = new ParticleVertex[_capacity * 4];
        var indices = new int[_capacity * 6];
        Vector2[] corners = { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) };
        for (int i = 0; i < _capacity; i++)
        {
            var seed = new Vector4((float)random.NextDouble(), (float)random.NextDouble(),
                (float)random.NextDouble(), (float)random.NextDouble());
            for (int c = 0; c < 4; c++) vertices[i * 4 + c] = new ParticleVertex { Seed = seed, Corner = corners[c] };
            int v = i * 4, n = i * 6;
            indices[n] = v; indices[n + 1] = v + 1; indices[n + 2] = v + 2;
            indices[n + 3] = v; indices[n + 4] = v + 2; indices[n + 5] = v + 3;
        }
        _vertices = new VertexBuffer(graphics, ParticleVertex.Declaration, vertices.Length, BufferUsage.WriteOnly);
        _vertices.SetData(vertices);
        _indices = new IndexBuffer(graphics, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
        _indices.SetData(indices);
    }

    private static Vector3 Linear(Color color)
    {
        Vector3 c = color.ToVector3();
        return new Vector3((float)Math.Pow(c.X, 2.2), (float)Math.Pow(c.Y, 2.2), (float)Math.Pow(c.Z, 2.2));
    }

    // The compiler strips unused parameters; a missing one must not break the frame.
    private void Set(string name, Matrix value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, Vector3 value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, Vector4 value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, float value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, bool value) => _shader.Parameters[name]?.SetValue(value);

    public void Dispose()
    {
        _vertices?.Dispose();
        _indices?.Dispose();
        _vertices = null;
        _indices = null;
        _shader.Dispose();
    }
}
