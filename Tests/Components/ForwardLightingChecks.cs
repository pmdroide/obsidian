using System.Reflection;
using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

internal static class ForwardLightingChecks
{
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        using var forward = new ForwardRenderModule(content, "Shaders/Forward/Forward");
        using var basic = new BasicEffect(graphics);
        using var material = new MaterialEffect(basic);
        material.Initialize(new Color(128, 128, 128), 0.35f, 0);
        material.Opacity = 0.5f;
        using var blackSky = new TextureCube(graphics, 1, false, SurfaceFormat.Color);
        foreach (var face in Enum.GetValues<CubeMapFace>()) blackSky.SetData(face, new[] { Color.Black });
        using var target = new RenderTarget2D(graphics, 48, 48, false, SurfaceFormat.HalfVector4, DepthFormat.Depth24);
        using var atlas = new Texture2D(graphics, 48, 8, false, SurfaceFormat.HalfSingle);
        var pixels = new HalfVector4[48 * 48];
        var meshes = new MeshMaterialLibrary(graphics);
        var vertices = new[]
        {
            Vertex(-1, -1), Vertex(1, -1), Vertex(1, 1),
            Vertex(-1, -1), Vertex(1, 1), Vertex(-1, 1),
        };
        var point = new PointLight(new Vector3(0, 0, 3), 10, Color.White, 5, true, false, 64, 0, false)
            { ShadowTileSize = 8, ShadowTilesDrawn = true, ShadowMapRadius = 3 };
        for (int face = 0; face < 6; face++) point.ShadowTiles[face] = new Vector4(face * 8, 0, 8, 1);
        var lights = new List<PointLight> { point };
        var camera = new Camera(new Vector3(0, 0, 5), Vector3.Zero);
        var context = new ForwardRenderModule.Context { Camera = camera, PointLights = lights,
            GlobalProbe = blackSky, AmbientStrength = 0, ShadowAtlas = atlas, Resolution = new Vector2(48) };
        Matrix world = Matrix.Identity;
        Matrix vp = Matrix.Identity;
        var shader = content.Load<Effect>("Shaders/Forward/Forward");

        int faceIndex = 0;
        foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
        {
            world = SpotLight.RotationFromDirection(direction);
            camera = new Camera(-direction * 5, Vector3.Zero);
            context.Camera = camera;
            point.Position = -direction * 3;
            SetView(direction);
            SetDepth(0); // Atlas stores 1 - distance/radius: 0 is the far plane.
            float lit = Render().Length();
            SetOccluder(faceIndex++); // Only the selected face contains an occluder.
            float shadowed = Render().Length();
            Check(lit > 0.01f && shadowed < lit * 0.01f, $"forward point shadows use the {direction} cube face");
        }

        point.ShadowStrength = 0.5f;
        float faded = Render().Length();
        point.ShadowStrength = 0;
        float unshadowed = Render().Length();
        Check(faded > unshadowed * 0.48f && faded < unshadowed * 0.52f,
            "forward point shadows respect the atlas distance fade");
        point.ShadowStrength = 1;
        point.ShadowTilesDrawn = false;
        Check(Near(Render().Length(), unshadowed), "unrendered shadow tiles do not darken forward materials");
        point.ShadowTilesDrawn = true;
        point.CastShadows = false;
        Check(Near(Render().Length(), unshadowed), "turning point shadows off clears the forward shadow state");
        point.CastShadows = true;
        context.ShadowAtlas = null;
        Check(Near(Render().Length(), unshadowed), "a missing shadow atlas leaves forward lights unshadowed");
        context.ShadowAtlas = atlas;
        point.ShadowTiles[5] = Vector4.Zero;
        Check(Near(Render().Length(), unshadowed), "a missing cube face does not sample another light's tile");

        var spot = new SpotLight(new Vector3(0, 0, 3), 10, Color.White, 5, -Vector3.UnitZ,
            castShadows: true) { ShadowTileSize = 8, ShadowTilesDrawn = true, ShadowMapRadius = 1 };
        spot.ShadowTiles[5] = new Vector4(40, 0, 8, 1);
        lights.Clear(); lights.Add(spot);
        // The current world/view faces +Z; surface-to-light is +Z and light-to-surface is -Z (face 5).
        Check(Render().Length() < 0.001f, "forward spotlights receive local shadows too");
        spot.CastShadows = false;
        float plainSpot = Render().Length();
        Check(plainSpot > 0.01f, "unshadowed forward spotlights retain their cone lighting");

