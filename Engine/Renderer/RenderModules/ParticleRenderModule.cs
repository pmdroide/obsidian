using Engine.Components;
using Engine.Entities;
using Engine.Particles;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;
using GameComponent = Engine.Components.GameComponent;

namespace Engine.Renderer.RenderModules;

/// <summary>
/// Draws every <see cref="ParticleSystemComponent"/> of the scene: one instanced quad per particle, after water
/// and before the weather, on the HDR image (so bright additive particles bloom). Systems are drawn far to near;
/// within one, particles follow its Sort Mode. All particles of a frame go into one instance buffer.
/// Volumetric systems upload their <see cref="ParticleVolume"/> into a 3D texture and are raymarched by a
/// full-screen pass scissored to the volume's box on screen: density with drifting detail noise, minus bullet
/// holes, lit by the sun (marched towards it through the volume) and the sky.
/// </summary>
public sealed class ParticleRenderModule : IDisposable
{
    private const int MaxInstances = 1 << 20;

    private readonly Effect _shader;
    private ParticleTextures _textures;
    private VertexBuffer _quad;
    private IndexBuffer _indices;
    private DynamicVertexBuffer _instances;
    private ParticleInstance[] _data = Array.Empty<ParticleInstance>();
    private float[] _sortKeys = Array.Empty<float>();
    private int[] _sortOrder = Array.Empty<int>();
    private readonly List<(ParticleSystemComponent System, float Distance, int Offset, int Count, bool Volume)> _batches = new();

    // Volumes
    private readonly Dictionary<ParticleVolume, (Texture3D Texture, int Version)> _volumeTextures = new();
    private readonly HashSet<ParticleVolume> _drawnVolumes = new();
    private readonly List<ParticleVolume> _staleVolumes = new();
    private readonly Vector4[] _carveFrom = new Vector4[ParticleVolumetricModule.MaxCarves];
    private readonly Vector4[] _carveTo = new Vector4[ParticleVolumetricModule.MaxCarves];
    // Lit particles: the point lights nearest each system (MAX_PARTICLE_POINT_LIGHTS in Particles.fx).
    private const int MaxLitPointLights = 4;
    private readonly Vector4[] _litLightPosition = new Vector4[MaxLitPointLights];
    private readonly Vector4[] _litLightColor = new Vector4[MaxLitPointLights];
    private readonly float[] _litLightScore = new float[MaxLitPointLights];
    private Texture3D _noise;
    private FullScreenTriangle _fullScreenTriangle;
    private int _frame;

    private static readonly BlendState PremultipliedBlend = new()
    {
        Name = "ParticleVolume.Premultiplied",
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };
    private static readonly BlendState AddBlend = new()
    {
        Name = "ParticleVolume.Additive",
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
    };
    private static readonly RasterizerState Scissored = new() { CullMode = CullMode.None, ScissorTestEnable = true };

    private struct QuadVertex : IVertexType
    {
        public Vector2 Corner;

        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

        VertexDeclaration IVertexType.VertexDeclaration => Declaration;
    }

    private struct ParticleInstance : IVertexType
    {
        public Vector3 Center;
        public Vector4 Color;
        public Vector4 Shape;
        public Vector4 Velocity;

        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 1),
            new VertexElement(12, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
            new VertexElement(28, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
            new VertexElement(44, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4));

        VertexDeclaration IVertexType.VertexDeclaration => Declaration;
    }

    public ParticleRenderModule(ContentManager content) =>
        _shader = content.Load<Effect>("Shaders/Forward/Particles");

    /// <summary>Particles drawn last frame, for stats and tests.</summary>
    public int LastParticleCount { get; private set; }
    /// <summary>Particle systems with particles drawn last frame.</summary>
    public int LastSystemCount { get; private set; }
    /// <summary>Volumetric systems raymarched last frame.</summary>
    public int LastVolumeCount { get; private set; }

