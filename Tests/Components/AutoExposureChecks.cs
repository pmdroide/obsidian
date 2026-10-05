using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules.PostProcessingFilters;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

// Feeds flat HDR frames of known luminance through the eye adaptation passes and reads the adapted EV back.
internal static class AutoExposureChecks
{
    public static void RunGraphics(GraphicsDevice device, ContentManager content)
    {
        float key = GameSettings.g_AutoExposureKey, min = GameSettings.g_AutoExposureMin,
            max = GameSettings.g_AutoExposureMax, up = GameSettings.g_AutoExposureSpeedDarkToLight,
            down = GameSettings.g_AutoExposureSpeedLightToDark, center = GameSettings.g_AutoExposureCenterWeight;
        GameSettings.g_AutoExposureKey = 0.18f;
        GameSettings.g_AutoExposureMin = -3;
        GameSettings.g_AutoExposureMax = 3;
        GameSettings.g_AutoExposureSpeedDarkToLight = 3;
        GameSettings.g_AutoExposureSpeedLightToDark = 1;
        GameSettings.g_AutoExposureCenterWeight = 0.5f;

        var triangle = new FullScreenTriangle(device);
        using var filter = new AutoExposureFilter(content, "Shaders/PostProcessing/AutoExposure");
        using var frame = new Texture2D(device, 64, 64, false, SurfaceFormat.Vector4);
        try
        {
            filter.Initialize(device, triangle);

            // 0.72 is two stops above the 0.18 key, so the first (snapped) frame exposes by -2 EV.
            Fill(frame, 0.72f);
            Check(Near(Draw(filter, device, frame, 0.016f), -2), "auto exposure snaps to the metered EV on the first frame");

            // Darker scene: target +2 EV, approached at the light-to-dark speed (1/s) over 0.1 s.
            Fill(frame, 0.045f);
            float expected = -2 + 4 * (1 - MathF.Exp(-0.1f));
            Check(Near(Draw(filter, device, frame, 0.1f), expected), "auto exposure adapts gradually toward a darker scene");

            float adapted = 0;
            for (int i = 0; i < 200; i++) adapted = Draw(filter, device, frame, 0.1f);
            Check(Near(adapted, 2), "auto exposure settles on the target EV");

            // Back to bright: the dark-to-light speed (3/s) is faster than the way down.
            Fill(frame, 0.72f);
            Check(Near(Draw(filter, device, frame, 0.1f), 2 - 4 * (1 - MathF.Exp(-0.3f))),
                "brightening adapts at the dark-to-light speed");

            // A very bright frame wants about -9 EV; the Min EV clamp holds it at -3.
            Fill(frame, 100);
            filter.Reset();
            Check(Near(Draw(filter, device, frame, 0.016f), -3), "auto exposure respects the Min EV clamp");

            // NaN/Inf pixels from the HDR buffer must not poison the average.
            var pixels = Enumerable.Repeat(new Vector4(0.72f, 0.72f, 0.72f, 1), 64 * 64).ToArray();
            pixels[0] = new Vector4(float.NaN);
            pixels[1] = new Vector4(float.PositiveInfinity);
            frame.SetData(pixels);
            filter.Reset();
            float withNaN = Draw(filter, device, frame, 0.016f);
            Check(!float.IsNaN(withNaN) && withNaN > -2.2f && withNaN < -1.8f, "invalid HDR pixels are ignored by the meter");
        }
        finally
        {
            triangle.Dispose();
            device.SetRenderTarget(null);
            GameSettings.g_AutoExposureKey = key;
            GameSettings.g_AutoExposureMin = min;
            GameSettings.g_AutoExposureMax = max;
            GameSettings.g_AutoExposureSpeedDarkToLight = up;
            GameSettings.g_AutoExposureSpeedLightToDark = down;
            GameSettings.g_AutoExposureCenterWeight = center;
        }
    }

    private static float Draw(AutoExposureFilter filter, GraphicsDevice device, Texture2D frame, float delta)
    {
        var exposure = (RenderTarget2D)filter.Draw(device, frame, delta);
        device.SetRenderTarget(null);
        var value = new float[1];
        exposure.GetData(value);
        return value[0];
    }

    private static void Fill(Texture2D frame, float luminance) =>
        frame.SetData(Enumerable.Repeat(new Vector4(luminance, luminance, luminance, 1), 64 * 64).ToArray());

    private static bool Near(float value, float expected) => MathF.Abs(value - expected) < 0.01f;

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
