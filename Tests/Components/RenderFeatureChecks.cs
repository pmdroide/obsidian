using System.Reflection;
using System.Text.Json;
using Anvil.Models;
using Anvil.ViewModels;
using Avalonia.Threading;
using Engine.Editor;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Features;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

internal static class RenderFeatureChecks
{
    /// <summary>
    /// Render features without a graphics device or a compile: the pipeline listing, saving, moving between
    /// injection points, script changes undone on Stop, and the bridge + Anvil's Render Pipeline panel.
    /// </summary>
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "obsidian-render-feature-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "System"));
        RenderFeatures.SourceRootOverride = root;
        try
        {
            RenderFeatures.Load(null);
            IReadOnlyList<RenderPipelineLayer> layers = RenderFeatures.Describe();
            List<string> names = layers.Select(l => l.Name).ToList();
            Check(RenderFeatures.All.Count == 0 &&
                  layers.Where(l => l.Kind == RenderPipelineLayerKind.InjectionPoint).Select(l => l.Event)
                      .SequenceEqual(Enum.GetValues<RenderFeatureEvent>()),
                "without RenderFeatures.json there are no features and the pipeline lists the 4 injection points in order");
            Check(Before(names, "Deferred Compose", "After Opaques") && Before(names, "After Opaques", "Forward") &&
                  Before(names, "Weather", "After Transparents") && Before(names, "After Transparents", "Temporal AA") &&
                  Before(names, "Bloom", "After Bloom") && Before(names, "After Bloom", "Post Processing") &&
                  Before(names, "Color Grading", "After Post Processing") && Before(names, "After Post Processing", "Editor Overlays"),
                "each injection point sits between the built-in passes it runs between");

            // Features whose shaders don't exist: listed and saved, never drawn, nothing compiles.
            var a = new RenderFeature { Name = "A", Event = RenderFeatureEvent.AfterOpaques, Shader = "RenderFeatures/A.fx", Color = null };
            var b = new RenderFeature { Name = "B", Event = RenderFeatureEvent.AfterOpaques, Shader = "RenderFeatures/B.fx" };
            var c = new RenderFeature { Name = "C", Event = RenderFeatureEvent.AfterBloom, Shader = "RenderFeatures/C.fx", Blend = RenderFeatureBlend.Additive };
            File.WriteAllText(Path.Combine(root, "System", "RenderFeatures.json"),
                JsonSerializer.Serialize(new RenderFeatures.Data { Features = { a, b, c } }));
            RenderFeatures.Load(null);
            Check(RenderFeatures.All.Select(f => f.Name).SequenceEqual(new[] { "A", "B", "C" }) &&
                  RenderFeatures.All.All(f => f.Status == RenderFeatureStatus.NotCompiled && !f.IsActive && f.Error.Contains("not found")),
                "features with a missing shader load as Not compiled and are never drawn");
            Check(RenderFeatures.Find("a").Color.SequenceEqual(new[] { 1f, 1f, 1f, 1f }) && RenderFeatures.Find("C").Blend == RenderFeatureBlend.Additive,
                "a missing Color defaults to white; Blend is read back");
            Check(Rows(RenderPipelineLayerKind.Feature).SequenceEqual(new[] { "A", "B", "C" }) &&
                  Next(RenderFeatures.Describe(), "After Opaques", 2).SequenceEqual(new[] { "A", "B" }) &&
                  Next(RenderFeatures.Describe(), "After Bloom", 1).SequenceEqual(new[] { "C" }),
                "the pipeline lists every feature under its injection point, in run order");

            Guid idA = RenderFeatures.Find("A").Id, idB = RenderFeatures.Find("B").Id, idC = RenderFeatures.Find("C").Id;
            Check(RenderFeatures.Move(idB, -1) && RenderFeatures.All.Select(f => f.Name).SequenceEqual(new[] { "B", "A", "C" }),
                "Up swaps a feature with the one before it at the same injection point");
            Check(!RenderFeatures.Move(idB, -1), "the first feature of After Opaques can't move up");
            Check(RenderFeatures.Move(idA, 1) && RenderFeatures.Find("A").Event == RenderFeatureEvent.AfterTransparents,
                "Down from the last feature crosses the passes below into the next injection point");
            Check(RenderFeatures.Move(idC, 1) && RenderFeatures.Find("C").Event == RenderFeatureEvent.AfterPostProcessing &&
                  !RenderFeatures.Move(idC, 1), "the last injection point is After Post Processing");
            Check(RenderFeatures.Move(idC, -1) && RenderFeatures.Move(idC, -1) &&
                  RenderFeatures.Find("C").Event == RenderFeatureEvent.AfterTransparents &&
                  RenderFeatures.All.Where(f => f.Event == RenderFeatureEvent.AfterTransparents).Select(f => f.Name).SequenceEqual(new[] { "A", "C" }),
                "Up into the previous injection point lands last there");

            string json = File.ReadAllText(Path.Combine(root, "System", "RenderFeatures.json"));
            RenderFeatures.Data saved = RenderFeatures.Read();
            Check(json.Contains("\"AfterTransparents\"") && json.Contains("\"Additive\"") &&
                  saved.Features.Select(f => f.Name).SequenceEqual(RenderFeatures.All.Select(f => f.Name)) &&
                  saved.Features.Select(f => f.Event).SequenceEqual(RenderFeatures.All.Select(f => f.Event)),
                "moves are saved, events and blends by name");

            RenderFeatures.Mutate(idA, f => { f.Intensity = 0.5f; f.Params = new[] { 1f, 2f }; });
            Check(RenderFeatures.Read().Features.Single(f => f.Id == idA) is { Intensity: 0.5f } savedA &&
                  savedA.Params.SequenceEqual(new[] { 1f, 2f, 0f, 0f }), "an edit is saved; Params is padded to a float4");

            bool editor = GameSettings.e_enableeditor;
            try
            {
                RenderFeatures.Find("A").Intensity = 2f;
                RenderFeatures.Find("A").Enabled = false;
                RenderFeatures.RestoreSaved();
                Check(RenderFeatures.Find("A").Intensity == 0.5f && RenderFeatures.Find("A").Enabled,
                    "values a script changed during Play go back to the saved ones on Stop");
                RenderFeatures.Find("A").ShowInEditMode = false;
                GameSettings.e_enableeditor = true;
                Check(!RenderFeatureRenderModule.HasActive(RenderFeatureEvent.AfterTransparents),
                    "nothing draws without a compiled shader");
            }
            finally { GameSettings.e_enableeditor = editor; }

            // Bridge + Anvil's Render Pipeline panel.
            var bridge = new EditorBridge();
            Invoke(bridge, "Bind", new MainSceneLogic(), new EditorLogic(), new Assets());
            Check(bridge.GetRenderPipeline().Count == RenderFeatures.Describe().Count, "the bridge lists the render pipeline");
            var main = new MainWindowViewModel();
            typeof(MainWindowViewModel).GetField("_bridge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, bridge);
            main.SetInspectorViewCommand.Execute("RenderPipeline");
            Check(main.IsWorkspaceRenderPipelineView && main.IsInspectorSelectionView, "the Pipeline button opens its workspace tab while keeping the object Inspector");

            var deleted = new List<string>();
            RenderPipelineViewModel vm = main.RenderPipeline;
            vm.AttachBridge(bridge, deleted.Add);
            Check(vm.Rows.Count == RenderFeatures.Describe().Count &&
                  vm.Rows.Where(r => r.IsFeature).Select(r => r.Name).SequenceEqual(RenderFeatures.All.Select(f => f.Name)) &&
                  vm.Rows.Count(r => r.IsInjectionPoint) == 4 && vm.Summary == "3 render features",
                "the panel lists passes, injection points and features");

            vm.SelectedRow = vm.Rows.First(r => r.IsStage);
            Check(vm.HasSelectedStage && !vm.HasSelectedFeature && !vm.MoveUpCommand.CanExecute(null) && vm.SelectedStageText.Length > 0,
                "selecting a built-in pass explains it and can't move it");
            vm.SelectedRow = vm.Rows.First(r => r.FeatureId == idA);
            Check(vm.HasSelectedFeature && vm.FeatureName == "A" && vm.Intensity == 0.5 &&
                  vm.EventIndex == (int)RenderFeatureEvent.AfterTransparents && vm.StatusText == "Not compiled",
                "selecting a feature shows its settings");
            vm.Intensity = 1.25;
            vm.BlendIndex = (int)RenderFeatureBlend.Multiply;
            vm.Param3 = 4;
            Drain(bridge);
            RenderFeature engineA = RenderFeatures.Find("A");
            Check(engineA.Intensity == 1.25f && engineA.Blend == RenderFeatureBlend.Multiply && engineA.Params[3] == 4,
                "panel edits reach the engine through the bridge");
            vm.EventIndex = -1;
            Drain(bridge);
            Check(engineA.Event == RenderFeatureEvent.AfterTransparents, "a transient ComboBox index of -1 is ignored");

            vm.Rows.First(r => r.FeatureId == idB).Active = false;
            Drain(bridge);
            Check(!RenderFeatures.Find("B").Enabled, "the row checkbox disables a feature");

            File.WriteAllText(Path.Combine(root, "RenderFeatures", "B.fx").Also(p => Directory.CreateDirectory(Path.GetDirectoryName(p)!)), "// B");
            vm.SelectedRow = vm.Rows.First(r => r.FeatureId == idB);
            vm.DeleteCommand.Execute(null);
            Drain(bridge);
            Check(RenderFeatures.Find("B") == null && RenderFeatures.Read().Features.Count == 2 &&
                  deleted.Count == 1 && deleted[0].EndsWith("B.fx"),
                "Delete Feature removes it and hands its shader to the Recycle Bin");
            RunUiJobs();
            Check(vm.Rows.Count(r => r.IsFeature) == 2, "the panel follows the engine's list");
        }
        finally
        {
            RenderFeatures.SourceRootOverride = null;
            RenderFeatures.Load(null);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// The real thing: New Render Feature writes the template, mgcb compiles it, and the module draws it at an
    /// injection point. Then Replace / Additive / Multiply over a known image, a chain of two features, a shader
    /// error keeping the last good version, and a hot reload after the .fx is saved.
    /// </summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        string root = Path.Combine(Path.GetTempPath(), "obsidian-render-feature-gpu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        RenderFeatures.SourceRootOverride = root;
        bool editor = GameSettings.e_enableeditor;
        try
        {
            RenderFeatures.Load(content);
            RenderFeature vignette = RenderFeatures.Create("Vignette", RenderFeatureEvent.AfterTransparents);
            string shaderFile = RenderFeatures.ShaderSourcePath(vignette);
            Check(vignette.Shader == "RenderFeatures/Vignette.fx" && File.ReadAllText(shaderFile).Contains("technique Main") &&
                  vignette.Status == RenderFeatureStatus.Compiling, "New Render Feature writes the template and starts compiling it");
            WaitForCompile(vignette);
            Check(vignette.Status == RenderFeatureStatus.Ready && vignette.Effect != null &&
                  File.Exists(Path.ChangeExtension(shaderFile, ".xnb")), $"the template compiles with mgcb ({vignette.Error})");

            const int Size = 64;
            using var image = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            using var module = new RenderFeatureRenderModule();
            module.Initialize(graphics);
            var triangle = new FullScreenTriangle(graphics);
            var context = new RenderFeatureRenderModule.Context { Time = 1, InverseViewProjection = Matrix.Identity };
            var grey = new Color(128, 128, 128);

            Color[] Run(RenderFeatureEvent at, bool inPlace = true)
            {
                graphics.SetRenderTarget(image);
                graphics.Clear(grey);
                RenderTarget2D result = module.Draw(graphics, at, image, context, triangle, inPlace);
                graphics.SetRenderTarget(null);
                var pixels = new Color[Size * Size];
                result.GetData(pixels);
                return pixels;
            }

            GameSettings.e_enableeditor = true;
            Color[] pixels = Run(RenderFeatureEvent.AfterTransparents);
            Color centre = pixels[Size / 2 * Size + Size / 2], corner = pixels[0];
            Check(centre.R > 115 && corner.R < 70, $"the template's vignette darkens the corners in place (centre {centre.R}, corner {corner.R})");
            Check(Run(RenderFeatureEvent.AfterOpaques)[0] == grey, "features only run at their own injection point");

            RenderFeatures.Mutate(vignette.Id, f => f.Color = new[] { 1f, 0f, 0f, 1f });
            pixels = Run(RenderFeatureEvent.AfterTransparents, inPlace: false);
            centre = pixels[Size / 2 * Size + Size / 2];
            Check(centre.R > 115 && centre.G < 10, "Color reaches the shader; not in place the result comes back from a scratch target");

            RenderFeatures.Mutate(vignette.Id, f => f.Intensity = 0);
            Check(Run(RenderFeatureEvent.AfterTransparents)[0] == grey, "Intensity 0 leaves the image as it was");

            RenderFeatures.Mutate(vignette.Id, f => f.ShowInEditMode = false);
            RenderFeatures.Mutate(vignette.Id, f => f.Intensity = 1);
            Check(Run(RenderFeatureEvent.AfterTransparents)[0] == grey, "Show in edit mode off hides it while editing");
            GameSettings.e_enableeditor = false;

            // A flat colour shader for the blend modes: outputs Color * Intensity.
            RenderFeature flat = RenderFeatures.Create("Flat", RenderFeatureEvent.AfterBloom);
            File.WriteAllText(RenderFeatures.ShaderSourcePath(flat), FlatShader);
            File.SetLastWriteTimeUtc(RenderFeatures.ShaderSourcePath(flat), DateTime.UtcNow.AddSeconds(5));
            WaitForCompile(flat);
            RenderFeatures.Recompile(flat.Id);
            WaitForCompile(flat);
            Check(flat.Status == RenderFeatureStatus.Ready, $"a hand-written shader compiles ({flat.Error})");

            RenderFeatures.Mutate(flat.Id, f => { f.Color = new[] { 0.25f, 0.5f, 1f, 1f }; f.Blend = RenderFeatureBlend.Replace; });
            Color replaced = Run(RenderFeatureEvent.AfterBloom)[0];
            RenderFeatures.Mutate(flat.Id, f => f.Blend = RenderFeatureBlend.Additive);
            Color added = Run(RenderFeatureEvent.AfterBloom)[0];
            RenderFeatures.Mutate(flat.Id, f => f.Blend = RenderFeatureBlend.Multiply);
            Color multiplied = Run(RenderFeatureEvent.AfterBloom)[0];
            RenderFeatures.Mutate(flat.Id, f => { f.Blend = RenderFeatureBlend.AlphaBlend; f.Color = new[] { 1f, 1f, 1f, 0.5f }; });
            Color blended = Run(RenderFeatureEvent.AfterBloom)[0];
            Check(Near(replaced, 64, 128, 255) && Near(added, 192, 255, 255) && Near(multiplied, 32, 64, 128) && Near(blended, 192, 192, 192) &&
                  added.A == 255 && multiplied.A == 255 && blended.A == 255,
                $"Replace / Additive / Multiply / Alpha Blend, blends keeping the image's alpha ({replaced}, {added}, {multiplied}, {blended})");

            // Two features at one point run in list order, each reading the previous result.
            RenderFeatures.Mutate(flat.Id, f => { f.Blend = RenderFeatureBlend.Multiply; f.Color = new[] { 0.5f, 0.5f, 0.5f, 1f }; });
            RenderFeatures.Mutate(vignette.Id, f => { f.Event = RenderFeatureEvent.AfterBloom; f.Intensity = 1; f.Color = new[] { 1f, 1f, 1f, 1f }; });
            pixels = Run(RenderFeatureEvent.AfterBloom);
            Check(pixels[0].R < 40 && Near(pixels[Size / 2 * Size + Size / 2], 64, 64, 64, 8),
                "features at the same injection point chain, each reading the previous result (vignetted and halved)");
            RenderFeatures.Mutate(flat.Id, f => f.Enabled = false);
            RenderFeatures.Mutate(vignette.Id, f => f.Enabled = false);
            Check(Run(RenderFeatureEvent.AfterBloom)[0] == grey, "disabled features don't draw");
            RenderFeatures.Mutate(flat.Id, f => f.Enabled = true);

            // A broken save keeps the last good shader; fixing it reloads (the per-frame source poll).
            File.WriteAllText(RenderFeatures.ShaderSourcePath(flat), FlatShader.Replace("return Color * Intensity;", "return Colour;"));
            File.SetLastWriteTimeUtc(RenderFeatures.ShaderSourcePath(flat), DateTime.UtcNow.AddSeconds(10));
            PollUntil(() => flat.Status == RenderFeatureStatus.Compiling);
            WaitForCompile(flat);
            Check(flat.Status == RenderFeatureStatus.Error && flat.Error.Contains("Flat.fx(") && flat.Effect != null &&
                  Near(Run(RenderFeatureEvent.AfterBloom)[0], 64, 64, 64), $"a shader error is reported with its line and the last good version keeps running ({flat.Error})");
            File.WriteAllText(RenderFeatures.ShaderSourcePath(flat), FlatShader.Replace("return Color * Intensity;", "return float4(1, 0, 0, 1);"));
            File.SetLastWriteTimeUtc(RenderFeatures.ShaderSourcePath(flat), DateTime.UtcNow.AddSeconds(15));
            PollUntil(() => flat.Status == RenderFeatureStatus.Compiling);
            WaitForCompile(flat);
            RenderFeatures.Mutate(flat.Id, f => f.Blend = RenderFeatureBlend.Replace);
            Check(flat.Status == RenderFeatureStatus.Ready && Near(Run(RenderFeatureEvent.AfterBloom)[0], 255, 0, 0),
                "saving the .fx hot-reloads it");

            // A parameter of another type than the engine sets (a texture named Intensity) stops the feature instead of throwing every frame.
            File.WriteAllText(RenderFeatures.ShaderSourcePath(flat), FlatShader
                .Replace("float Intensity;", "Texture2D Intensity; SamplerState IntensitySampler { Texture = <Intensity>; };")
                .Replace("return Color * Intensity;", "return Color * Intensity.Sample(IntensitySampler, float2(0.5f, 0.5f));"));
            RenderFeatures.Recompile(flat.Id);
            WaitForCompile(flat);
            Run(RenderFeatureEvent.AfterBloom);
            Check(flat.Status == RenderFeatureStatus.Error && flat.Error.StartsWith("Draw failed") && !flat.IsActive,
                $"a feature that fails to draw is switched off with the reason ({flat.Error})");

            // Reloading from disk loads the compiled .xnb without compiling again.
            RenderFeatures.Load(content);
            RenderFeature reloaded = RenderFeatures.Find("Vignette");
            Check(reloaded.Status == RenderFeatureStatus.Ready && reloaded.Effect != null, "a saved feature loads its compiled shader at startup");
        }
        finally
        {
            GameSettings.e_enableeditor = editor;
            RenderFeatures.SourceRootOverride = null;
            RenderFeatures.Load(content);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private const string FlatShader = @"
float Intensity;
float4 Color;
Texture2D ScreenTexture;
struct VertexInput { float2 Position : POSITION0; };
struct VertexOutput { float4 Position : SV_POSITION; };
VertexOutput MainVS(VertexInput input) { VertexOutput o; o.Position = float4(input.Position, 0, 1); return o; }
float4 MainPS(VertexOutput input) : SV_TARGET { return Color * Intensity; }
technique Main { pass P0 { VertexShader = compile vs_5_0 MainVS(); PixelShader = compile ps_5_0 MainPS(); } }
";

    private static void WaitForCompile(RenderFeature feature)
    {
        DateTime end = DateTime.UtcNow.AddMinutes(2);
        while (feature.Status == RenderFeatureStatus.Compiling && DateTime.UtcNow < end)
        {
            Thread.Sleep(50);
            RenderFeatures.Update();
        }
    }

    // Frames until the source poll (every 30 frames) notices a saved .fx.
    private static void PollUntil(Func<bool> condition)
    {
        for (int i = 0; i < 120 && !condition(); i++) RenderFeatures.Update();
        Check(condition(), "the per-frame poll notices the saved shader");
    }

    private static bool Near(Color c, int r, int g, int b, int tolerance = 3) =>
        Math.Abs(c.R - r) <= tolerance && Math.Abs(c.G - g) <= tolerance && Math.Abs(c.B - b) <= tolerance;

    private static bool Before(List<string> names, string first, string second) =>
        names.IndexOf(first) >= 0 && names.IndexOf(first) < names.IndexOf(second);

    private static IEnumerable<string> Rows(RenderPipelineLayerKind kind) =>
        RenderFeatures.Describe().Where(l => l.Kind == kind).Select(l => l.Name);

    private static IEnumerable<string> Next(IReadOnlyList<RenderPipelineLayer> layers, string point, int count)
    {
        int index = layers.ToList().FindIndex(l => l.Name == point);
        return layers.Skip(index + 1).Take(count).Select(l => l.Name);
    }

    private static void Drain(EditorBridge bridge)
    {
        for (int i = 0; i < 3; i++) Invoke(bridge, "DrainAndPublish");
        RunUiJobs();
    }

    private static void RunUiJobs()
    {
        try { Dispatcher.UIThread.RunJobs(); } catch (InvalidOperationException) { }
    }

    private static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }

    private static void Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
