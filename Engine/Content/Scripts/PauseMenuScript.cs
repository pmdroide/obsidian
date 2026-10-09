using AngleSharp.Dom;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// A pause menu for the whole game from one script on a persistent gameobject. The PersistenceTest samples put it
/// on the Player, next to Persistent Player. Because the gameobject is persistent, this same running instance comes
/// along on every scene load, so it pauses each scene and its session numbers (time played, scene loads, the route)
/// keep counting from scene to scene.
///
/// Esc (or gamepad Start) pauses: <see cref="GameFlow.Paused"/> freezes every other script, component and
/// physics body, while this one keeps running (<see cref="UpdateWhilePaused"/>). Its Vista layer
/// (Content/UI/PauseMenu.xml + .css) opens on pause and closes on resume, so it is always the top layer.
///   Resume · Restart scene (reload; persistent gameobjects carry on) · Settings ·
///   Main menu (scene list entry 0, persistent gameobjects left behind) · Quit game
///   Keyboard  arrows or WASD, Enter/Space, Esc/Backspace (back, resume)
///   Mouse     hover, click, wheel, right click (back)
///   Gamepad   D-pad or left stick, A, B (back), Start (resume)
/// </summary>
public sealed class PauseMenuScript : ScriptBehaviour
{
    public const string ScriptId = "pause-menu";

    private const string DocumentPath = "UI/PauseMenu";
    private const string TickSound = "Audio/blip.wav";
    private const int RowHeight = 62, SettingRowHeight = 54;
    private const float CloseSeconds = 0.2f, LeaveSeconds = 0.55f, ArriveSeconds = 0.9f;

    /// <summary>The pause page's rows, in order (each matches a <c>data-action</c> in the document).</summary>
    private static readonly string[] PauseActions = { "resume", "restart", "settings", "main-menu", "quit" };

    [Tooltip("Pauses and resumes the game. Gamepad Start always does.")]
    public Keys PauseKey = Keys.Escape;
    [Tooltip("Show the session card: time played, scene loads and the route, kept across scene loads.")]
    public bool ShowSession = true;

    /// <summary>Keeps running while the game is paused: this script is the pause menu.</summary>
    public override bool UpdateWhilePaused => true;

    /// <summary>Seconds of unpaused play since this instance started, across scene loads.</summary>
    public float TimePlayed { get; private set; }
    /// <summary>Seconds of unpaused play in the current scene.</summary>
    public float SceneTime { get; private set; }
    /// <summary>Scene loads this instance came through (<see cref="OnSceneLoaded"/>).</summary>
    public int ScenesLoaded { get; private set; }
    /// <summary>How often the game was paused this session.</summary>
    public int Pauses { get; private set; }
    /// <summary>The scenes visited, in order.</summary>
    public IReadOnlyList<string> Route => _route;
    /// <summary>True from pausing until the menu has closed again.</summary>
    public bool IsMenuOpen => _state != State.Closed;

    private enum State { Closed, Open, Closing, Leaving, Arriving }
    private enum Page { Pause, Settings }
    private enum Confirm { None, Restart, MainMenu, Quit }

    private readonly List<string> _route = new();
    private State _state = State.Closed;
    private Page _page = Page.Pause;
    private readonly Dictionary<Page, int> _selected = new();
    private Confirm _confirm = Confirm.None;
    private int _confirmChoice;
    private float _timer;
    private Func<bool> _afterLeave;
    private List<MenuSetting> _settings;

    // The layer exists only while the menu is open. _shown: its first layout is done, so classes now transition.
    private UIManager _ui;
    private bool _shown;
    private readonly Dictionary<Page, List<IElement>> _rows = new();
    private string _hints;

    public override void Start()
    {
        _route.Clear();
        _route.Add(GameFlow.ActiveSceneName ?? "?");
        _settings = new List<MenuSetting> { MenuSettings.MasterVolume() };
        _settings.AddRange(MenuSettings.Graphics());
        // Escape pauses instead of closing the standalone game; Quit game is in the menu.
        GameFlow.EscapeQuits = false;
    }

    public override void Stop()
    {
        CloseLayer();
        if (_state != State.Closed) GameFlow.Paused = false;
        _state = State.Closed;
        GameFlow.EscapeQuits = true;
    }

    public override void OnSceneLoaded()
    {
        ScenesLoaded++;
        SceneTime = 0f;
        _route.Add(GameFlow.ActiveSceneName ?? "?");

        // The load unpaused the game. After our own Restart, fade in from black; anything else just closes.
        if (_state == State.Leaving && _ui != null)
        {
            _state = State.Arriving;
            _timer = ArriveSeconds;
            _confirm = Confirm.None;
            _ui.SetClass("#root", "open", false);
            _ui.SetClass("#fade", "out", false);
            ApplyState();
            return;
        }
        CloseLayer();
        _state = State.Closed;
    }

