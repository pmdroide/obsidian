using System.Buffers.Binary;
using System.Globalization;
using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// The MultiplayerTest sample scene: peer-to-peer play through Steam with capsules as players.
/// Runs on the Main Camera (Add Component > Script Behaviour > Multiplayer Test).
///
/// One player hosts a Steam lobby (H); the others find it (J) or accept a Steam invite (I).
/// Every player owns their capsule and sends its position to every other lobby member about
/// 20 times a second over SteamNetworkingMessages; remote capsules ease towards the latest state.
///   W A S D / left stick   move (relative to the camera)    Space / A   jump
///   Hold right mouse       orbit the camera
///   H host   J join   I invite friends   L leave   C connect Steam
/// Without Steam the local capsule still moves, so the scene works offline.
/// Two players need two Steam accounts, normally on two PCs.
/// </summary>
public sealed class MultiplayerTestScript : ScriptBehaviour
{
    public const string ScriptId = "multiplayer-test";

    // Lobbies are tagged with this; App 480 (Spacewar) is shared by every Steamworks developer.
    private const string GameKey = "obsidian-capsules-v1";
    private const int MaxPlayers = 4;
    private const string DocumentPath = "UI/MultiplayerTest";

    private const float SendInterval = 1f / 20f;
    private const float MoveSpeed = 5f, TurnSmoothing = 14f, RemoteSmoothing = 15f;
    private const float JumpSpeed = 5.5f, Gravity = 16f;
    private const float ArenaHalfSize = 9.25f; // inner wall faces at 9.75, capsule radius 0.5
    // The capsule is 2 units tall and centred on its origin, so it stands at Z = 1.
    private const float StandHeight = 1f;
    private const float CameraDistance = 7.5f, LookDegreesPerPixel = 0.25f;

    private const byte StatePacket = 1;
    private const int StatePacketSize = 1 + 4 + 4 * 4;

    // Colours are picked from the Steam ID, so every peer shows a player in the same colour.
    private static readonly Color[] Palette =
    {
        new(232, 93, 63), new(64, 156, 255), new(98, 201, 104), new(246, 196, 64),
        new(178, 102, 255), new(48, 205, 196), new(255, 120, 190), new(235, 235, 235),
    };

    private sealed class Player
    {
        public ulong Id;
        public string Name;
        public Color Color;
        public BasicEntity Body, Visor;
        public Vector3 Position, Target;
        public float Yaw, TargetYaw;
        public bool HasState;
        public uint Sequence;
    }

    private SteamP2PSession _session;
    private readonly Dictionary<ulong, Player> _remotes = new();
    private Player _local;
    private float _verticalSpeed;
    private float _sendTimer;
    private uint _sequence;
    private readonly byte[] _packet = new byte[StatePacketSize];

    private float _cameraYaw, _cameraPitch = 22f;
    private UIManager _ui;
    private float _hudTimer;

    public override void Start()
    {
        Vector3 forward = Forward;
        _cameraYaw = MathHelper.ToDegrees(MathF.Atan2(forward.Y, forward.X));

        // Start facing away from the camera (yaw turns +Y, the capsule's forward, around Z).
        _local = new Player { Id = 0, Name = "You", Color = Palette[0], Position = new Vector3(0, 0, StandHeight),
                              Yaw = MathF.Atan2(-forward.X, forward.Y) };
        _local.Target = _local.Position;
        SpawnBody(_local);

        _ui = GameUI.Open(DocumentPath);
        UpdateCamera();
    }