        using var red = new Texture2D(graphics, 2, 2);
        using var green = new Texture2D(graphics, 2, 2);
        using var black = new Texture2D(graphics, 2, 2);
        red.SetData(Enumerable.Repeat(Color.Red, 4).ToArray());
        green.SetData(Enumerable.Repeat(Color.Lime, 4).ToArray());
        black.SetData(Enumerable.Repeat(Color.Black, 4).ToArray());
        SetCookie(spot, red);
        Vector3 redLight = Render();
        Check(redLight.X > 0.01f && redLight.Y < 0.001f && redLight.Z < 0.001f,
            "spot cookies tint forward diffuse and specular light");
        var other = new SpotLight(spot.Position, 10, Color.White, 4, -Vector3.UnitZ);
        SetCookie(other, green);
        lights.Add(other);
        Vector3 combined = Render();
        var cached = shader.Parameters["CookieAtlas"].GetValueTexture2D();
        Check(combined.X > 0.01f && combined.Y > 0.01f && combined.Z < 0.001f,
            "multiple forward spotlights use their own cookie without colour bleeding");
        other.Intensity = 6;
        Vector3 reranked = Render();
        Check(reranked.Y > combined.Y && Near(reranked.X, combined.X) &&
              ReferenceEquals(cached, shader.Parameters["CookieAtlas"].GetValueTexture2D()),
            "ranking changes preserve cookie associations and reuse the cached atlas");
        lights.Remove(other);
        SetCookie(spot, black);
        Check(Render().Length() < 0.001f, "a black cookie blocks both forward diffuse and specular light");
        SetCookie(spot, null);
        Check(Near(Render().Length(), plainSpot), "removing cookies clears their forward shader state");

        using var split = new Texture2D(graphics, 16, 16);
        split.SetData(Enumerable.Range(0, 256).Select(i => i % 16 < 8 ? Color.Red : Color.Lime).ToArray());
        SetCookie(spot, split);
        Render();
        Vector4 left = pixels[24 * 48 + 18].ToVector4(), right = pixels[24 * 48 + 30].ToVector4();
        Check(left.X > left.Y * 5 && right.Y > right.X * 5, "forward cookies project their pattern across the surface");
        spot.RotationMatrix = Matrix.CreateRotationZ(MathHelper.PiOver2);
        Render();
        Vector4 top = pixels[18 * 48 + 24].ToVector4(), bottom = pixels[30 * 48 + 24].ToVector4();
        Check(top.Y > top.X * 5 && bottom.X > bottom.Y * 5, "rolling a spotlight rotates its forward cookie projection");
        spot.RotationMatrix = Matrix.Identity;
        SetCookie(spot, null);
        material.Opacity = 0;
        Check(Render().Length() > 0.001f, "clear glass retains its direct specular reflections");
        SetCookie(spot, black);
        Check(Render().Length() < 0.001f, "cookies also mask the specular reflection on clear glass");
        SetCookie(spot, null);
        spot.CastShadows = true;
        Check(Render().Length() < 0.001f, "local shadows also mask the specular reflection on clear glass");

        void SetView(Vector3 direction)
        {
            Vector3 up = Math.Abs(direction.Z) > 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            vp = Matrix.CreateLookAt(camera.Position, Vector3.Zero, up) *
                Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 20);
        }
        void SetDepth(float depth) => atlas.SetData(Enumerable.Repeat(new HalfSingle(depth), 48 * 8).ToArray());
        void SetOccluder(int face)
        {
            var depths = new HalfSingle[48 * 8];
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++) depths[y * 48 + face * 8 + x] = new HalfSingle(1);
            atlas.SetData(depths);
        }
        Vector3 Render()
        {
            forward.PrepareCookies(graphics, context.Camera, null!, lights);
            graphics.SetRenderTarget(target);
            graphics.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            forward.Draw(graphics, target, meshes, vp, context);
            forward.SetMaterialSettings(material);
            graphics.BlendState = BlendState.Opaque;
            graphics.DepthStencilState = DepthStencilState.None;
            graphics.RasterizerState = RasterizerState.CullNone;
            forward.Apply(world, null, vp);
            graphics.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2);
            graphics.SetRenderTarget(null);
            target.GetData(pixels);
            Vector4 pixel = pixels[24 * 48 + 24].ToVector4();
            return new Vector3(pixel.X, pixel.Y, pixel.Z);
        }
    }

    private static VertexPositionNormalTexture Vertex(float x, float y) => new(new Vector3(x, y, 0), Vector3.UnitZ, Vector2.Zero);
    private static void SetCookie(SpotLight spot, Texture2D? cookie) =>
        typeof(SpotLight).GetProperty(nameof(SpotLight.Cookie))!.SetValue(spot, cookie);
    private static bool Near(float a, float b) => Math.Abs(a - b) < Math.Max(0.0001f, Math.Abs(b) * 0.01f);
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