    public override void Update()
    {
        switch (_state)
        {
            case State.Closed:
                if (GameFlow.Paused) return; // paused by something else: not our menu
                TimePlayed += DeltaTime;
                SceneTime += DeltaTime;
                if (!DebugScreen.ConsoleOpen && PausePressed()) Pause();
                return;

            case State.Open:
                // The scene only unpauses through this menu; if something else resumed it, step aside.
                if (!GameFlow.Paused)
                {
                    CloseLayer();
                    _state = State.Closed;
                    return;
                }
                if (!_shown) Show();
                if (!DebugScreen.ConsoleOpen) HandleInput();
                UpdateHints();
                return;

            case State.Closing:
                _timer -= DeltaTime;
                if (_timer > 0f) return;
                CloseLayer();
                _state = State.Closed;
                // Last, so no other script sees this frame's resume key (Space also jumps).
                GameFlow.Paused = false;
                return;

            case State.Leaving:
                if (_afterLeave == null) return; // waiting for the queued scene load
                _timer -= DeltaTime;
                if (_timer > 0f) return;
                Func<bool> action = _afterLeave;
                _afterLeave = null;
                if (action()) return;
                // The load couldn't be queued (missing file): stay paused in the menu.
                _state = State.Open;
                _ui?.SetClass("#fade", "out", false);
                return;

            case State.Arriving:
                TimePlayed += DeltaTime;
                SceneTime += DeltaTime;
                _timer -= DeltaTime;
                if (_timer > 0f) return;
                CloseLayer();
                _state = State.Closed;
                return;
        }
    }

    private bool PausePressed() => GameInput.WasPressed(PauseKey) || GameInput.WasPressed(Buttons.Start);

    ////////////////////////////////////////////////////////////////////////////////
    //  PAUSE AND RESUME
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>Freezes the game and opens the menu on its first page.</summary>
    public void Pause()
    {
        if (_state != State.Closed || GameFlow.Paused) return;
        GameFlow.Paused = true;
        Pauses++;
        _state = State.Open;
        _page = Page.Pause;
        _selected.Clear();
        _confirm = Confirm.None;
        _shown = false;
        _hints = null;
        Sound(0.8f, 0.35f);

        // Opened now, so it is above layers opened earlier (the player's HUD).
        _ui = GameUI.Open(DocumentPath);
        if (_ui == null) return; // the game still pauses; logged by GameUI
        _ui.Loaded += Build; // CSS/XML hot reload rebuilds the generated rows
        Build();
    }

    /// <summary>Closes the menu, then resumes the game.</summary>
    public void Resume()
    {
        if (_state != State.Open) return;
        Sound(1.2f, 0.35f);
        _state = State.Closing;
        _timer = CloseSeconds;
        _confirm = Confirm.None;
        _ui?.SetClass("#root", "open", false);
        ApplyState();
    }

    /// <summary>After the first layout, so the backdrop and screen fade in instead of snapping.</summary>
    private void Show()
    {
        _shown = true;
        _ui?.SetClass("#root", "open", true);
    }

    private void CloseLayer()
    {
        if (_ui == null) return;
        _ui.Loaded -= Build;
        GameUI.Close(_ui);
        _ui = null;
        _rows.Clear();
    }