    public override void Stop()
    {
        // Leaving raises PeerLeft for every peer; the spawned capsules are removed after Stop anyway.
        _session?.Dispose();
        _session = null;
        _remotes.Clear();
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void Update()
    {
        UpdateSteam();
        if (!DebugScreen.ConsoleOpen)
        {
            HandleLobbyKeys();
            MoveLocal();
        }
        if (_session != null)
        {
            _session.Receive(OnMessage);
            _sendTimer -= DeltaTime;
            if (_sendTimer <= 0f && _session.Peers.Count > 0)
            {
                _sendTimer += SendInterval;
                if (_sendTimer < 0f) _sendTimer = SendInterval; // after a long frame, don't burst
                SendState();
            }
        }
        foreach (Player remote in _remotes.Values) SmoothRemote(remote);
        UpdateCamera();

        _hudTimer -= DeltaTime;
        if (_hudTimer <= 0f)
        {
            _hudTimer = 0.2f;
            UpdateHud();
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  STEAM SESSION
    ////////////////////////////////////////////////////////////////////////////////

    private static bool SteamReady => SteamService.Current?.Status is { IsConnected: true, IsLoggedOn: true };

    /// <summary>Creates the lobby session when Steam comes online and drops it when Steam goes away.</summary>
    private void UpdateSteam()
    {
        if (SteamReady && _session == null)
        {
            _session = new SteamP2PSession(GameKey);
            _session.PeerJoined += OnPeerJoined;
            _session.PeerLeft += OnPeerLeft;
            _local.Id = _session.LocalId;
            _local.Name = _session.NameOf(_local.Id);
            if (_local.Body != null) _local.Body.Name = $"Player {_local.Name}";
            SetColor(_local, ColorFor(_local.Id));
        }
        else if (!SteamReady && _session != null)
        {
            _session.Dispose();
            _session = null;
            foreach (ulong id in _remotes.Keys.ToArray()) OnPeerLeft(id);
        }
    }

    private void HandleLobbyKeys()
    {
        if (GameInput.WasPressed(Keys.C) && !SteamReady && SteamService.Current is { } steam)
        {
            // Same as Connect in Anvil > Window > Steam: turns Steam on in the saved settings.
            steam.Configure(true, steam.ConfiguredAppId);
            Log("Steam: " + steam.Status.Message);
        }
        if (_session == null) return;
        if (GameInput.WasPressed(Keys.H)) _session.Host(MaxPlayers);
        if (GameInput.WasPressed(Keys.J)) _session.FindAndJoin();
        if (GameInput.WasPressed(Keys.L)) _session.Leave();
        if (GameInput.WasPressed(Keys.I) && !_session.InviteFriends()) Log("Host or join a lobby before inviting friends.");
    }

    private void OnPeerJoined(ulong id)
    {
        if (_remotes.ContainsKey(id)) return;
        // The capsule appears with the peer's first state, so it doesn't pop in at the origin.
        _remotes[id] = new Player { Id = id, Name = _session.NameOf(id), Color = ColorFor(id) };
        Log($"{_remotes[id].Name} joined");
        _sendTimer = 0f; // send our state right away
    }

    private void OnPeerLeft(ulong id)
    {
        if (!_remotes.Remove(id, out Player player)) return;
        Destroy(player.Body);
        Destroy(player.Visor);
        Log($"{player.Name} left");
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  NETWORK STATE  [kind:1][sequence:4][x:4][y:4][z:4][yaw:4], little endian
    ////////////////////////////////////////////////////////////////////////////////

    private void SendState()
    {
        Span<byte> data = _packet;
        data[0] = StatePacket;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(1), ++_sequence);
        BinaryPrimitives.WriteSingleLittleEndian(data.Slice(5), _local.Position.X);
        BinaryPrimitives.WriteSingleLittleEndian(data.Slice(9), _local.Position.Y);
        BinaryPrimitives.WriteSingleLittleEndian(data.Slice(13), _local.Position.Z);
        BinaryPrimitives.WriteSingleLittleEndian(data.Slice(17), _local.Yaw);
        _session.Broadcast(_packet, StatePacketSize, reliable: false);
    }

    private void OnMessage(ulong sender, byte[] buffer, int length)
    {
        if (length < StatePacketSize || buffer[0] != StatePacket) return;
        if (!_remotes.TryGetValue(sender, out Player player)) return;
        ReadOnlySpan<byte> data = buffer.AsSpan(0, length);
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1));
        // Unreliable messages can arrive out of order: keep only the newest.
        if (player.HasState && (int)(sequence - player.Sequence) <= 0) return;
        player.Sequence = sequence;
        var position = new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(5)),
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(9)),
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(13)));
        float yaw = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(17));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) || !float.IsFinite(yaw)) return;

        player.Target = position;
        player.TargetYaw = yaw;
        if (!player.HasState)
        {
            player.HasState = true;
            player.Position = position;
            player.Yaw = yaw;
            SpawnBody(player);
        }
    }

    private void SmoothRemote(Player player)
    {
        if (player.Body == null) return;
        float t = 1f - MathF.Exp(-RemoteSmoothing * DeltaTime);
        // Ease towards the newest state; snap after a teleport or a long gap.
        player.Position = Vector3.Distance(player.Position, player.Target) > 4f
            ? player.Target
            : Vector3.Lerp(player.Position, player.Target, t);
        player.Yaw += MathHelper.WrapAngle(player.TargetYaw - player.Yaw) * t;
        PlaceBody(player);
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  LOCAL PLAYER
    ////////////////////////////////////////////////////////////////////////////////

    private void MoveLocal()
    {
        float yaw = MathHelper.ToRadians(_cameraYaw);
        var forward = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
        var right = new Vector3(MathF.Sin(yaw), -MathF.Cos(yaw), 0f);

        Vector2 stick = GameInput.LeftStick;
        Vector3 move = forward * stick.Y + right * stick.X;
        if (GameInput.IsDown(Keys.W)) move += forward;
        if (GameInput.IsDown(Keys.S)) move -= forward;
        if (GameInput.IsDown(Keys.D)) move += right;
        if (GameInput.IsDown(Keys.A)) move -= right;
        if (move.LengthSquared() > 1f) move.Normalize();

        Vector3 position = _local.Position + move * MoveSpeed * DeltaTime;
        position.X = Math.Clamp(position.X, -ArenaHalfSize, ArenaHalfSize);
        position.Y = Math.Clamp(position.Y, -ArenaHalfSize, ArenaHalfSize);

        bool grounded = position.Z <= StandHeight + 1e-3f;
        if (grounded && (GameInput.WasPressed(Keys.Space) || GameInput.WasPressed(Buttons.A))) _verticalSpeed = JumpSpeed;
        _verticalSpeed -= Gravity * DeltaTime;
        position.Z += _verticalSpeed * DeltaTime;
        if (position.Z <= StandHeight)
        {
            position.Z = StandHeight;
            _verticalSpeed = 0f;
        }
        _local.Position = position;

        // Face the way we move. Yaw turns the capsule's +Y (its forward) around Z.
        if (move.LengthSquared() > 0.01f)
        {
            float target = MathF.Atan2(-move.X, move.Y);
            _local.Yaw += MathHelper.WrapAngle(target - _local.Yaw) * (1f - MathF.Exp(-TurnSmoothing * DeltaTime));
            _local.Yaw = MathHelper.WrapAngle(_local.Yaw);
        }
        PlaceBody(_local);
    }

    private void UpdateCamera()
    {
        MouseState mouse = Input.mouseState, last = Input.mouseLastState;
        if (!DebugScreen.ConsoleOpen && mouse.RightButton == ButtonState.Pressed && last.RightButton == ButtonState.Pressed)
        {
            _cameraYaw -= (mouse.X - last.X) * LookDegreesPerPixel;
            _cameraPitch = Math.Clamp(_cameraPitch + (mouse.Y - last.Y) * LookDegreesPerPixel, 5f, 70f);
        }
        float yaw = MathHelper.ToRadians(_cameraYaw), pitch = MathHelper.ToRadians(_cameraPitch);
        var view = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
        Vector3 focus = _local.Position + new Vector3(0, 0, 0.6f);
        Position = focus - view * CameraDistance;
        LookAt(focus);
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  CAPSULES
    ////////////////////////////////////////////////////////////////////////////////

    private void SpawnBody(Player player)
    {
        player.Body = Spawn("Capsule", player.Position, $"Player {player.Name}");
        // A dark visor shows which way the capsule faces.
        player.Visor = Spawn("Cube", player.Position, $"Player {player.Name} Visor");
        if (player.Body == null || player.Visor == null) return;
        player.Body.AddComponent(new MaterialComponent { Roughness = 0.45f });
        player.Visor.AddComponent(new MaterialComponent { Red = 0.04f, Green = 0.05f, Blue = 0.07f, Roughness = 0.1f, Metallic = 0.6f });
        player.Visor.Scale = new Vector3(0.3f, 0.12f, 0.11f); // the cube is 2 units across
        SetColor(player, player.Color);
        PlaceBody(player);
    }

    private static void PlaceBody(Player player)
    {
        if (player.Body == null) return;
        Matrix rotation = Matrix.CreateRotationZ(player.Yaw);
        player.Body.Position = player.Position;
        player.Body.RotationMatrix = rotation;
        if (player.Visor == null) return;
        player.Visor.Position = player.Position + Vector3.TransformNormal(new Vector3(0f, 0.42f, 0.45f), rotation);
        player.Visor.RotationMatrix = rotation;
    }

    private static void SetColor(Player player, Color color)
    {
        player.Color = color;
        if (player.Body?.GetComponent<MaterialComponent>() is not { } material) return;
        material.Red = color.R / 255f;
        material.Green = color.G / 255f;
        material.Blue = color.B / 255f;
        material.OnChanged(player.Body);
    }

    private static Color ColorFor(ulong steamId) => Palette[(int)(steamId % (ulong)Palette.Length)];

    ////////////////////////////////////////////////////////////////////////////////
    //  HUD (Content/UI/MultiplayerTest.xml + .css)
    ////////////////////////////////////////////////////////////////////////////////

    private void UpdateHud()
    {
        if (_ui == null) return;
        SteamConnectionStatus steam = SteamService.Current?.Status;
        string steamLine, status;
        if (steam == null) (steamLine, status) = ("Steam unavailable", "The Steam service did not start.");
        else if (!steam.IsEnabled) (steamLine, status) = ("Steam off", "Press C to connect to Steam (turns it on in the saved settings).");
        else if (!steam.IsConnected) (steamLine, status) = ("Steam disconnected", steam.Message + " Press C to retry.");
        else if (!steam.IsLoggedOn) (steamLine, status) = ($"{steam.PersonaName} · offline", "Steam is offline. Lobbies need an online Steam session.");
        else
        {
            steamLine = $"{steam.PersonaName} · App {steam.AppId}";
            status = _session?.Status ?? "Connecting...";
        }
        _ui.SetText("#steam", steamLine);
        _ui.SetText("#status", status);
        _ui.SetClass("#steam-dot", "online", SteamReady);

        string lobby = _session?.InLobby == true
            ? (_session.IsOwner ? "Hosting" : "In lobby") + $" · {_session.Peers.Count + 1}/{MaxPlayers}"
            : "No lobby";
        _ui.SetText("#lobby", lobby);

        var rows = new List<(Player Player, string Detail)> { (_local, _session?.IsOwner == true ? "you · host" : "you") };
        foreach (Player remote in _remotes.Values)
        {
            var (state, ping) = _session != null ? _session.GetPeerLink(remote.Id) : ("Not connected", -1);
            string detail = ping >= 0 ? ping.ToString(CultureInfo.InvariantCulture) + " ms" : state;
            if (remote.Id == _session?.OwnerId) detail += " · host";
            if (!remote.HasState) detail += " · no data yet";
            rows.Add((remote, detail));
        }
        for (int i = 0; i < MaxPlayers; i++)
        {
            string row = $"#player-{i}";
            bool used = i < rows.Count;
            _ui.SetClass(row, "used", used);
            if (!used) continue;
            Color c = rows[i].Player.Color;
            _ui.SetVariable($"{row} .swatch", "style", $"background-color: rgb({c.R}, {c.G}, {c.B})");
            _ui.SetText($"{row} .name", rows[i].Player.Name);
            _ui.SetText($"{row} .detail", rows[i].Detail);
        }

        bool inLobby = _session?.InLobby == true;
        _ui.SetClass("#keys-lobby", "visible", _session != null && !inLobby);
        _ui.SetClass("#keys-in-lobby", "visible", inLobby);
        _ui.SetClass("#keys-steam", "visible", _session == null);
    }
}
