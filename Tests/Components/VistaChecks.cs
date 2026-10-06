using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Dom;
using Microsoft.Xna.Framework;
using Vista;

/// <summary>Vista layout, styles and hit testing on a parsed document (no graphics device needed).</summary>
internal static class VistaChecks
{
    public static void Run()
    {
        // A 1280x720 render device under a 1920x1080 layout canvas, like a GameUI layer scaled to a 720p window.
        const string css = @"
            html, body { margin: 0; padding: 0; }
            #panel { left: 50%; margin-left: -100px; width: 200px; bottom: 10%; height: 50px; }
            #right { right: 20px; width: 100px; height: 40%; }
            #stretch { left: 10px; right: 30px; top: 0; height: 10px; }
            #ghost { pointer-events: none; }
            #gone { display: none; }
            .fade { opacity: 0; transition: opacity 0.2s; }
            .fade.shown { opacity: 1; }
            #shade { background-image: radial-gradient(ellipse at center, rgba(0, 0, 0, 0), rgba(0, 0, 0, 0.8)); }
            #bar { background-image: linear-gradient(to right, #ff0000, #0000ff); border-left: 3px solid #E9A94B; }
            #label { text-align: center; vertical-align: middle; letter-spacing: 4px; color: white; }
            #shade, #bar, #label { top: 0; width: 100px; height: 20px; }";
        const string xml = @"<div id='root'>
            <div id='panel'><div id='ghost'></div></div>
            <div id='right'></div>
            <div id='stretch'></div>
            <div id='gone'></div>
            <div id='fader' class='fade'></div>
            <div id='shade'></div>
            <div id='bar'></div>
            <div id='label'>Hi</div>
        </div>";

        var device = new VistaRenderDevice { ViewPortWidth = 1280, ViewPortHeight = 720, DeviceWidth = 1280, DeviceHeight = 720, RenderWidth = 1280, RenderHeight = 720 };
        var context = BrowsingContext.New(Configuration.Default.WithCss().WithRenderDevice(device));
        IDocument doc = context.OpenAsync(r => r.Content($"<style>{UIStyles.RewriteRadialGradients(css)}</style>{xml}")).GetAwaiter().GetResult();
        var views = new Dictionary<IElement, UIElement>();
        UIElement body = UIManager.BuildTree(doc.Body!, null, views, null);
        UIElement View(string id) => views[doc.GetElementById(id)!];
        void Layout(float dt) => body.CalculateLayout(new Vector4(0, 0, 1920, 1080), 1f, dt);
        Layout(0);

        Check(View("panel").ScreenBounds == new Rectangle(860, 922, 200, 50),
            "percent left/bottom resolve against the parent box, not AngleSharp's render device");
        Check(View("right").ScreenBounds == new Rectangle(1800, 0, 100, 432) && View("stretch").ScreenBounds == new Rectangle(10, 0, 1880, 10),
            "right anchoring, percent heights and left+right stretching");

        Check(body.HitTest(new Vector2(900, 940)) == View("panel"),
            "hit testing returns the element under the point, skipping pointer-events: none children");
        Check(body.HitTest(new Vector2(5, 5)) != View("gone") && !View("gone").IsVisible, "display: none elements are neither drawn nor hit");

        UIElement fader = View("fader");
        Check(fader.EffectiveOpacity == 0f && body.HitTest(new Vector2(960, 540)) != fader, "transparent elements don't take clicks");
        doc.GetElementById("fader")!.ClassList.Add("shown");
        fader.MarkSubtreeDirty();
        Layout(0.05f);
        float mid = fader.EffectiveOpacity;
        Layout(0.3f);
        Check(mid > 0.2f && mid < 0.95f && fader.EffectiveOpacity == 1f,
            $"a class change transitions opacity over its transition-duration ({mid:0.00} after 0.05 s of 0.2 s)");

        Check(View("shade").Style.BackgroundGradient is { Kind: GradientKind.Radial } shade && shade.Stops.Count == 2 && shade.Stops[1].W > 0.79f,
            "radial-gradient survives AngleSharp (rewritten on load) and reads back as radial");
        var bar = View("bar").Style;
        Check(bar.BackgroundGradient is { Kind: GradientKind.Linear, Angle: 90 } && bar.BorderWidth[3] == 3 &&
              Math.Abs(bar.BorderColor[3].X - 0xE9 / 255f) < 0.01f,
            "linear gradients keep their direction; border longhands are read");
        var label = View("label").Style;
        Check(label.TextAlign == TextAlign.Center && label.VerticalAlign == VerticalAlign.Middle && label.LetterSpacing == 4 && label.Color == Vector4.One,
            "text alignment, letter spacing and colour are parsed");

        Check(UIStyles.ParseColorVector("#E9A94B80").W is > 0.49f and < 0.51f && UIStyles.ParseColorVector("rgba(255, 0, 0, 0.5)") == new Vector4(1, 0, 0, 0.5f) &&
              UIStyles.Premultiply(new Vector4(1, 1, 1, 0.5f), 0.5f) is { R: >= 63 and <= 64, A: >= 63 and <= 64 },
            "colours parse (hex with alpha, rgba) and premultiply for SpriteBatch");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