    /// <param name="depthMap">Linear G-buffer depth (view Z / -farClip): hides particles behind geometry and softens them into it.</param>
    /// <param name="extraSystems">Systems without a gameobject (water splash droplets); they sort by their simulation's pose.</param>
    /// <param name="pointLights">Point lights that shade Lit particles (scene lights plus particle Lights modules); optional.</param>
    public void Draw(GraphicsDevice graphics, IReadOnlyList<BasicEntity> entities, Camera camera, Matrix view,
        Matrix viewProjection, TextureCube environment, List<DirectionalLight> lights, Texture2D depthMap, float farClip,
        IReadOnlyList<ParticleSystemComponent> extraSystems = null, IReadOnlyList<PointLight> pointLights = null)
    {
        LastParticleCount = 0;
        LastSystemCount = 0;
        LastVolumeCount = 0;
        _frame++;
        _drawnVolumes.Clear();
        if (entities == null || camera == null)
        {
            ReleaseUnusedVolumes();
            return;
        }

        // Gather the systems with something to draw, far to near.
        _batches.Clear();
        int total = 0;
        for (int i = 0; i < entities.Count; i++)
        {
            BasicEntity entity = entities[i];
            if (entity == null || !entity.IsEnabled) continue;
            foreach (GameComponent component in entity.Components)
            {
                if (component is not ParticleSystemComponent { Enabled: true } system) continue;
                bool volume = system.HasVolume;
                // A volumetric system draws its billboards too only when asked (Draw Particles).
                int count = system.Volumetric.Enabled && !system.Volumetric.DrawParticles ? 0 : system.ParticleCount;
                count = Math.Max(0, Math.Min(count, MaxInstances - total));
                if (count == 0 && !volume) continue;
                // A volume sorts by its own centre (a voxel smoke stays where it bloomed).
                BoundingBox box = volume ? system.Volume.Bounds : default;
                Vector3 at = volume ? (box.Min + box.Max) * 0.5f : entity.Position;
                _batches.Add((system, Vector3.DistanceSquared(camera.Position, at), 0, count, volume));
                total += count;
            }
        }
        if (extraSystems != null)
            for (int i = 0; i < extraSystems.Count; i++)
            {
                ParticleSystemComponent system = extraSystems[i];
                int count = Math.Max(0, Math.Min(system?.ParticleCount ?? 0, MaxInstances - total));
                if (count == 0) continue;
                Vector3 at = system.Simulation?.World.Translation ?? Vector3.Zero;
                _batches.Add((system, Vector3.DistanceSquared(camera.Position, at), 0, count, false));
                total += count;
            }
        if (_batches.Count == 0)
        {
            ReleaseUnusedVolumes();
            return;
        }
        _batches.Sort((a, b) => b.Distance.CompareTo(a.Distance));

        EnsureBuffers(graphics, Math.Max(total, 1));
        int offset = 0;
        for (int b = 0; b < _batches.Count; b++)
        {
            var batch = _batches[b];
            if (batch.Count > 0) Fill(batch.System, camera.Position, offset, batch.Count);
            _batches[b] = (batch.System, batch.Distance, offset, batch.Count, batch.Volume);
            offset += batch.Count;
        }
        if (total > 0) _instances.SetData(_data, 0, total, SetDataOptions.Discard);

        Matrix inverseView = Matrix.Invert(view);
        Set("ViewProj", viewProjection);
        Set("View", view);
        Set("CameraPosition", camera.Position);
        Set("CameraRight", inverseView.Right);
        Set("CameraUp", inverseView.Up);
        float fov = camera.FieldOfView > 0 ? camera.FieldOfView : MathHelper.PiOver4;
        Set("PixelAngle", 2 * (float)Math.Tan(fov / 2) / Math.Max(1, graphics.Viewport.Height));
        bool hasDepth = depthMap != null && farClip > 0;
        Set("HasDepth", hasDepth);
        _shader.Parameters["DepthMap"]?.SetValue(hasDepth ? depthMap : null);
        Set("FarClip", farClip);
        Set("HasEnvironment", environment != null);
        _shader.Parameters["EnvironmentMap"]?.SetValue(environment);
        DirectionalLight light = lights?.FirstOrDefault(l => l.IsEnabled && l.Intensity > 0);
        Set("LightDirection", light == null ? Vector3.UnitZ : -light.Direction);
        // Same 0.1 scale the deferred light shaders apply, so lit particles match the scene.
        Set("LightColor", light == null ? Vector3.Zero : light.ColorV3 * light.Intensity * 0.1f);

        graphics.DepthStencilState = DepthStencilState.None;
        _textures ??= new ParticleTextures(graphics);
        Rectangle scissor = graphics.ScissorRectangle;

        foreach (var batch in _batches)
        {
            ParticleSystemComponent system = batch.System;
            ParticleRendererModule renderer = system.Renderer;
            if (batch.Volume) DrawVolume(graphics, system, inverseView, viewProjection);
            if (batch.Count == 0) continue;
            graphics.RasterizerState = RasterizerState.CullNone;
            graphics.Indices = _indices;
            _shader.CurrentTechnique = _shader.Techniques["Particles"];
            graphics.BlendState = renderer.Blend == ParticleBlendMode.Additive ? BlendState.Additive : BlendState.NonPremultiplied;
            _shader.Parameters["ParticleTexture"]?.SetValue(_textures.For(renderer));
            ParticleTextureSheetModule sheet = system.TextureSheetAnimation;
            Set("Tiles", sheet.Enabled ? new Vector2(sheet.ClampedTilesX, sheet.ClampedTilesY) : Vector2.One);
            Set("RenderMode", (float)renderer.RenderMode);
            Set("LengthScale", Finite(renderer.LengthScale, 2, 0, 1000));
            Set("SpeedScale", Finite(renderer.SpeedScale, 0, 0, 1000));
            Set("Brightness", Finite(renderer.Brightness, 1, 0, 1000));
            Set("Lit", renderer.Lit);
            if (renderer.Lit) SetPointLights(system, pointLights);
            Set("SoftParticles", Finite(renderer.SoftParticles, 0.3f, 0, 100));
            Set("CameraFade", Finite(renderer.CameraFade, 0.5f, 0, 100));
            _shader.CurrentTechnique.Passes[0].Apply();
            graphics.SetVertexBuffers(new VertexBufferBinding(_quad, 0, 0), new VertexBufferBinding(_instances, batch.Offset, 1));
            graphics.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, 2, batch.Count);
            LastParticleCount += batch.Count;
            LastSystemCount++;
        }

