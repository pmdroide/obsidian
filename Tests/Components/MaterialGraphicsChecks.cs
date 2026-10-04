using System.Runtime.InteropServices;
using Engine.Components;
using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

internal static class MaterialGraphicsChecks
{
    // A hidden native window lets WindowsDX render/read back without opening the editor.
    public static void Run()
    {
        IntPtr window = CreateWindowEx(0, "STATIC", "Material checks", 0, 0, 0, 96, 96,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new InvalidOperationException("Could not create hidden graphics window.");
        try
        {
            using var graphics = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                new PresentationParameters { BackBufferWidth = 96, BackBufferHeight = 96,
                    DeviceWindowHandle = window, DepthStencilFormat = DepthFormat.Depth24 });
            var services = new GameServiceContainer();
            services.AddService<IGraphicsDeviceService>(new DeviceService(graphics));
            using var content = new ContentManager(services,
                Path.Combine(AppContext.BaseDirectory, "Content"));
            var model = content.Load<Model>("GameObjects/Default/cube");
            var definition = new ModelDefinition(model, new BoundingBox(-Vector3.One, Vector3.One));
            using var basicEffect = new BasicEffect(graphics);
            using var source = new MaterialEffect(basicEffect);
            source.Initialize(Color.Red, 0.6f, 0.1f);
            using var texture = new Texture2D(graphics, 1, 1);
            texture.SetData(new[] { Color.White });
            source.AlbedoMap = texture;
            var component = new MaterialComponent { Shader = MaterialShader.Water,
                Red = 0.05f, Green = 0.35f, Blue = 0.45f, Roughness = 0.08f };
            var entity = new BasicEntity(definition, source, Vector3.Zero, Matrix.Identity,
                new Vector3(1, 1, 0.05f)) { IsEnabled = true };
            entity.Components.Add(component);
            var meshes = new MeshMaterialLibrary(graphics);
            entity.RegisterInLibrary(meshes);
            var instance = meshes.MaterialLib[0].GetMaterial();
            Check(instance.Type == MaterialEffect.MaterialTypes.Water && instance.IsTransparent &&
                !instance.HasShadow && source.Type == MaterialEffect.MaterialTypes.Basic &&
                instance.AlbedoMap == source.AlbedoMap,
                "water instance preserves maps without mutating the source material");
            var clone = (BasicEntity)entity.Clone;
            clone.IsEnabled = true;
            clone.RegisterInLibrary(meshes);
            Check(meshes.Index == model.Meshes.Sum(m => m.MeshParts.Count) * 2,
                "editable material instances are not merged across objects");
            clone.Dispose(meshes);
            Check(!instance.IsDisposed && meshes.Index > 0, "deleting a clone leaves the original material alive");
            float radius = model.Meshes.Max(m => m.BoundingSphere.Radius);
            var camera = new Camera(new Vector3(0, -radius * 3, radius * 3), Vector3.Zero);
            Matrix view = Matrix.CreateLookAt(camera.Position, Vector3.Zero, Vector3.UnitZ);
            Matrix vp = view * Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, radius * 20);
            using var water = new WaterRenderModule(content);
            using var target = new RenderTarget2D(graphics, 96, 96, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var lights = new List<DirectionalLight> { new(Color.White, 10, -Vector3.UnitZ) };
            Color[] Render(float seconds, float depth = 1)
            {
                meshes.FrustumCulling(new List<BasicEntity> { entity }, new BoundingFrustum(vp), true, camera.Position);
                graphics.SetRenderTarget(target);
                graphics.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, depth, 0);
                water.Draw(graphics, meshes, vp, camera, null!, lights,
                    new GameTime(TimeSpan.FromSeconds(seconds), TimeSpan.Zero));
                graphics.SetRenderTarget(null);
                var pixels = new Color[96 * 96];
                target.GetData(pixels);
                return pixels;
            }
            var first = Render(0);
            Check(first.Any(p => p.R > 0 || p.G > 0 || p.B > 0), "water shader renders visible pixels");
            var later = Render(2);
            Check(first.Where((p, i) => p != later[i]).Any(), "water ripples animate over time");
            using var environment = new TextureCube(graphics, 1, false, SurfaceFormat.Color);
            foreach (var face in Enum.GetValues<CubeMapFace>()) environment.SetData(face, new[] { Color.White });
            graphics.SetRenderTarget(target);
            graphics.Clear(Color.Black);
            water.Draw(graphics, meshes, vp, camera, environment, lights, new GameTime());
            graphics.SetRenderTarget(null);
            var reflected = new Color[96 * 96];
            target.GetData(reflected);
            Check(first.Where((p, i) => p != reflected[i]).Any(), "water samples the supplied environment cubemap");
            Check(Render(0, 0).All(p => p == Color.Black), "opaque foreground depth occludes water");
            component.Opacity = 0;
            component.OnChanged(entity);
            Check(instance.IsDisposed && Render(0).All(p => p == Color.Black),
                "editing water releases old instances and zero opacity hides it");
            instance = meshes.MaterialLib[0].GetMaterial();
            component.Enabled = false;
            component.OnChanged(entity);
            Check(instance.IsDisposed && ReferenceEquals(meshes.MaterialLib[0].GetMaterial(), source),
                "disabling material restores the original surface and disposes overrides");
            component.Enabled = true;
            component.Shader = MaterialShader.Standard;
            component.OnChanged(entity);
            instance = meshes.MaterialLib[0].GetMaterial();
            Check(instance.Type == MaterialEffect.MaterialTypes.Basic && !instance.IsTransparent && instance.HasShadow,
                "switching back to Standard restores opaque rendering and shadows");
            using var gbuffer = new GBufferRenderModule(content, "Shaders/GBufferSetup/ClearGBuffer", "Shaders/GBufferSetup/Gbuffer");
            gbuffer.Initialize(graphics);
            gbuffer.FarClip = radius * 20;
            gbuffer.Camera = camera.Position;
            using var normals = new RenderTarget2D(graphics, 96, 96, false, SurfaceFormat.Color, DepthFormat.None);
            using var depth = new RenderTarget2D(graphics, 96, 96, false, SurfaceFormat.Single, DepthFormat.None);
            gbuffer.Draw(graphics, new[] { new RenderTargetBinding(target), new RenderTargetBinding(normals),
                new RenderTargetBinding(depth) }, meshes, vp, view);
            graphics.SetRenderTarget(null);
            var tinted = new Color[96 * 96];
            target.GetData(tinted);
            Check(tinted.Any(p => p.R > 0 && p.G > p.R && p.B > p.G),
                "Standard material tints imported albedo textures");
            var surfacePixels = new Color[96 * 96];
            normals.GetData(surfacePixels);
            Check(surfacePixels.Any(p => Math.Abs(p.A / 255f - component.Roughness) < 0.005f),
                "Standard material writes the authored roughness to the G-buffer");
            entity.Components.Remove(component);
            entity.RefreshMaterials();
            Check(instance.IsDisposed && ReferenceEquals(meshes.MaterialLib[0].GetMaterial(), source),
                "removing material restores the original surface");
            entity.Dispose(meshes);
            Check(meshes.Index == 0 && !source.IsDisposed && !texture.IsDisposed,
                "cleanup unregisters meshes without disposing shared source assets");
        }
        finally { DestroyWindow(window); }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class DeviceService(GraphicsDevice graphics) : IGraphicsDeviceService
    {
        public GraphicsDevice GraphicsDevice => graphics;
        public event EventHandler<EventArgs>? DeviceCreated { add { } remove { } }
        public event EventHandler<EventArgs>? DeviceDisposing { add { } remove { } }
        public event EventHandler<EventArgs>? DeviceReset { add { } remove { } }
        public event EventHandler<EventArgs>? DeviceResetting { add { } remove { } }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
}
