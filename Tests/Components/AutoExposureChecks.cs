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
        var metering = GameSettings.g_ExposureMetering;
        float brightPercent = GameSettings.g_BrightPixelPercent, brightTarget = GameSettings.g_BrightPixelTarget,
            minAverage = GameSettings.g_MinAverageLuminance;
        GameSettings.g_ExposureMetering = Engine.Logic.ExposureMetering.Average;

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

            // Bright pixel metering (Source's tonemap controller): the brightest 2 % land on the 0.18 target.
            GameSettings.g_ExposureMetering = Engine.Logic.ExposureMetering.BrightPixels;
            GameSettings.g_BrightPixelPercent = 2;
            GameSettings.g_BrightPixelTarget = 0.18f;
            GameSettings.g_MinAverageLuminance = 0;
            GameSettings.g_AutoExposureCenterWeight = 0;
            GameSettings.g_AutoExposureMin = -8;
            GameSettings.g_AutoExposureMax = 8;
            Fill(frame, 0.72f);
            filter.Reset();
            Check(MathF.Abs(Draw(filter, device, frame, 0.016f) + 2) < 0.35f, "bright pixel metering exposes a flat frame like the average");
            // A dim room (0.045) with a bright window on the top 3 rows (7.2, ~5 % of the frame)
            var room = new Vector4[64 * 64];
            for (int i = 0; i < room.Length; i++)
            {
                float l = i < 3 * 64 ? 7.2f : 0.045f;
                room[i] = new Vector4(l, l, l, 1);
            }
            frame.SetData(room);
            filter.Reset();
            float highlights = Draw(filter, device, frame, 0.016f);
            Check(MathF.Abs(highlights - MathF.Log2(0.18f / 7.2f)) < 0.4f,
                "bright pixel metering exposes the window, not the dim room (no blown-out view out of a dark room)");
            GameSettings.g_MinAverageLuminance = 0.03f;
            filter.Reset();
            float floor = Draw(filter, device, frame, 0.016f);
            float averageLog = (3 * MathF.Log2(7.2f) + 61 * MathF.Log2(0.045f)) / 64;
            Check(floor > highlights + 3 && MathF.Abs(floor - (MathF.Log2(0.03f) - averageLog)) < 0.4f,
                "Min Average Luminance keeps the room from going black");
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
            GameSettings.g_ExposureMetering = metering;
            GameSettings.g_BrightPixelPercent = brightPercent;
            GameSettings.g_BrightPixelTarget = brightTarget;
            GameSettings.g_MinAverageLuminance = minAverage;
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
