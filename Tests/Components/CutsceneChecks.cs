using System.Text.Json;
using Engine.Logic;
using Microsoft.Xna.Framework;

/// <summary>
/// Cutscenes without a game running: camera track evaluation (ends, Catmull-Rom through keys, cuts, yaw wrapping),
/// fades and subtitles at a time, path normalising, and a JSON round trip in the scene file format.
/// </summary>
internal static class CutsceneChecks
{
    public static void Run()
    {
        var cutscene = new Cutscene
        {
            Duration = 6,
            CameraKeys =
            {
                new CutsceneCameraKey { Time = 2, Position = new Vector3(10, 0, 0), Yaw = 170, Fov = 60, Smooth = false },
                new CutsceneCameraKey { Time = 0, Position = Vector3.Zero, Yaw = 0, Fov = 40, Smooth = false },
                new CutsceneCameraKey { Time = 4, Position = new Vector3(0, 50, 0), Yaw = -170, Cut = true },
            },
            Events =
            {
                new CutsceneEvent { Time = 0, Duration = 1, Kind = CutsceneEventKind.FadeIn },
                new CutsceneEvent { Time = 1, Duration = 2, Kind = CutsceneEventKind.Subtitle, Text = "Hello" },
                new CutsceneEvent { Time = 5, Duration = 1, Kind = CutsceneEventKind.FadeOut },
            },
        };

        var start = cutscene.EvaluateCamera(-1)!.Value;
        Check(start.Position == Vector3.Zero && start.Fov == 40, "before the first key the camera holds the first key (keys sorted by time)");
        var mid = cutscene.EvaluateCamera(1)!.Value;
        Check(Math.Abs(mid.Position.X - 5) < 0.6f && Math.Abs(mid.Fov - 50) < 0.01f, "halfway between keys the position and FOV blend");
        var held = cutscene.EvaluateCamera(3.9f)!.Value;
        Check(held.Position == new Vector3(10, 0, 0), "a Cut key holds the previous shot until its time");
        var cut = cutscene.EvaluateCamera(4)!.Value;
        Check(cut.Position == new Vector3(0, 50, 0), "at a Cut key the camera jumps to it");
        Check(Math.Abs(Cutscene.WrapDegrees(-170 - 170) - 20) < 1e-3f, "yaw blends the short way round");
        Check(Vector3.Distance(new CutsceneCameraPose(Vector3.Zero, 90, 0, 45).Forward, Vector3.UnitY) < 1e-4f, "yaw 90 faces +Y (Z up)");
        Check(Cutscene.EvaluateCamera(new List<CutsceneCameraKey>(), 0) == null, "no keys: no camera pose");

        Check(Math.Abs(cutscene.FadeAt(0) - 1) < 1e-4f && Math.Abs(cutscene.FadeAt(0.5f) - 0.5f) < 1e-4f && Math.Abs(cutscene.FadeAt(3) - 0) < 1e-4f && Math.Abs(cutscene.FadeAt(5.5f) - 0.5f) < 1e-4f && Math.Abs(cutscene.FadeAt(6) - 1) < 1e-4f,
            "Fade In comes up from black and Fade Out darkens over their durations");
        Check(cutscene.SubtitleAt(2) == "Hello" && cutscene.SubtitleAt(3.5f) == null, "subtitles show for their duration");

        Check(Cutscene.Normalize("Intro") == "Cutscenes/Intro.cutscene" && Cutscene.Normalize("Story\\A.cutscene") == "Story/A.cutscene",
            "cutscene paths normalise to Content-relative .cutscene paths");

        string json = JsonSerializer.Serialize(cutscene, SceneSerialization.JsonOptions);
        var loaded = JsonSerializer.Deserialize<Cutscene>(json, SceneSerialization.JsonOptions)!;
        Check(loaded.CameraKeys.Count == 3 && loaded.CameraKeys[0].Position == new Vector3(10, 0, 0) && loaded.CameraKeys[2].Cut
              && loaded.Events[1].Kind == CutsceneEventKind.Subtitle && json.Contains("\"Subtitle\""),
            "cutscenes round-trip through JSON with event kinds saved by name");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
