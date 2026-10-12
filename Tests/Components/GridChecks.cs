using System.Reflection;
using Anvil.ViewModels;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

internal static class GridChecks
{
    /// <summary>Anvil's viewport Grid button reaches the engine through the bridge.</summary>
    public static void Run()
    {
        bool shown = GameSettings.e_showgrid;
        try
        {
            Check(GameSettings.e_showgrid && GameSettings.e_gridheight == 0 && GameSettings.e_gridcellsize == 1,
                "the ground grid is on by default, at Z = 0 with 1 m cells");
            var bridge = new EditorBridge();
            typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(bridge, new object[] { new MainSceneLogic(), new EditorLogic(), new Assets() });
            var main = new MainWindowViewModel();
            typeof(MainWindowViewModel).GetField("_bridge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, bridge);
            Check(main.IsGridVisible && main.ToggleGridCommand is not null, "the viewport toolbar has an active Grid toggle");
            main.ToggleGridCommand!.Execute(null);
            Check(GameSettings.e_showgrid, "the grid toggle waits for the game thread");
            Drain(bridge);
            Check(!main.IsGridVisible && !GameSettings.e_showgrid && main.GridToggleTip == "Show ground grid",
                "Grid off hides the engine grid");
            main.ToggleGridCommand!.Execute(null);
            Drain(bridge);
            Check(main.IsGridVisible && GameSettings.e_showgrid, "Grid on shows it again");
        }
        finally
        {
            GameSettings.e_showgrid = shown;
        }
    }

    /// <summary>
    /// Draws the grid over a cleared image from a camera above the ground: lines below the horizon, nothing in
    /// the sky, nothing where nearer geometry covers the plane, red/green axes, nothing in Play or when off.
    /// Set OBSIDIAN_GRID_PNG to a file path to save the image.
    /// </summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var effect = content.Load<Effect>("Shaders/Editor/Grid");
        Check(effect.Techniques["Grid"] != null &&
              new[] { "InverseViewProjection", "View", "CameraPosition", "GridHeight", "CellSize", "DepthMap" }.All(p => effect.Parameters[p] != null),
            "the grid shader has its technique and parameters");

        const int Size = 256;
        const float FarClip = 500;
        bool editor = GameSettings.e_enableeditor, shown = GameSettings.e_showgrid;
        using var module = new GridRenderModule(content);
        var triangle = new FullScreenTriangle(graphics);
        using var target = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.Color, DepthFormat.None);
        using var depth = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.Single, DepthFormat.None);
        // Sky everywhere (depth 1) except a box 3 m away in the lower-left quarter.
        var depthValues = new float[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                depthValues[y * Size + x] = x < Size / 4 && y > Size * 3 / 4 ? 3f / FarClip : 1;
        depth.SetData(depthValues);

        // Looking slightly down: the horizon crosses the upper half of the image.
        var target3 = new Vector3(0, 0, 1.5f);
        var camera = new Camera(new Vector3(6, -10, 3), target3);
        Matrix view = Matrix.CreateLookAt(camera.Position, target3, Vector3.UnitZ);
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 1, FarClip);
        Matrix viewProjection = view * projection;
        var background = new Color(40, 40, 40);

        Color[] Render()
        {
            graphics.SetRenderTarget(target);
            graphics.Clear(background);
            module.Draw(graphics, camera, view, viewProjection, depth, FarClip, triangle);
            graphics.SetRenderTarget(null);
            var pixels = new Color[Size * Size];
            target.GetData(pixels);
            return pixels;
        }

        (int X, int Y) Screen(Vector3 world)
        {
            Vector4 clip = Vector4.Transform(new Vector4(world, 1), viewProjection);
            return ((int)((clip.X / clip.W * 0.5f + 0.5f) * Size), (int)((-clip.Y / clip.W * 0.5f + 0.5f) * Size));
        }

        bool Drawn(Color c) => c != background;

        try
        {
            GameSettings.e_enableeditor = true;
            GameSettings.e_showgrid = true;
            var pixels = Render();
            string? png = Environment.GetEnvironmentVariable("OBSIDIAN_GRID_PNG");
            if (!string.IsNullOrEmpty(png))
                using (var stream = File.Create(png)) target.SaveAsPng(stream, Size, Size);

            var origin = Screen(Vector3.Zero);
            int lowerRows = Enumerable.Range(Size / 2, Size / 4).Count(y => Enumerable.Range(Size / 4, Size / 2).Any(x => Drawn(pixels[y * Size + x])));
            Check(lowerRows > Size / 6, "the grid covers the ground below the horizon");
            Check(Enumerable.Range(0, 8 * Size).All(i => !Drawn(pixels[i])), "the sky above the horizon stays clear");
            Check(Enumerable.Range(Size * 3 / 4 + 2, Size / 4 - 2).All(y => Enumerable.Range(0, Size / 4 - 2).All(x => !Drawn(pixels[y * Size + x]))),
                "geometry in front of the ground hides the grid");

            Color Near(Vector3 world, Func<Color, float> score)
            {
                var (sx, sy) = Screen(world);
                Color best = background;
                for (int y = Math.Max(0, sy - 2); y <= Math.Min(Size - 1, sy + 2); y++)
                    for (int x = Math.Max(0, sx - 2); x <= Math.Min(Size - 1, sx + 2); x++)
                        if (score(pixels[y * Size + x]) > score(best)) best = pixels[y * Size + x];
                return best;
            }
            Color xAxis = Near(new Vector3(2.5f, 0, 0), c => c.R - c.G);
            Color yAxis = Near(new Vector3(0, 2.5f, 0), c => c.G - c.R);
            Check(origin.X > 0 && origin.X < Size && xAxis.R > xAxis.G + 20 && yAxis.G > yAxis.R + 20,
                "the X axis is drawn red and the Y axis green through the origin");

            GameSettings.e_showgrid = false;
            Check(Render().All(c => !Drawn(c)), "Grid off draws nothing");
            GameSettings.e_showgrid = true;
            GameSettings.e_enableeditor = false;
            Check(Render().All(c => !Drawn(c)), "the grid is never drawn in Play (editor off)");
        }
        finally
        {
            GameSettings.e_enableeditor = editor;
            GameSettings.e_showgrid = shown;
            triangle.Dispose();
        }
    }

    private static void Drain(EditorBridge bridge)
    {
        for (int i = 0; i < 6; i++)
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, null);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
