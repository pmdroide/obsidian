using System.Globalization;
using AngleSharp.Dom;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// The MainMenu sample scene: a Vista menu (Content/UI/MainMenu.xml + .css) over a slow camera
/// drift around the monoliths. Select Main Camera, then Add Component > Script Behaviour > Main Menu.
///
/// Title ("Press any button") -> Main Menu -> Play / Scenes / Settings / Credits / Quit.
///   Keyboard  arrows or WASD, Enter/Space, Esc/Backspace (back)
///   Mouse     hover, click, wheel, right click (back)
///   Gamepad   D-pad or left stick, A, B (back)
/// Play loads the scene after this one in the scene list; Scenes lists the whole build order.
/// </summary>
public sealed class MainMenuScript : ScriptBehaviour
{
    public const string ScriptId = "main-menu";

    private const string DocumentPath = "UI/MainMenu";
    private const int RowHeight = 64, SettingRowHeight = 58, CreditRowHeight = 46;
    private const int VisibleSceneRows = 6;
    private const string TickSound = "Audio/blip.wav";

    private enum Page { Title, Main, Scenes, Settings, Credits }

    // The interface scale survives scene switches within a session.
    private static readonly float[] UiScales = { 0.9f, 1f, 1.1f, 1.25f };
    private static int _uiScaleIndex = 1;

    private UIManager _ui;
    private UIElement _press;
    private Page _page = Page.Title;
    private readonly Dictionary<Page, List<IElement>> _rows = new();
    private readonly Dictionary<Page, int> _selected = new();
    private List<MenuSetting> _settings;
    private int _sceneScroll;
    private bool _quitOpen;
    private int _quitChoice;

    private float _time;
    private bool _fadedIn;
    private float _leaveTimer = -1f;
    private Action _afterLeave;

    // Camera drift around the ember core.
    private static readonly Vector3 Center = new(0, 0, 7.5f);
    private float _radius, _baseAngle, _height, _framing;

    public override void Start()
    {
        _ui = GameUI.Open(DocumentPath, 1080f / UiScales[_uiScaleIndex]);
        if (_ui == null) return;
        GameFlow.EscapeQuits = false;
        _settings = CreateSettings();
        _ui.Loaded += Build; // CSS/XML hot reload rebuilds the dynamic parts
        Build();

        Vector3 offset = Position - Center;
        _radius = Math.Max(10f, new Vector2(offset.X, offset.Y).Length());
        _baseAngle = MathF.Atan2(offset.Y, offset.X);
        _height = Position.Z;
    }

