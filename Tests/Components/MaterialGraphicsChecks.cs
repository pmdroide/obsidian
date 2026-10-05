using System.Runtime.InteropServices;
using System.Reflection;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
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
            EnvironmentChecks.RunGraphics(graphics, content);
            var model = content.Load<Model>("GameObjects/Default/cube");
            var definition = new ModelDefinition(model, new BoundingBox(-Vector3.One, Vector3.One));
            using var basicEffect = new BasicEffect(graphics);
            using var source = new MaterialEffect(basicEffect);
            source.Initialize(Color.Red, 0.6f, 0.1f);
            using var texture = new Texture2D(graphics, 1, 1);
            texture.SetData(new[] { Color.White });
            source.AlbedoMap = texture;
            source.Type = MaterialEffect.MaterialTypes.SubsurfaceScattering;
            source.IsTransparent = true;
            source.HasShadow = false;
            source.EmissiveStrength = 2;
            var authored = new BasicEntity(definition, source, Vector3.Zero, Matrix.Identity, Vector3.One);
            var scene = new MainSceneLogic();
            scene.BasicEntities.Add(authored);
            var bridge = new EditorBridge();
            typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(bridge, new object[] { scene, new EditorLogic(), new Assets { Cube = definition } });
            bridge.EnqueueAddComponent(authored.Id, MaterialComponent.TypeId);
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, null);
            var inherited = authored.Components.OfType<MaterialComponent>().Single();
            Check(inherited.MaterialType == source.Type && inherited.IsTransparent && !inherited.CastShadows &&
                inherited.Red == 1 && inherited.Green == 0 && inherited.Blue == 0 &&
                inherited.Roughness == 0.6f && inherited.Metallic == 0.1f && inherited.EmissiveStrength == 2,
                "adding Material copies the original surface settings and material type");
            source.Type = MaterialEffect.MaterialTypes.Basic;
            source.IsTransparent = false;
            source.HasShadow = true;
            source.EmissiveStrength = 0;
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
            // Exercise compiled texture loading, independent scalar maps, and explicit removal.
            using var mapped = source.Clone();
            var maps = new MaterialComponent
            {
                BaseColorTexture = "GameObjects/Error/ERRORText_typeBlinn_BaseColor.jpg",
                NormalTexture = "GameObjects/Error/ERRORText_typeBlinn_Normal.jpg",
                RoughnessTexture = "GameObjects/Error/ERRORText_typeBlinn_Roughness.jpg",
                MetallicTexture = "GameObjects/Error/ERRORText_typeBlinn_Metallic.jpg",
                MaskTexture = "GameObjects/error.png",
                DisplacementTexture = "GameObjects/Error/ERRORText_typeBlinn_Roughness.jpg",
            };
            maps.ApplyTo(mapped, content);
            Check(mapped.HasDiffuse && mapped.HasNormalMap && mapped.HasRoughnessMap && mapped.HasMetallic &&
                mapped.HasMask && mapped.HasDisplacement && mapped.UseComponentRoughnessMap && mapped.UseComponentMetallicMap &&
                mapped.AlbedoMap != source.AlbedoMap && source.AlbedoMap == texture,
                "all six texture slots load compiled maps without changing source assets");
            // Bind only metallic, without normal/roughness/albedo, to exercise independent sampling.
            using var grayscale = new Texture2D(graphics, 1, 1);
            grayscale.SetData(new[] { new Color(200, 200, 200) });
            instance.AlbedoMap = null;
            instance.NormalMap = null;
            instance.RoughnessMap = null;
            instance.MetallicMap = grayscale;
            instance.UseComponentMetallicMap = true;
            gbuffer.Draw(graphics, new[] { new RenderTargetBinding(target), new RenderTargetBinding(normals),
                new RenderTargetBinding(depth) }, meshes, vp, view);
            graphics.SetRenderTarget(null);
            normals.GetData(surfacePixels);
            var encodedWithMap = surfacePixels.Select(p => p.B).ToArray();
            instance.UseComponentMetallicMap = false;
            gbuffer.Draw(graphics, new[] { new RenderTargetBinding(target), new RenderTargetBinding(normals),
                new RenderTargetBinding(depth) }, meshes, vp, view);
            graphics.SetRenderTarget(null);
            normals.GetData(surfacePixels);
            Check(surfacePixels.Where((p, i) => p.B != encodedWithMap[i]).Any(),
                "metallic texture affects G-buffer even without other maps");
            instance.RoughnessMap = grayscale;
            instance.UseComponentRoughnessMap = true;
            gbuffer.Draw(graphics, new[] { new RenderTargetBinding(target), new RenderTargetBinding(normals),
                new RenderTargetBinding(depth) }, meshes, vp, view);
            graphics.SetRenderTarget(null);
            normals.GetData(surfacePixels);
            Check(surfacePixels.Any(p => Math.Abs(p.A - 200) <= 1),
                "roughness texture takes precedence over the component slider");
            maps.BaseColorTexture = maps.NormalTexture = maps.RoughnessTexture = maps.MetallicTexture =
                maps.MaskTexture = maps.DisplacementTexture = "";
            var loadedMap = mapped.AlbedoMap;
            maps.ApplyTo(mapped, content);
            Check(!mapped.HasDiffuse && !mapped.HasNormalMap && !mapped.HasRoughnessMap && !mapped.HasMetallic &&
                !mapped.HasMask && !mapped.HasDisplacement && !loadedMap.IsDisposed,
                "clearing all texture maps resets flags without disposing shared textures");
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
