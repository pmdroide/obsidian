using System;
using System.IO;
using Steamworks;

namespace Engine.Steam
{
    public sealed record SteamConnectionStatus(bool IsConnected, bool IsLoggedOn,
        uint AppId, ulong SteamId, string PersonaName, string Message, bool IsEnabled = false)
    {
        public static SteamConnectionStatus Disconnected(string message,
            uint appId = SteamService.SpacewarAppId, bool enabled = false) =>
            new(false, false, appId, 0, string.Empty, message, enabled);
    }

    /// <summary>
    /// Owns the process Steam session. Connect, Update, Disconnect and future Steamworks
    /// calls must run on the game thread. Status is an immutable snapshot safe for the UI.
    /// </summary>
    public sealed class SteamService : IDisposable
    {
        public const uint SpacewarAppId = 480;
        private readonly ISteamClient _client;
        private readonly SteamSettingsStore _settingsStore;
        private SteamSettings _settings;
        private volatile SteamConnectionStatus _status = SteamConnectionStatus.Disconnected("Steam is disconnected.");
        private bool _initialized;
        private bool _disposed;
        private DateTime _nextStatusRefresh;

        public SteamConnectionStatus Status => _status;
        /// <summary>The engine's session, for scripts (see <see cref="SteamP2PSession"/>). Set by <c>Engine.Engine</c>.</summary>
        public static SteamService Current { get; internal set; }
        /// <summary>Saved App ID; also the App ID of the running session once connected.</summary>
        public uint ConfiguredAppId => _settings.AppId;

        public SteamService() : this(new SteamworksClient(), new SteamSettingsStore()) { }
        internal SteamService(ISteamClient client, SteamSettingsStore settingsStore = null)
        {
            _client = client;
            _settingsStore = settingsStore;
            _settings = settingsStore?.Read() ?? new SteamSettings();
            SetDisconnected(_settings.Enabled ? "Steam is disconnected." : "Steam is disabled.");
        }

        public void Start()
        {
            if (_settings.Enabled) Connect();
        }

        public void Configure(bool enabled, uint appId)
        {
            if (_disposed) return;
            if (appId == 0)
            {
                _status = _status with { Message = "Enter a Steam App ID between 1 and 4294967295." };
                return;
            }
            var settings = new SteamSettings(enabled, appId);
            try { _settingsStore?.Save(settings); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _status = _status with { Message = "Steam settings could not be saved: " + ex.Message };
                return;
            }
            bool appChanged = _settings.AppId != appId;
            _settings = settings;
            if (!enabled || appChanged) Disconnect();
            if (enabled) Connect();
        }

        public void Connect()
        {
            if (_disposed || _initialized || !_settings.Enabled) return;
            try
            {
                if (!Environment.Is64BitProcess)
                {
                    SetDisconnected("Steam requires a 64-bit build (steam_api64.dll).");
                    return;
                }

                // Development launches may have any working directory. Scope the override to
                // Init, and leave Steam-launched published builds to use their client App ID.
                string previousAppId = Environment.GetEnvironmentVariable("SteamAppId");
                string previousGameId = Environment.GetEnvironmentVariable("SteamGameId");
                string developmentFile = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
                bool development = File.Exists(developmentFile);
                string error;
                bool success;
                try
                {
                    if (development)
                    {
                        File.WriteAllText(developmentFile, _settings.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        Environment.SetEnvironmentVariable("SteamAppId", _settings.AppId.ToString());
                        Environment.SetEnvironmentVariable("SteamGameId", _settings.AppId.ToString());
                    }
                    success = _client.Initialize(out error);
                }
                finally
                {
                    if (development)
                    {
                        Environment.SetEnvironmentVariable("SteamAppId", previousAppId);
                        Environment.SetEnvironmentVariable("SteamGameId", previousGameId);
                    }
                }

                if (!success)
                {
                    SetDisconnected("Unable to connect. Start Steam and sign in, then retry. " + error);
                    return;
                }

                _initialized = true;
                RefreshStatus();
                if (_status.AppId != _settings.AppId)
                {
                    uint actualAppId = _status.AppId;
                    Disconnect();
                    SetDisconnected($"Steam initialized App ID {actualAppId}, but the configured ID is {_settings.AppId}.");
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException ||
                ex is EntryPointNotFoundException || ex is InvalidOperationException ||
                ex is IOException || ex is UnauthorizedAccessException)
            {
                Disconnect();
                SetDisconnected("Steam initialization failed: " + ex.Message +
                    " Check the matching Steamworks.NET.dll and steam_api64.dll in thirdparty/steam.");
            }
        }

        public void Update()
        {
            if (!_initialized || _disposed) return;
            // Callbacks also run while the game is unfocused or the editor is in Edit mode.
            _client.RunCallbacks();
            if (DateTime.UtcNow < _nextStatusRefresh) return;
            if (!_client.IsRunning)
            {
                Disconnect();
                SetDisconnected("Steam closed. Start Steam and reconnect.");
                return;
            }
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            _status = _client.ReadStatus() with { IsEnabled = _settings.Enabled };
            _nextStatusRefresh = DateTime.UtcNow.AddSeconds(1);
        }

        public void Disconnect()
        {
            if (_initialized)
            {
                _initialized = false;
                _client.Shutdown();
            }
            SetDisconnected(_settings.Enabled ? "Steam is disconnected." : "Steam is disabled.");
        }

        private void SetDisconnected(string message) =>
            _status = SteamConnectionStatus.Disconnected(message, _settings.AppId, _settings.Enabled);

        public void Dispose()
        {
            if (_disposed) return;
            Disconnect();
            _disposed = true;
        }
    }

    internal interface ISteamClient
    {
        bool Initialize(out string error);
        bool IsRunning { get; }
        void RunCallbacks();
        SteamConnectionStatus ReadStatus();
        void Shutdown();
    }

    internal sealed class SteamworksClient : ISteamClient
    {
        public bool Initialize(out string error) => SteamAPI.InitEx(out error) == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
        public bool IsRunning => SteamAPI.IsSteamRunning();
        public void RunCallbacks() => SteamAPI.RunCallbacks();
        public void Shutdown() => SteamAPI.Shutdown();

        public SteamConnectionStatus ReadStatus()
        {
            bool loggedOn = SteamUser.BLoggedOn();
            return new SteamConnectionStatus(true, loggedOn, SteamUtils.GetAppID().m_AppId,
                SteamUser.GetSteamID().m_SteamID, SteamFriends.GetPersonaName(),
                loggedOn ? "Connected to Steam." : "Steam session initialized. Steam is offline; online lobbies need a connection.");
        }
    }
}