    public override void Stop()
    {
        GameFlow.EscapeQuits = true;
        if (_ui == null) return;
        _ui.Loaded -= Build;
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void Update()
    {
        if (_ui == null) return;
        _time += DeltaTime;
        UpdateCamera();
        if (_press != null) _press.RuntimeOpacity = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Cos(_time * 2.4f));

        // Fade in from black once the first frame has laid out (a class set before that would snap).
        if (!_fadedIn && _time > 0.05f)
        {
            _fadedIn = true;
            _ui.SetClass("#fade", "clear", true);
        }

        if (_leaveTimer >= 0f)
        {
            _leaveTimer -= DeltaTime;
            if (_leaveTimer < 0f)
            {
                Action after = _afterLeave;
                _afterLeave = null;
                after?.Invoke();
            }
            return;
        }

        if (DebugScreen.ConsoleOpen) return;
        HandleInput();
        UpdateHints();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  BUILD
    ////////////////////////////////////////////////////////////////////////////////

    private void Build()
    {
        _ui.SetClass("#root", "editor", GameFlow.IsEditor);
        _press = null;

        _rows[Page.Main] = _ui.QueryAll("#main-list .option").ToList();
        _rows[Page.Credits] = new List<IElement>();

        IElement sceneList = _ui.Query("#scene-list");
        _rows[Page.Scenes] = new List<IElement>();
        for (int i = 0; i < GameFlow.SceneCount; i++)
        {
            IElement row = Div("option scene-row");
            row.AppendChild(Div("hl"));
            row.AppendChild(Div("bar"));
            row.AppendChild(Div("index", i.ToString("00", CultureInfo.InvariantCulture)));
            row.AppendChild(Div("label", GameFlow.SceneName(i)));
            row.AppendChild(Div("meta", SceneTags(i)));
            sceneList?.AppendChild(row);
            _rows[Page.Scenes].Add(row);
        }

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

        Stack(_rows[Page.Main], RowHeight);
        Stack(_rows[Page.Settings], SettingRowHeight);
        Stack(_ui.QueryAll("#credits-list .credit").ToList(), CreditRowHeight);

        int next = NextSceneIndex();
        _ui.SetText("#play-meta", next >= 0 ? GameFlow.SceneName(next) : "No next scene");
        _ui.SetText("#scenes-meta", GameFlow.SceneCount == 1 ? "1 scene" : $"{GameFlow.SceneCount} scenes");
        _ui.SetText("#quit-text", GameFlow.IsEditor
            ? "Inside Anvil this goes back to the title screen. Press Stop to leave Play mode."
            : "The game will close.");
        _ui.SetClass(_rows[Page.Main].FirstOrDefault(), "disabled", next < 0);

        // New rows and inline positions: rebuild the visual tree once.
        _ui.Refresh();
        _press = _ui.Get("#press");
        // After a hot reload, skip the fade from black.
        if (_fadedIn) _ui.SetClass("#fade", "clear", true);
        ApplyState();
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
        for (int i = 0; i < rows.Count; i++) rows[i].SetAttribute("style", $"top: {i * height}px");
    }

    private static string SceneTags(int index)
    {
        var tags = new List<string>();
        if (index == 0) tags.Add("Start");
        if (index == GameFlow.ActiveSceneIndex) tags.Add("Running");
        if (!File.Exists(SceneList.ResolvePath(SceneList.Scenes[index]))) tags.Add("Missing");
        return string.Join(" · ", tags);
    }

    /// <summary>The scene after this one in the build list, or -1.</summary>
    private static int NextSceneIndex()
    {
        int active = GameFlow.ActiveSceneIndex;
        int next = active >= 0 ? active + 1 : 1;
        return next < GameFlow.SceneCount ? next : -1;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  STATE -> CLASSES
    ////////////////////////////////////////////////////////////////////////////////

    private int Selected(Page page) => _selected.TryGetValue(page, out int i) ? i : 0;

    private void ApplyState()
    {
        _ui.SetClass("#root", "in-menu", _page != Page.Title);
        _ui.SetClass("#title-screen", "active", _page == Page.Title);
        _ui.SetClass("#main-screen", "active", _page == Page.Main);
        _ui.SetClass("#scenes-screen", "active", _page == Page.Scenes);
        _ui.SetClass("#settings-screen", "active", _page == Page.Settings);
        _ui.SetClass("#credits-screen", "active", _page == Page.Credits);

        foreach (var (page, rows) in _rows)
            for (int i = 0; i < rows.Count; i++)
                _ui.SetClass(rows[i], "selected", i == Selected(page));

        // Scene list: a window of VisibleSceneRows that follows the selection.
        List<IElement> scenes = _rows[Page.Scenes];
        int selected = Selected(Page.Scenes);
        if (selected < _sceneScroll) _sceneScroll = selected;
        if (selected >= _sceneScroll + VisibleSceneRows) _sceneScroll = selected - VisibleSceneRows + 1;
        for (int i = 0; i < scenes.Count; i++)
        {
            bool visible = i >= _sceneScroll && i < _sceneScroll + VisibleSceneRows;
            _ui.SetClass(scenes[i], "offscreen", !visible);
            if (visible) SetTop(scenes[i], (i - _sceneScroll) * RowHeight);
        }
        _ui.SetClass("#scene-scroll-up", "visible", _sceneScroll > 0);
        _ui.SetClass("#scene-scroll-down", "visible", _sceneScroll + VisibleSceneRows < scenes.Count);

        _ui.SetClass("#quit-dialog", "open", _quitOpen);
        _ui.SetClass("#quit-cancel", "selected", _quitChoice == 0);
        _ui.SetClass("#quit-confirm", "selected", _quitChoice == 1);

        UpdateDescriptions();
        UpdateHints();
    }

    private void SetTop(IElement row, int top)
    {
        string style = $"top: {top}px";
        if (row.GetAttribute("style") == style) return;
        row.SetAttribute("style", style);
        _ui.Invalidate(row);
    }

    private void UpdateDescriptions()
    {
        string main = _rows[Page.Main].ElementAtOrDefault(Selected(Page.Main))?.GetAttribute("data-action") switch
        {
            "play" => NextSceneIndex() >= 0
                ? $"Load {GameFlow.SceneName(NextSceneIndex())}, the next scene in the build list."
                : "Add another scene in Anvil > Game Settings > Scenes to play it from here.",
            "scenes" => "Every scene in the build list. Pick one to load it. Index 0 always loads first when the game starts.",
            "settings" => "Rendering and display options for this session.",
            "credits" => "The libraries behind the engine, and what this screen tests.",
            "quit" => GameFlow.IsEditor ? "Leave the game. Inside Anvil this goes back to the title screen." : "Close the game.",
            _ => "",
        };
        _ui.SetText("#main-desc", main);

        int scene = Selected(Page.Scenes);
        string scenes = "The scene list is empty. Add scenes in Anvil > Game Settings > Scenes.";
        if (scene < GameFlow.SceneCount)
        {
            string entry = SceneList.Scenes[scene];
            scenes = File.Exists(SceneList.ResolvePath(entry))
                ? $"{entry}\nPress Select to load it."
                : $"{entry}\nThe file is missing.";
        }
        _ui.SetText("#scenes-desc", scenes);

        MenuSetting setting = _settings.ElementAtOrDefault(Selected(Page.Settings));
        _ui.SetText("#settings-desc", setting?.Description ?? "");
    }

    private void UpdateHints()
    {
        (string key, string label)[] hints;
        if (_quitOpen)
            hints = new[] { ("← →", "Choose"), (ConfirmKey(), "Confirm"), (BackKey(), "Cancel") };
        else if (GameInput.LastDevice == InputDevice.Mouse)
            hints = new[] { ("Wheel", "Browse"), ("Click", "Select"), ("RMB", "Back") };
        else
            hints = new[] { (GameInput.LastDevice == InputDevice.GamePad ? "D-Pad" : "↑ ↓", "Browse"), (ConfirmKey(), "Select"), (BackKey(), "Back") };

        for (int i = 0; i < 3; i++)
        {
            _ui.SetText($"#key-{i + 1}", hints[i].key);
            _ui.SetText($"#label-{i + 1}", hints[i].label);
        }
        bool change = _page == Page.Settings && !_quitOpen;
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
        if (_page == Page.Title)
        {
            // A short grace period so the key that started Play doesn't skip the title.
            if (_time > 0.6f && GameInput.AnyInputPressed)
            {
                Sound(1f, 0.45f);
                Go(Page.Main);
            }
            return;
        }

        if (_quitOpen)
        {
            HandleQuitDialog();
            return;
        }

        List<IElement> rows = _rows[_page];
        IElement hit = _ui.ElementAt(GameInput.MousePosition)?.DomNode;
        int hovered = hit?.Closest(".option") is { } row ? rows.IndexOf(row) : -1;
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

    private void HandleQuitDialog()
    {
        IElement hit = _ui.ElementAt(GameInput.MousePosition)?.DomNode;
        int hovered = hit?.Id == "quit-cancel" ? 0 : hit?.Id == "quit-confirm" ? 1 : -1;
        if (hovered >= 0 && GameInput.MouseMoved && hovered != _quitChoice) ChooseQuit(hovered);

        if (GameInput.MouseClicked)
        {
            if (hovered >= 0) ConfirmQuit(hovered);
            else if (hit?.Closest(".dialog-panel") == null) CloseQuit(); // clicked the backdrop
            return;
        }
        if (GameInput.MenuLeft) ChooseQuit(0);
        if (GameInput.MenuRight) ChooseQuit(1);
        if (GameInput.WasPressed(Microsoft.Xna.Framework.Input.Keys.Tab)) ChooseQuit(1 - _quitChoice);
        if (GameInput.MenuConfirm) ConfirmQuit(_quitChoice);
        else if (GameInput.MenuBack || GameInput.MouseRightClicked) CloseQuit();
    }

    private void Move(int delta)
    {
        List<IElement> rows = _rows[_page];
        if (rows.Count == 0) return;
        Select(((Selected(_page) + delta) % rows.Count + rows.Count) % rows.Count);
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
        Sound(0.75f, 0.3f);
        Go(_page == Page.Main ? Page.Title : Page.Main);
    }

    private void Activate(int index, IElement hit)
    {
        switch (_page)
        {
            case Page.Main:
                switch (_rows[Page.Main].ElementAtOrDefault(index)?.GetAttribute("data-action"))
                {
                    case "play":
                        int next = NextSceneIndex();
                        if (next >= 0) LeaveTo(() => GameFlow.LoadScene(next));
                        break;
                    case "scenes": Confirm(); Go(Page.Scenes); break;
                    case "settings": Confirm(); Go(Page.Settings); break;
                    case "credits": Confirm(); Go(Page.Credits); break;
                    case "quit":
                        Confirm();
                        _quitOpen = true;
                        _quitChoice = 0;
                        ApplyState();
                        break;
                }
                break;
            case Page.Scenes:
                if (index < GameFlow.SceneCount && File.Exists(SceneList.ResolvePath(SceneList.Scenes[index])))
                    LeaveTo(() => GameFlow.LoadScene(index));
                break;
            case Page.Settings:
                // Clicking the left arrow steps back; the row or right arrow steps forward.
                ChangeSetting(index, hit?.ClassList.Contains("arrow-left") == true ? -1 : 1);
                break;
            case Page.Credits:
                Back();
                break;
        }
    }

    private void ChooseQuit(int choice)
    {
        if (choice == _quitChoice) return;
        _quitChoice = choice;
        Sound(1.7f, 0.18f);
        ApplyState();
    }

    private void CloseQuit()
    {
        Sound(0.75f, 0.3f);
        _quitOpen = false;
        ApplyState();
    }

    private void ConfirmQuit(int choice)
    {
        if (choice == 0)
        {
            CloseQuit();
            return;
        }
        _quitOpen = false;
        if (GameFlow.IsEditor)
        {
            Log("Quit pressed: inside Anvil the menu returns to the title screen.");
            Go(Page.Title);
            return;
        }
        LeaveTo(GameFlow.Quit);
    }

    /// <summary>Fades to black, then runs <paramref name="action"/> (load a scene, quit).</summary>
    private void LeaveTo(Action action)
    {
        Confirm();
        _ui.SetClass("#fade", "out", true);
        _leaveTimer = 0.55f;
        _afterLeave = action;
        ApplyState();
    }

    private void Confirm() => Sound(1f, 0.4f);

    private void Sound(float pitch, float volume) => PlaySound(TickSound, volume, pitch: pitch);

    ////////////////////////////////////////////////////////////////////////////////
    //  SETTINGS
    ////////////////////////////////////////////////////////////////////////////////

    private List<MenuSetting> CreateSettings()
    {
        List<MenuSetting> settings = MenuSettings.Graphics();
        settings.Add(new MenuSetting
        {
            Name = "Interface scale",
            Description = "Scales this menu. Vista lays it out on a 1080-high canvas and scales it to the window.",
            Values = new[] { "90%", "100%", "110%", "125%" },
            Get = () => _uiScaleIndex,
            Set = i =>
            {
                _uiScaleIndex = i;
                if (_ui != null) _ui.ReferenceHeight = 1080f / UiScales[i];
            },
            EngineWide = false,
        });
        return settings;
    }

    private void ChangeSetting(int index, int direction)
    {
        MenuSetting setting = _settings.ElementAtOrDefault(index);
        if (setting == null) return;
        int value = setting.Step(direction);
        Sound(direction > 0 ? 1.3f : 1.1f, 0.3f);
        IElement valueText = _rows[Page.Settings][index].QuerySelector(".value");
        if (valueText != null) valueText.TextContent = setting.Values[value];
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  CAMERA
    ////////////////////////////////////////////////////////////////////////////////

    private void UpdateCamera()
    {
        // 0 on the title (subject centred), 1 in the menus (subject framed right of the menu).
        _framing = MathHelper.Lerp(_framing, _page == Page.Title ? 0f : 1f, 1f - MathF.Exp(-2.2f * DeltaTime));

        float angle = _baseAngle + 0.2f * MathF.Sin(_time * 0.05f);
        float radius = _radius * (1f - 0.12f * _framing);
        float height = _height + 0.8f * MathF.Sin(_time * 0.11f);
        Position = new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, height);

        Vector3 toCenter = Vector3.Normalize(Center - Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(toCenter, Vector3.UnitZ));
        // Aiming left of the core moves it right on screen, clear of the menu column.
        LookAt(Center - right * (12f * _framing) + new Vector3(0, 0, 1.5f));
    }
}