    /// <summary>Fades to black, then runs <paramref name="action"/> (queue a scene load, quit). False: it failed.</summary>
    private void LeaveTo(Func<bool> action)
    {
        Sound(1f, 0.4f);
        _confirm = Confirm.None;
        _state = State.Leaving;
        _timer = LeaveSeconds;
        _afterLeave = action;
        _ui?.SetClass("#fade", "out", true);
        ApplyState();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  BUILD
    ////////////////////////////////////////////////////////////////////////////////

    private void Build()
    {
        if (_ui == null) return;
        IElement[] options = _ui.QueryAll("#pause-list .option").ToArray();
        _rows[Page.Pause] = PauseActions
            .Select(action => options.FirstOrDefault(o => o.GetAttribute("data-action") == action))
            .ToList();

        IElement settingsList = _ui.Query("#settings-list");
        _rows[Page.Settings] = new List<IElement>();
        foreach (MenuSetting setting in _settings)
        {
            IElement row = Div("option setting");
            row.AppendChild(Div("hl"));
            row.AppendChild(Div("bar"));
            row.AppendChild(Div("label", setting.Name));
            row.AppendChild(Div("arrow arrow-left", "‹"));
            row.AppendChild(Div("value", setting.ValueText));
            row.AppendChild(Div("arrow arrow-right", "›"));
            settingsList?.AppendChild(row);
            _rows[Page.Settings].Add(row);
        }
        Stack(_rows[Page.Pause], RowHeight);
        Stack(_rows[Page.Settings], SettingRowHeight);

        string scene = GameFlow.ActiveSceneName ?? "Untitled scene";
        int index = GameFlow.ActiveSceneIndex;
        _ui.SetText("#scene-line", index >= 0 ? $"{scene}  ·  Scene {index + 1} of {GameFlow.SceneCount}" : scene);
        _ui.SetText("#clock", $"Time stopped at {FormatTime(TimePlayed)} into the session");
        _ui.SetText("#main-menu-meta", MainMenuAvailable ? GameFlow.SceneName(0) : "");
        _ui.SetText("#quit-meta", GameFlow.IsEditor ? "Use Stop" : "");
        _ui.SetClass(_rows[Page.Pause][3], "disabled", !MainMenuAvailable);
        _ui.SetClass(_rows[Page.Pause][4], "disabled", !QuitAvailable);
        UpdateSession();

        // New rows and inline positions: rebuild the visual tree once.
        _ui.Refresh();
        if (_shown) _ui.SetClass("#root", "open", _state == State.Open || _state == State.Leaving);
        _hints = null;
        ApplyState();
    }

    private void UpdateSession()
    {
        _ui.SetClass("#session", "hidden", !ShowSession);
        _ui.SetText("#time-played", FormatTime(TimePlayed));
        _ui.SetText("#time-scene", FormatTime(SceneTime));
        _ui.SetText("#scene-loads", ScenesLoaded.ToString());
        _ui.SetText("#pauses", Pauses.ToString());
        _ui.SetText("#route", string.Join("  ›  ", _route.TakeLast(3)) + (_route.Count > 3 ? $"\n{_route.Count} scenes so far" : ""));
        string host = GameObject?.Name ?? "Main Camera";
        _ui.SetText("#session-note", IsPersistent
            ? $"Kept by this menu on the persistent {host}: the numbers carry across scene loads."
            : $"{host} isn't Persistent, so this session starts over when a scene loads.");
    }

    private IElement Div(string classes, string text = null)
    {
        IElement element = _ui.Document.CreateElement("div");
        element.ClassName = classes;
        if (text != null) element.TextContent = text;
        return element;
    }

    private static void Stack(List<IElement> rows, int height)
    {
        for (int i = 0; i < rows.Count; i++) rows[i]?.SetAttribute("style", $"top: {i * height}px");
    }

    /// <summary>"4:05", or "1:02:09" past an hour.</summary>
    private static string FormatTime(float seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }

    /// <summary>Scene list entry 0 exists and isn't the running scene.</summary>
    private static bool MainMenuAvailable =>
        GameFlow.SceneCount > 0 && GameFlow.ActiveSceneIndex != 0 && File.Exists(SceneList.ResolvePath(SceneList.Scenes[0]));

    /// <summary>Inside Anvil, Play ends with Stop instead.</summary>
    private static bool QuitAvailable => !GameFlow.IsEditor;

    ////////////////////////////////////////////////////////////////////////////////
    //  STATE -> CLASSES
    ////////////////////////////////////////////////////////////////////////////////

    private int Selected(Page page) => _selected.TryGetValue(page, out int i) ? i : 0;

    private int RowCount(Page page) => page == Page.Pause ? PauseActions.Length : _settings.Count;

    private void ApplyState()
    {
        if (_ui == null || _rows.Count == 0) return;
        _ui.SetClass("#pause-screen", "active", _page == Page.Pause);
        _ui.SetClass("#settings-screen", "active", _page == Page.Settings);
        foreach (var (page, rows) in _rows)
            for (int i = 0; i < rows.Count; i++)
                _ui.SetClass(rows[i], "selected", i == Selected(page));

        _ui.SetClass("#confirm", "open", _confirm != Confirm.None);
        _ui.SetClass("#confirm-cancel", "selected", _confirmChoice == 0);
        _ui.SetClass("#confirm-ok", "selected", _confirmChoice == 1);
        if (_confirm != Confirm.None)
        {
            string scene = GameFlow.ActiveSceneName ?? "the scene";
            (string title, string text, string ok) = _confirm switch
            {
                Confirm.Restart => ("Restart the scene?",
                    $"{scene} reloads from its file. Persistent gameobjects carry on as they are.", "Restart"),
                Confirm.MainMenu => ("Return to the main menu?",
                    "Progress in this scene is lost, and persistent gameobjects stay behind.", "Main menu"),
                _ => ("Quit to desktop?", "The game will close.", "Quit"),
            };
            _ui.SetText("#confirm-title", title);
            _ui.SetText("#confirm-text", text);
            _ui.SetText("#confirm-ok", ok);
        }

        UpdateDescriptions();
        UpdateHints();
    }

    private void UpdateDescriptions()
    {
        string scene = GameFlow.ActiveSceneName ?? "the scene";
        string pause = PauseActions[Selected(Page.Pause)] switch
        {
            "resume" => $"Back to {scene}, exactly where you left it.",
            "restart" => IsPersistent
                ? $"Reload {scene} from its file. Persistent gameobjects, this menu included, carry on."
                : $"Reload {scene} from its file.",
            "settings" => "Sound, display and rendering options for this session.",
            "main-menu" => MainMenuAvailable
                ? $"Load {GameFlow.SceneName(0)}. Persistent gameobjects stay behind, so the next run starts fresh."
                : GameFlow.SceneCount == 0 ? "The scene list is empty." : "This is already the first scene in the scene list.",
            "quit" => QuitAvailable ? "Close the game." : "Inside Anvil, press Stop to leave Play mode.",
            _ => "",
        };
        _ui.SetText("#pause-desc", pause);
        _ui.SetText("#settings-desc", _settings.ElementAtOrDefault(Selected(Page.Settings))?.Description ?? "");
    }

    private void UpdateHints()
    {
        if (_ui == null) return;
        (string key, string label)[] hints;
        if (_confirm != Confirm.None)
            hints = new[] { ("← →", "Choose"), (ConfirmKey(), "Confirm"), (BackKey(), "Cancel") };
        else if (GameInput.LastDevice == InputDevice.Mouse)
            hints = new[] { ("Wheel", "Browse"), ("Click", "Select"), ("RMB", _page == Page.Pause ? "Resume" : "Back") };
        else
            hints = new[]
            {
                (GameInput.LastDevice == InputDevice.GamePad ? "D-Pad" : "↑ ↓", "Browse"), (ConfirmKey(), "Select"),
                (BackKey(), _page == Page.Pause ? "Resume" : "Back"),
            };
        bool change = _page == Page.Settings && _confirm == Confirm.None;

        // Only touch the document when something changed (device, page or dialog).
        string key = string.Join("|", hints.Select(h => h.key + h.label)) + change;
        if (key == _hints) return;
        _hints = key;
        for (int i = 0; i < 3; i++)
        {
            _ui.SetText($"#key-{i + 1}", hints[i].key);
            _ui.SetText($"#label-{i + 1}", hints[i].label);
        }
        _ui.SetClass("#hint-4", "visible", change);
        _ui.SetText("#key-4", GameInput.LastDevice == InputDevice.Mouse ? "Click" : "← →");
        _ui.SetText("#label-4", "Change");
    }

    private static string ConfirmKey() => GameInput.LastDevice == InputDevice.GamePad ? "A" : "Enter";
    private static string BackKey() => GameInput.LastDevice == InputDevice.GamePad ? "B" : "Esc";

    ////////////////////////////////////////////////////////////////////////////////
    //  INPUT
    ////////////////////////////////////////////////////////////////////////////////

    private void HandleInput()
    {
        if (_confirm != Confirm.None)
        {
            HandleConfirm();
            return;
        }
        // Start, or a pause key other than Escape (which is Back), resumes from any page.
        if (GameInput.WasPressed(Buttons.Start) || (PauseKey != Keys.Escape && GameInput.WasPressed(PauseKey)))
        {
            Resume();
            return;
        }

        List<IElement> rows = _rows.TryGetValue(_page, out var list) ? list : null;
        IElement hit = _ui?.ElementAt(GameInput.MousePosition)?.DomNode;
        int hovered = rows != null && hit?.Closest(".option") is { } row ? rows.IndexOf(row) : -1;
        // Hover only selects when the cursor moves, so a resting mouse doesn't fight the keyboard.
        if (hovered >= 0 && GameInput.MouseMoved) Select(hovered);

        if (GameInput.MouseClicked)
        {
            if (hovered >= 0) Activate(hovered, hit);
            return;
        }
        if (GameInput.MouseRightClicked)
        {
            Back();
            return;
        }
        if (GameInput.MouseScroll != 0) Move(-GameInput.MouseScroll);

        if (GameInput.MenuUp) Move(-1);
        if (GameInput.MenuDown) Move(1);
        if (_page == Page.Settings)
        {
            if (GameInput.MenuLeft) ChangeSetting(Selected(Page.Settings), -1);
            if (GameInput.MenuRight) ChangeSetting(Selected(Page.Settings), 1);
        }
        if (GameInput.MenuConfirm) Activate(Selected(_page), null);
        else if (GameInput.MenuBack) Back();
    }

    private void HandleConfirm()
    {
        IElement hit = _ui?.ElementAt(GameInput.MousePosition)?.DomNode;
        int hovered = hit?.Id == "confirm-cancel" ? 0 : hit?.Id == "confirm-ok" ? 1 : -1;
        if (hovered >= 0 && GameInput.MouseMoved && hovered != _confirmChoice) Choose(hovered);

        if (GameInput.MouseClicked)
        {
            if (hovered >= 0) Answer(hovered);
            else if (hit?.Closest(".dialog-panel") == null) Answer(0); // clicked the backdrop
            return;
        }
        if (GameInput.MenuLeft) Choose(0);
        if (GameInput.MenuRight) Choose(1);
        if (GameInput.WasPressed(Keys.Tab)) Choose(1 - _confirmChoice);
        if (GameInput.MenuConfirm) Answer(_confirmChoice);
        else if (GameInput.MenuBack || GameInput.MouseRightClicked) Answer(0);
    }

    private void Move(int delta)
    {
        int count = RowCount(_page);
        if (count == 0) return;
        Select(((Selected(_page) + delta) % count + count) % count);
    }

    private void Select(int index)
    {
        if (index == Selected(_page)) return;
        _selected[_page] = index;
        Sound(1.7f, 0.18f);
        ApplyState();
    }

    private void Go(Page page)
    {
        _page = page;
        ApplyState();
    }

    private void Back()
    {
        if (_page == Page.Pause)
        {
            Resume();
            return;
        }
        Sound(0.75f, 0.3f);
        Go(Page.Pause);
    }

    private void Activate(int index, IElement hit)
    {
        if (_page == Page.Settings)
        {
            // Clicking the left arrow steps back; the row or right arrow steps forward.
            ChangeSetting(index, hit?.ClassList.Contains("arrow-left") == true ? -1 : 1);
            return;
        }
        switch (PauseActions.ElementAtOrDefault(index))
        {
            case "resume": Resume(); break;
            case "restart": Ask(Confirm.Restart); break;
            case "settings": Sound(1f, 0.4f); Go(Page.Settings); break;
            case "main-menu":
                if (MainMenuAvailable) Ask(Confirm.MainMenu);
                else Sound(0.5f, 0.25f);
                break;
            case "quit":
                if (QuitAvailable) Ask(Confirm.Quit);
                else Sound(0.5f, 0.25f);
                break;
        }
    }

    private void Ask(Confirm question)
    {
        Sound(1f, 0.4f);
        _confirm = question;
        _confirmChoice = 0;
        _hints = null;
        ApplyState();
    }

    private void Choose(int choice)
    {
        if (choice == _confirmChoice) return;
        _confirmChoice = choice;
        Sound(1.7f, 0.18f);
        ApplyState();
    }

    private void Answer(int choice)
    {
        Confirm question = _confirm;
        if (choice == 0)
        {
            Sound(0.75f, 0.3f);
            _confirm = Confirm.None;
            ApplyState();
            return;
        }
        switch (question)
        {
            case Confirm.Restart: LeaveTo(GameFlow.ReloadScene); break;
            // Back to the menu without the player: the next run starts from the menu's own scene.
            case Confirm.MainMenu: LeaveTo(() => GameFlow.LoadScene(0, carryPersistent: false)); break;
            case Confirm.Quit:
                LeaveTo(() =>
                {
                    GameFlow.Quit();
                    return true;
                });
                break;
        }
    }

    private void ChangeSetting(int index, int direction)
    {
        MenuSetting setting = _settings.ElementAtOrDefault(index);
        if (setting == null) return;
        int value = setting.Step(direction);
        Sound(direction > 0 ? 1.3f : 1.1f, 0.3f);
        if (_rows.TryGetValue(Page.Settings, out var rows) && rows.ElementAtOrDefault(index)?.QuerySelector(".value") is { } valueText)
            valueText.TextContent = setting.Values[value];
    }

    private void Sound(float pitch, float volume) => PlaySound(TickSound, volume, pitch: pitch);
}
