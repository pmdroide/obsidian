using System.Diagnostics;
using System.Globalization;
using AngleSharp.Dom;
using Engine.Logic;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// The SceneLoadingTest sample (on the Main Camera of SceneLoadingTest and SceneLoadingArena): loads scenes the
/// three ways the engine can and measures what each costs, with the HUD in Content/UI/SceneLoadingTest.xml.
///
///   Loading screen  GameFlow.LoadSceneAsync(name): built over frames behind the engine's loading screen
///   Background      GameFlow.LoadSceneAsync(name, loadingScreen: false): no screen, this scene keeps
///                   playing and the HUD shows the load's progress
///   Blocking        GameFlow.LoadScene(name): read, build and swap in one frame
///
/// The frame-time graph measures every frame with a stopwatch (its history survives the swap, so a blocking
/// load's hitch shows in the next scene), and "Last loads" lists GameFlow.LastLoad's timings.
/// Keys: Up/Down choose, Enter load (and start a load held at Ready), M mode, H hold at ready, B build budget,
/// Esc cancel.
/// </summary>
public sealed class SceneLoadingTestScript : ScriptBehaviour
{
    public const string ScriptId = "scene-loading-test";

    private const string DocumentPath = "UI/SceneLoadingTest";
    private const int GraphBars = 120, BarWidth = 5, GraphHeight = 140, DestRowHeight = 44, ReportRowHeight = 30, MaxReports = 5;
    private const float GraphMaxMs = 100f, LiveBarWidth = 720f;

    private enum LoadMode { LoadingScreen, Background, Blocking }
    private static readonly string[] ModeNames = { "Loading screen", "Background", "Blocking" };

    private static readonly (string Scene, string Label, string Note)[] Destinations =
    {
        ("SceneLoadingArena", "Arena", "1,550 gameobjects"),
        ("SceneLoadingTest", "Hub", "this sample's start"),
        ("AutoExposureTest", "Auto Exposure Test", "1.5 MB baked lighting"),
        ("WaterTest", "Water Test", "pool, waves, rain"),
    };
    private static readonly float[] Budgets = { 1f, 2f, 4f, 8f, 16f };

    // Statics survive the scene swap: the choices, the frame history and the reports carry into the next scene.
    private static int _selected, _budgetIndex = 2;
    private static LoadMode _mode;
    private static bool _hold;
    private static readonly float[] FrameMs = new float[GraphBars];
    private static int _frameHead;
    private static long _lastStamp;
    private static readonly List<SceneLoadOperation> Reports = new();
    private static SceneLoadOperation _reported;

    private UIManager _ui;
    private readonly List<IElement> _destRows = new(), _bars = new(), _reportRows = new();
    private UIElement _cursor;
    private string _message = "";
    private float _messageTime;

    public override void Start()
    {
        _ui = GameUI.Open(DocumentPath);
        if (_ui == null) return;
        GameFlow.EscapeQuits = false;
        _ui.Loaded += Build; // CSS/XML hot reload rebuilds the dynamic parts
        Build();
    }