        _shader.CurrentTechnique = _shader.Techniques["Particles"];
        _shader.Parameters["DepthMap"]?.SetValue((Texture2D)null);
        _shader.Parameters["VolumeTexture"]?.SetValue((Texture3D)null);
        graphics.ScissorRectangle = scissor;
        graphics.SetVertexBuffer(null);
        graphics.DepthStencilState = DepthStencilState.Default;
        graphics.RasterizerState = RasterizerState.CullCounterClockwise;
        graphics.BlendState = BlendState.Opaque;
        ReleaseUnusedVolumes();
    }

    // The (up to) four point lights that shine most on a Lit system, judged from its emitter.
    private void SetPointLights(ParticleSystemComponent system, IReadOnlyList<PointLight> pointLights)
    {
        int count = 0;
        Vector3 at = system.Owner?.Position ?? system.Simulation?.World.Translation ?? Vector3.Zero;
        // Smoke drifts away from its emitter: lights a few metres beyond their radius still count.
        const float Slack = 4f;
        if (pointLights != null)
            for (int i = 0; i < pointLights.Count; i++)
            {
                PointLight light = pointLights[i];
                if (light == null || !light.IsEnabled || light is SpotLight || !(light.Intensity > 0) || !(light.Radius > 0)) continue;
                float distance = Vector3.Distance(light.Position, at);
                if (distance > light.Radius + Slack) continue;
                float score = light.Intensity * light.Radius / (1 + distance * distance);
                int slot = count < MaxLitPointLights ? count++ : MaxLitPointLights;
                if (slot == MaxLitPointLights && score <= _litLightScore[MaxLitPointLights - 1]) continue;
                if (slot == MaxLitPointLights) slot--;
                while (slot > 0 && _litLightScore[slot - 1] < score)
                {
                    _litLightScore[slot] = _litLightScore[slot - 1];
                    _litLightPosition[slot] = _litLightPosition[slot - 1];
                    _litLightColor[slot] = _litLightColor[slot - 1];
                    slot--;
                }
                _litLightScore[slot] = score;
                _litLightPosition[slot] = new Vector4(light.Position, light.Radius);
                // The same 0.1 output scale as DeferredPointLight.fx.
                _litLightColor[slot] = new Vector4(light.ColorV3 * light.Intensity * 0.1f, 0);
            }
        Set("PointLightCount", (float)count);
        _shader.Parameters["PointLightPosition"]?.SetValue(_litLightPosition);
        _shader.Parameters["PointLightColor"]?.SetValue(_litLightColor);
    }

    // ---------------------------------------------------------------------------------------------
    // Volumes
    // ---------------------------------------------------------------------------------------------

    private void DrawVolume(GraphicsDevice graphics, ParticleSystemComponent system, Matrix inverseView, Matrix viewProjection)
    {
        ParticleVolume volume = system.Volume;
        BoundingBox bounds = volume.Bounds;
        _drawnVolumes.Add(volume); // keep its texture while it is merely off screen
        if (!TryScreenRect(graphics.Viewport, bounds, viewProjection, out Rectangle rect)) return;

        Texture3D texture = Upload(graphics, volume);
        _noise ??= CreateNoiseTexture(graphics);
        _fullScreenTriangle ??= new FullScreenTriangle(graphics);

        ParticleVolumetricModule settings = system.Volumetric;
        ParticleRendererModule renderer = system.Renderer;
        // Holes shrink as they refill (the same shape ParticleVolume.CarveFactor gives scripts).
        float refill = Finite(settings.RefillTime, 2.5f, 0.01f, 1000);
        int carves = 0;
        foreach (ParticleVolumeCarve carve in volume.Carves)
        {
            float strength = 1 - carve.Age / refill;
            if (strength <= 0 || carves >= _carveFrom.Length) continue;
            _carveFrom[carves] = new Vector4(carve.From, carve.Radius * (float)Math.Sqrt(strength));
            _carveTo[carves] = new Vector4(carve.To, 0);
            carves++;
        }

        _shader.CurrentTechnique = _shader.Techniques["Volume"];
        Set("InverseViewProjection", Matrix.Invert(viewProjection));
        Set("CameraForward", inverseView.Forward);
        Set("VolumeMin", bounds.Min);
        Set("VolumeSize", bounds.Max - bounds.Min);
        Set("VolumeCell", volume.CellSize);
        Set("VolumeDensity", Finite(settings.Density, 4, 0, 1000));
        Set("VolumeErosion", Finite(settings.NoiseErosion, 0.5f, 0, 1));
        float noiseScale = Finite(settings.NoiseScale, 0.7f, 0.001f, 100);
        Set("VolumeNoiseScale", noiseScale);
        // The detail noise rises through the volume and rolls slowly sideways (in noise-texture tiles).
        float drift = Finite(settings.NoiseSpeed, 0.25f, -100, 100) * volume.Time * noiseScale * 0.25f;
        Set("VolumeNoiseOffset", new Vector3(drift * 0.13f, drift * 0.07f, -drift));
        Set("VolumeSteps", (float)settings.ClampedSteps);
        Set("VolumeJitter", (float)(_frame % 64));
        Set("VolumeUniformColor", volume.UniformColor);
        Color color = system.Main.StartColor;
        Set("VolumeColor", new Vector3(color.R, color.G, color.B) / 255f);
        Set("Brightness", Finite(renderer.Brightness, 1, 0, 1000));
        Set("Lit", renderer.Lit);
        _shader.Parameters["CarveCount"]?.SetValue(carves);
        _shader.Parameters["CarveFrom"]?.SetValue(_carveFrom);
        _shader.Parameters["CarveTo"]?.SetValue(_carveTo);
        _shader.Parameters["VolumeTexture"]?.SetValue(texture);
        _shader.Parameters["NoiseTexture"]?.SetValue(_noise);

        graphics.BlendState = renderer.Blend == ParticleBlendMode.Additive ? AddBlend : PremultipliedBlend;
        graphics.RasterizerState = Scissored;
        graphics.ScissorRectangle = rect;
        _shader.CurrentTechnique.Passes[0].Apply();
        _fullScreenTriangle.Draw(graphics);
        LastVolumeCount++;
    }

    /// <summary>The volume's box on screen (padded); the whole view when the camera is inside or right next to it.</summary>
    private static bool TryScreenRect(Viewport viewport, BoundingBox bounds, Matrix viewProjection, out Rectangle rect)
    {
        rect = viewport.Bounds;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (Vector3 corner in bounds.GetCorners())
        {
            Vector4 clip = Vector4.Transform(new Vector4(corner, 1), viewProjection);
            if (clip.W < 0.1f) return true;
            float x = (clip.X / clip.W * 0.5f + 0.5f) * viewport.Width;
            float y = (0.5f - clip.Y / clip.W * 0.5f) * viewport.Height;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        int left = Math.Max(viewport.X, (int)Math.Floor(minX) - 2);
        int top = Math.Max(viewport.Y, (int)Math.Floor(minY) - 2);
        int right = Math.Min(viewport.X + viewport.Width, (int)Math.Ceiling(maxX) + 2);
        int bottom = Math.Min(viewport.Y + viewport.Height, (int)Math.Ceiling(maxY) + 2);
        if (right <= left || bottom <= top) return false;
        rect = new Rectangle(left, top, right - left, bottom - top);
        return true;
    }

    private Texture3D Upload(GraphicsDevice graphics, ParticleVolume volume)
    {
        _volumeTextures.TryGetValue(volume, out var entry);
        Texture3D texture = entry.Texture;
        if (texture != null && entry.Version == volume.Version) return texture;
        if (texture == null || texture.Width != volume.SizeX || texture.Height != volume.SizeY || texture.Depth != volume.SizeZ)
        {
            texture?.Dispose();
            texture = new Texture3D(graphics, volume.SizeX, volume.SizeY, volume.SizeZ, false, SurfaceFormat.Color);
        }
        texture.SetData(volume.Cells, 0, volume.SizeX * volume.SizeY * volume.SizeZ);
        _volumeTextures[volume] = (texture, volume.Version);
        return texture;
    }

    // Textures of volumes that weren't drawn this frame (gone or emptied) are let go.
    private void ReleaseUnusedVolumes()
    {
        if (_volumeTextures.Count == 0) return;
        _staleVolumes.Clear();
        foreach (ParticleVolume volume in _volumeTextures.Keys)
            if (!_drawnVolumes.Contains(volume)) _staleVolumes.Add(volume);
        foreach (ParticleVolume volume in _staleVolumes)
        {
            _volumeTextures[volume].Texture?.Dispose();
            _volumeTextures.Remove(volume);
        }
    }

    /// <summary>32³ tiling detail noise for volumes: R = inverted Worley (billows), G = value-noise fbm (wisps), B = a second value noise (warp).</summary>
    private static Texture3D CreateNoiseTexture(GraphicsDevice graphics)
    {
        const int Size = 32, Cells = 4;
        var points = new Vector3[Cells * Cells * Cells];
        uint rng = 0x9E3779B9u;
        float Next()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng >> 8) * (1f / 16777216f);
        }
        for (int i = 0; i < points.Length; i++) points[i] = new Vector3(Next(), Next(), Next());

        var data = new Color[Size * Size * Size];
        const float CellSize = (float)Size / Cells;
        for (int z = 0; z < Size; z++)
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            Vector3 p = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) / CellSize;
            int cx = (int)p.X, cy = (int)p.Y, cz = (int)p.Z;
            float nearest = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gy = cy + dy, gz = cz + dz;
                Vector3 feature = points[Wrap(gx, Cells) + Cells * (Wrap(gy, Cells) + Cells * Wrap(gz, Cells))] + new Vector3(gx, gy, gz);
                nearest = Math.Min(nearest, Vector3.DistanceSquared(p, feature));
            }
            float worley = 1 - Math.Clamp((float)Math.Sqrt(nearest) / 0.9f, 0, 1);
            float value = 0.5f * TilingValueNoise(x, y, z, Size, 4) + 0.3f * TilingValueNoise(x, y, z, Size, 8) +
                          0.2f * TilingValueNoise(x, y, z, Size, 16);
            // A second field (axes swapped) for the domain warp.
            float warp = 0.6f * TilingValueNoise(z, x, y, Size, 4) + 0.4f * TilingValueNoise(y, z, x, Size, 8);
            data[x + Size * (y + Size * z)] = new Color(worley, value, warp, 1f);
        }
        var texture = new Texture3D(graphics, Size, Size, Size, false, SurfaceFormat.Color);
        texture.SetData(data);
        return texture;
    }

    private static int Wrap(int value, int period) => ((value % period) + period) % period;

    // Smooth value noise with `period` lattice cells across the tile, so it wraps seamlessly.
    private static float TilingValueNoise(int x, int y, int z, int size, int period)
    {
        float scale = (float)period / size;
        float fx = (x + 0.5f) * scale, fy = (y + 0.5f) * scale, fz = (z + 0.5f) * scale;
        int ix = (int)fx, iy = (int)fy, iz = (int)fz;
        float u = Smooth(fx - ix), v = Smooth(fy - iy), w = Smooth(fz - iz);
        float Corner(int ox, int oy, int oz) => Hash(Wrap(ix + ox, period), Wrap(iy + oy, period), Wrap(iz + oz, period), period);
        float x00 = MathHelper.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), u), x10 = MathHelper.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), u);
        float x01 = MathHelper.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), u), x11 = MathHelper.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), u);
        return MathHelper.Lerp(MathHelper.Lerp(x00, x10, v), MathHelper.Lerp(x01, x11, v), w);
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    private static float Hash(int x, int y, int z, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + salt * 144269);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) * (1f / 16777215f);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Billboards
    // ---------------------------------------------------------------------------------------------

    private void Fill(ParticleSystemComponent system, Vector3 cameraPosition, int offset, int count)
    {
        ReadOnlySpan<Particle> particles = system.Simulation.Particles;
        ParticleSortMode sort = system.Renderer.SortMode;
        if (sort == ParticleSortMode.None)
        {
            for (int i = 0; i < count; i++) Write(in particles[i], offset + i);
            return;
        }
        if (_sortKeys.Length < count)
        {
            _sortKeys = new float[count];
            _sortOrder = new int[count];
        }
        for (int i = 0; i < count; i++)
        {
            _sortOrder[i] = i;
            // Ascending keys draw first: the farthest / oldest / youngest particle first.
            _sortKeys[i] = sort switch
            {
                ParticleSortMode.Distance => -Vector3.DistanceSquared(particles[i].Position, cameraPosition),
                ParticleSortMode.OldestFirst => particles[i].Age,
                _ => -particles[i].Age,
            };
        }
        Array.Sort(_sortKeys, _sortOrder, 0, count);
        for (int i = 0; i < count; i++) Write(in particles[_sortOrder[i]], offset + i);
    }

    private void Write(in Particle p, int index)
    {
        _data[index] = new ParticleInstance
        {
            Center = p.Position,
            Color = p.Color,
            Shape = new Vector4(p.Size, p.Size, p.Rotation, p.Frame),
            Velocity = new Vector4(p.DrawVelocity, 0),
        };
    }

    private void EnsureBuffers(GraphicsDevice graphics, int count)
    {
        if (_quad == null)
        {
            _quad = new VertexBuffer(graphics, QuadVertex.Declaration, 4, BufferUsage.WriteOnly);
            _quad.SetData(new[]
            {
                new QuadVertex { Corner = new Vector2(-1, -1) }, new QuadVertex { Corner = new Vector2(1, -1) },
                new QuadVertex { Corner = new Vector2(1, 1) }, new QuadVertex { Corner = new Vector2(-1, 1) },
            });
            _indices = new IndexBuffer(graphics, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
            _indices.SetData(new short[] { 0, 1, 2, 0, 2, 3 });
        }
        if (_instances != null && _instances.VertexCount >= count) return;
        int capacity = 1024;
        while (capacity < count) capacity *= 2;
        _instances?.Dispose();
        _instances = new DynamicVertexBuffer(graphics, ParticleInstance.Declaration, capacity, BufferUsage.WriteOnly);
        _data = new ParticleInstance[capacity];
    }

    private static float Finite(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    // The compiler strips unused parameters; a missing one must not break the frame.
    private void Set(string name, Matrix value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, Vector3 value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, Vector2 value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, float value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, bool value) => _shader.Parameters[name]?.SetValue(value);

    public void Dispose()
    {
        _quad?.Dispose();
        _indices?.Dispose();
        _instances?.Dispose();
        _textures?.Dispose();
        _noise?.Dispose();
        _fullScreenTriangle?.Dispose();
        foreach (var entry in _volumeTextures.Values) entry.Texture?.Dispose();
        _volumeTextures.Clear();
        _noise = null;
        _fullScreenTriangle = null;
        _quad = null;
        _indices = null;
        _instances = null;
        _textures = null;
        _shader.Dispose();
    }
}