    public override void Stop()
    {
        GameFlow.EscapeQuits = true;
        GameFlow.LoadBudgetMs = 4f; // engine-wide; only this sample changes it
        if (_ui == null) return;
        _ui.Loaded -= Build;
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void Update()
    {
        RecordFrame();
        if (_ui == null) return;
        CollectReport();
        if (!DebugScreen.ConsoleOpen) HandleInput();
        Show();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  LOADING
    ////////////////////////////////////////////////////////////////////////////////

    private void HandleInput()
    {
        SceneLoadOperation load = GameFlow.CurrentLoad;
        if (GameInput.WasPressed(Keys.Escape) || GameInput.WasPressed(Keys.C))
        {
            if (load != null) Say(GameFlow.CancelLoad() ? $"Canceled loading {load.SceneName}" : "Too late to cancel: the scene already swapped");
            return;
        }

        if (GameInput.WasPressed(Keys.Up) || GameInput.WasPressed(Keys.W)) _selected = (_selected + Destinations.Length - 1) % Destinations.Length;
        if (GameInput.WasPressed(Keys.Down) || GameInput.WasPressed(Keys.S)) _selected = (_selected + 1) % Destinations.Length;
        if (GameInput.WasPressed(Keys.M)) _mode = (LoadMode)(((int)_mode + 1) % ModeNames.Length);
        if (GameInput.WasPressed(Keys.H)) _hold = !_hold;
        if (GameInput.WasPressed(Keys.B)) _budgetIndex = (_budgetIndex + 1) % Budgets.Length;

        if (!GameInput.WasPressed(Keys.Enter) && !GameInput.WasPressed(Keys.Space)) return;
        // A load held at Ready swaps on the next Enter.
        if (load is { Status: SceneLoadStatus.WaitingForActivation, AllowSceneActivation: false })
        {
            load.AllowSceneActivation = true;
            return;
        }
        StartLoad();
    }

    private void StartLoad()
    {
        string scene = Destinations[_selected].Scene;
        GameFlow.LoadBudgetMs = Budgets[_budgetIndex];
        if (_mode == LoadMode.Blocking)
        {
            if (!GameFlow.LoadScene(scene)) Say($"Scene '{scene}' not found");
            return;
        }
        SceneLoadOperation load = GameFlow.LoadSceneAsync(scene, loadingScreen: _mode == LoadMode.LoadingScreen);
        if (load == null)
        {
            Say($"Scene '{scene}' not found");
            return;
        }
        load.AllowSceneActivation = !_hold;
        load.ReadyMessage = "Ready - press Enter to start";
        load.Completed += done =>
        {
            // Runs after the swap too (this script has stopped by then), so it only logs.
            if (done.Status != SceneLoadStatus.Done) EditorLog($"{done.SceneName}: {done.Status} {done.Error}");
        };
    }

    private static void EditorLog(string text) => global::Engine.Editor.EditorBridge.Log("SceneLoadingTest: " + text);

    private void CollectReport()
    {
        SceneLoadOperation last = GameFlow.LastLoad;
        if (last == null || ReferenceEquals(last, _reported)) return;
        _reported = last;
        Reports.Insert(0, last);
        if (Reports.Count > MaxReports) Reports.RemoveAt(Reports.Count - 1);
    }

    private void Say(string text)
    {
        _message = text;
        _messageTime = 4f;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  FRAME TIME
    ////////////////////////////////////////////////////////////////////////////////

    // Wall-clock time between this script's Updates. Across a swap it runs from the old scene's last frame to the
    // new scene's first, so the swap (and a blocking load's whole work) lands in one bar.
    private static void RecordFrame()
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastStamp != 0)
        {
            float ms = (float)((now - _lastStamp) * 1000.0 / Stopwatch.Frequency);
            // A gap of seconds is Play stopped and started again, not a frame.
            if (ms < 5000f)
            {
                FrameMs[_frameHead] = ms;
                _frameHead = (_frameHead + 1) % GraphBars;
            }
        }
        _lastStamp = now;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  HUD
    ////////////////////////////////////////////////////////////////////////////////

    private void Build()
    {
        _destRows.Clear();
        IElement list = _ui.Query("#dest-list");
        for (int i = 0; i < Destinations.Length; i++)
        {
            IElement row = Div("dest");
            row.SetAttribute("style", $"top: {i * DestRowHeight}px");
            row.AppendChild(Div("hl"));
            row.AppendChild(Div("bar"));
            row.AppendChild(Div("label", Destinations[i].Label));
            row.AppendChild(Div("note", Destinations[i].Note));
            list?.AppendChild(row);
            _destRows.Add(row);
        }

        _bars.Clear();
        IElement graph = _ui.Query("#graph");
        for (int i = 0; i < GraphBars; i++)
        {
            IElement bar = Div("fbar");
            graph?.AppendChild(bar);
            _bars.Add(bar);
        }
        IElement cursor = Div(null);
        cursor.Id = "cursor";
        graph?.AppendChild(cursor);

        _reportRows.Clear();
        IElement table = _ui.Query("#report-rows");
        for (int i = 0; i < MaxReports; i++)
        {
            IElement row = Div("rrow");
            row.SetAttribute("style", $"top: {i * ReportRowHeight}px");
            foreach (string column in new[] { "c-scene", "c-mode", "c-time", "c-frames", "c-worst", "c-swap" })
                row.AppendChild(Div("cell " + column));
            table?.AppendChild(row);
            _reportRows.Add(row);
        }

        _ui.Refresh();
        _cursor = _ui.Get("#cursor");
        for (int i = 0; i < GraphBars; i++) DrawBar(i);
    }

    private void Show()
    {
        _ui.SetText("#title", Spaced(GameFlow.ActiveSceneName));
        for (int i = 0; i < _destRows.Count; i++) _ui.SetClass(_destRows[i], "selected", i == _selected);
        _ui.SetText("#opt-mode", ModeNames[(int)_mode]);
        _ui.SetText("#opt-hold", _hold ? "On - waits at 90% for Enter" : "Off");
        _ui.SetText("#opt-budget", _mode == LoadMode.Blocking
            ? "unused when blocking"
            : $"{Budgets[_budgetIndex].ToString(CultureInfo.InvariantCulture)} ms per frame");

        ShowGraph();
        ShowLive();
        ShowReports();

        _messageTime -= DeltaTime;
        _ui.SetText("#message", _messageTime > 0f ? _message : "");
    }

    private void ShowGraph()
    {
        int newest = (_frameHead + GraphBars - 1) % GraphBars;
        DrawBar(newest);
        if (_cursor != null) _cursor.RuntimeOffset = new Microsoft.Xna.Framework.Vector2(_frameHead * BarWidth, 0f);

        float worst = 0f;
        for (int i = 0; i < GraphBars; i++) worst = Math.Max(worst, FrameMs[i]);
        float current = FrameMs[newest];
        _ui.SetText("#frame-now", current > 0f ? $"{current:0.0} ms  ·  {1000f / current:0} fps" : "");
        _ui.SetText("#frame-worst", $"worst {worst:0.0} ms");
    }

    private void DrawBar(int index)
    {
        if (index >= _bars.Count) return;
        float ms = FrameMs[index];
        int height = (int)Math.Clamp(ms / GraphMaxMs * GraphHeight, ms > 0f ? 1f : 0f, GraphHeight);
        string tone = ms > 50f ? "bad" : ms > 20f ? "warn" : "ok";
        IElement bar = _bars[index];
        if (bar.ClassName != "fbar " + tone) bar.ClassName = "fbar " + tone;
        bar.SetAttribute("style", $"left: {index * BarWidth}px; top: {GraphHeight - height}px; height: {height}px");
        _ui.Invalidate(bar);
    }

    // The progress of a load this HUD can still see (the loading screen covers it otherwise).
    private void ShowLive()
    {
        SceneLoadOperation load = GameFlow.CurrentLoad;
        _ui.SetClass("#live", "shown", load != null && !load.Blocking);
        if (load == null || load.Blocking) return;

        _ui.SetText("#live-scene", $"{Spaced(load.SceneName)}  ·  {(load.ShowLoadingScreen ? "loading screen" : "background")}");
        _ui.SetText("#live-status", load.Status switch
        {
            SceneLoadStatus.Reading => $"Reading {Size(load.BytesRead)} of {Size(load.TotalBytes)} on a worker thread",
            SceneLoadStatus.Building => $"Building {load.ObjectsBuilt:N0} of {load.ObjectCount:N0} objects",
            SceneLoadStatus.WaitingForActivation => load.AllowSceneActivation ? "Swapping next frame" : "Ready - press Enter to start, Esc to cancel",
            _ => "Starting the scene",
        });
        _ui.SetText("#live-percent", $"{(int)(load.Progress * 100f)}%");
        _ui.SetText("#live-time", $"{load.Seconds:0.00} s  ·  {load.Frames} frames");
        _ui.SetVariable("#live-fill", "style", $"width: {(int)(load.Progress * LiveBarWidth)}px");
    }

    private void ShowReports()
    {
        _ui.SetText("#reports-empty", Reports.Count == 0 ? "Load a scene to measure it." : "");
        for (int i = 0; i < _reportRows.Count; i++)
        {
            IElement row = _reportRows[i];
            SceneLoadOperation r = i < Reports.Count ? Reports[i] : null;
            SetCell(row, "c-scene", r == null ? "" : Spaced(r.SceneName));
            SetCell(row, "c-mode", r == null ? "" : r.Blocking ? "Blocking" : r.ShowLoadingScreen ? "Screen" : "Background");
            SetCell(row, "c-time", r == null ? "" : $"{r.Seconds - r.WaitedSeconds:0.00} s");
            SetCell(row, "c-frames", r == null ? "" : r.Frames.ToString(CultureInfo.InvariantCulture));
            SetCell(row, "c-worst", r == null ? "" : $"{r.LongestFrameMs:0} ms");
            SetCell(row, "c-swap", r == null ? "" : $"{r.ActivationMs:0} ms");
            _ui.SetClass(row.QuerySelector(".c-worst"), "bad", r != null && r.LongestFrameMs > 50);
        }
    }

    private static void SetCell(IElement row, string column, string text)
    {
        IElement cell = row.QuerySelector("." + column);
        if (cell != null && cell.TextContent != text) cell.TextContent = text;
    }

    private IElement Div(string classes, string text = null)
    {
        IElement element = _ui.Document.CreateElement("div");
        if (classes != null) element.ClassName = classes;
        if (text != null) element.TextContent = text;
        return element;
    }

    private static string Size(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" : $"{Math.Max(1, (bytes + 1023) / 1024)} KB";

    // "SceneLoadingArena" -> "Scene Loading Arena"
    private static string Spaced(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var text = new System.Text.StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1])) text.Append(' ');
            text.Append(name[i]);
        }
        return text.ToString();
    }
}
