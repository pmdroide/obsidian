using System.Reflection;
using Anvil.Models;
using Anvil.ViewModels;
using Engine.Editor;
using Engine.Logic;
using Engine.Recources;
using Engine.Steam;

internal static class SteamChecks
{
    public static void Run()
    {
        var client = new FakeSteamClient { Succeed = false };
        using var steam = new SteamService(client);
        steam.Configure(true, 480);
        Check(!steam.Status.IsConnected && steam.Status.Message.Contains("Start Steam"),
            "failed Steam initialization remains retryable with useful status");
        client.Succeed = true;
        steam.Connect();
        var connected = steam.Status;
        Check(connected.IsConnected && connected.IsLoggedOn && connected.AppId == 480,
            "retry initializes Spacewar and publishes the signed-in account");
        steam.Connect();
        Check(client.Initializations == 2, "connecting an initialized session is idempotent");
        steam.Update();
        steam.Update();
        Check(client.CallbackPumps == 2, "each update pumps callbacks");

        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bridge, new object[] { new MainSceneLogic(), new EditorLogic(), new Assets() });
        bridge.BindSteam(steam);
        var panel = new SteamViewModel();
        Check(!panel.ConnectCommand.CanExecute(null), "Steam controls wait for the engine bridge");
        panel.AttachBridge(bridge);
        Check(panel.PersonaName == "Test account" && panel.SteamId == "123" &&
            !panel.ConnectCommand.CanExecute(null) && panel.DisconnectCommand.CanExecute(null),
            "Steam panel reflects identity and connected command state");
        panel.DisconnectCommand.Execute(null);
        Check(steam.Status.IsConnected && client.Shutdowns == 0, "editor disconnect waits for the game thread");
        Drain(bridge);
        panel.Refresh();
        Check(!steam.Status.IsConnected && client.Shutdowns == 1 && panel.SteamId == "-" &&
            panel.ConnectCommand.CanExecute(null) && !panel.DisconnectCommand.CanExecute(null),
            "queued disconnect shuts down and clears the panel identity");
        Check(connected.IsConnected && connected.SteamId == 123, "previous Steam status snapshots remain immutable");
        panel.ConnectCommand.Execute(null);
        Check(!steam.Status.IsConnected, "editor connect waits for the game thread");
        Drain(bridge);
        Check(steam.Status.IsConnected && client.Initializations == 3, "queued reconnect creates a new session");

        client.LoggedOn = false;
        RefreshOnNextUpdate(steam);
        steam.Update();
        Check(steam.Status.IsConnected && !steam.Status.IsLoggedOn, "offline Steam session is distinct from disconnected");
        client.IsRunning = false;
        RefreshOnNextUpdate(steam);
        steam.Update();
        Check(!steam.Status.IsConnected && client.Shutdowns == 2 && steam.Status.Message.Contains("Steam closed"),
            "closing the Steam client releases the session and permits reconnecting");
        steam.Dispose();
        steam.Dispose();
        steam.Connect();
        steam.Update();
        Check(client.Initializations == 3 && client.Shutdowns == 2, "disposed service never reinitializes or shuts down twice");

        var wrongApp = new FakeSteamClient { AppId = 999 };
        using var wrongSteam = new SteamService(wrongApp);
        wrongSteam.Configure(true, 480);
        Check(!wrongSteam.Status.IsConnected && wrongApp.Shutdowns == 1,
            "unexpected application identity shuts down the initialized session");

        var broken = new FakeSteamClient { ReadError = new InvalidOperationException("bad interface") };
        using var brokenSteam = new SteamService(broken);
        brokenSteam.Configure(true, 480);
        Check(!brokenSteam.Status.IsConnected && broken.Shutdowns == 1 &&
            brokenSteam.Status.Message.Contains("bad interface"), "partial initialization cleans up before reporting failure");

        var missing = new FakeSteamClient { InitError = new DllNotFoundException("steam_api64.dll") };
        using var missingSteam = new SteamService(missing);
        missingSteam.Configure(true, 480);
        Check(!missingSteam.Status.IsConnected && missing.Shutdowns == 0 &&
            missingSteam.Status.Message.Contains("thirdparty/steam"), "missing native library does not crash startup");

        var main = new MainWindowViewModel();
        main.SetInspectorViewCommand.Execute("Steam");
        Check(main.IsInspectorSteamView && !main.IsInspectorSelectionView && !main.IsInspectorEnvironmentView,
            "Window Steam command selects the Steam inspector section");
        CheckSettingsPersistence();
        Console.WriteLine("Steam lifecycle, settings persistence and editor checks passed.");
    }

    public static void RunNative()
    {
        // The smoke check must not change the project's saved enable preference.
        using var steam = new SteamService(new SteamworksClient());
        steam.Configure(true, 480);
        steam.Update();
        Console.WriteLine($"Native Steam smoke check: connected={steam.Status.IsConnected}, AppId={steam.Status.AppId}. {steam.Status.Message}");
        if (steam.Status.Message.Contains("initialization failed:"))
            throw new Exception("Steam native dependencies did not load correctly.");
        if (steam.Status.IsConnected) Check(steam.Status.AppId == 480, "native Steam initialized Spacewar");
        steam.Disconnect();
    }

    private static void Drain(EditorBridge bridge) =>
        typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, null);

    private static void CheckSettingsPersistence()
    {
        string folder = Path.Combine(Path.GetTempPath(), "anvil-steam-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "SteamSettings.json");
        var store = new SteamSettingsStore(path);
        try
        {
            var firstClient = new FakeSteamClient();
            using (var first = new SteamService(firstClient, store))
            {
                first.Start();
                first.Connect();
                first.Update();
                Check(!first.Status.IsEnabled && first.Status.AppId == 480 && firstClient.Initializations == 0,
                    "first launch defaults to Steam off and never touches native initialization");
                first.Configure(false, 12345);
                Check(!first.Status.IsEnabled && first.Status.AppId == 12345 && firstClient.Initializations == 0,
                    "App ID can be changed and saved while Steam is disabled");
            }
            var enabledClient = new FakeSteamClient { AppId = 12345 };
            using (var enabled = new SteamService(enabledClient, store))
            {
                enabled.Start();
                Check(enabled.Status.AppId == 12345 && !enabled.Status.IsEnabled && enabledClient.Initializations == 0,
                    "reopening retains the custom App ID and disabled preference");
                enabled.Configure(true, 12345);
                Check(enabled.Status.IsConnected && enabled.Status.IsEnabled && enabled.Status.AppId == 12345,
                    "enabling initializes the configured custom App ID");
                Check(enabledClient.RequestedAppId == "12345",
                    "the custom App ID reaches native initialization instead of the Spacewar default");
            }
            var reopenedClient = new FakeSteamClient { AppId = 12345 };
            using (var reopened = new SteamService(reopenedClient, store))
            {
                reopened.Start();
                Check(reopened.Status.IsEnabled && reopened.Status.IsConnected && reopenedClient.Initializations == 1,
                    "reopening automatically connects when the saved preference is enabled");

                var bridge = new EditorBridge();
                typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(bridge, new object[] { new MainSceneLogic(), new EditorLogic(), new Assets() });
                bridge.BindSteam(reopened);
                var panel = new SteamViewModel();
                panel.AttachBridge(bridge);
                Check(panel.IsEnabled && panel.AppId == 12345, "Steam panel restores the saved toggle and ID");
                panel.AppId = 54321;
                panel.Refresh();
                Check(panel.AppId == 54321 && reopened.Status.AppId == 12345,
                    "status refresh preserves an unsaved App ID without changing the connection");
                reopenedClient.AppId = 54321;
                panel.ApplyAppIdCommand.Execute(null);
                Check(reopened.Status.AppId == 12345 && panel.IsBusy, "App ID changes wait for the game thread");
                Drain(bridge);
                panel.Refresh();
                Check(reopened.Status.AppId == 54321 && reopenedClient.Shutdowns == 1 &&
                    reopenedClient.Initializations == 2 && store.Read().AppId == 54321 && !panel.IsBusy,
                    "applying a new ID saves it and reconnects the enabled Steam session");
                panel.AppId = 0;
                Check(!panel.ApplyAppIdCommand.CanExecute(null), "zero App ID is rejected by the editor");
                panel.AppId = 1.5m;
                Check(!panel.ApplyAppIdCommand.CanExecute(null), "fractional App ID is rejected by the editor");
                reopened.Configure(true, 0);
                Check(reopened.Status.AppId == 54321 && store.Read().AppId == 54321,
                    "invalid App ID does not overwrite the saved valid configuration");
                panel.AppId = 0;
                Check(panel.CanToggleSteam, "invalid ID draft does not prevent turning Steam off");
                panel.IsEnabled = false;
                Drain(bridge);
                Check(!reopened.Status.IsEnabled && !reopened.Status.IsConnected && !store.Read().Enabled,
                    "turning Steam off saves the preference and releases the session while keeping the valid saved ID");
            }

            var offClient = new FakeSteamClient();
            using (var off = new SteamService(offClient, store))
            {
                off.Start();
                Check(!off.Status.IsEnabled && off.Status.AppId == 54321 && offClient.Initializations == 0,
                    "Steam remains off with the last configured ID after reopening");
            }
            var failedClient = new FakeSteamClient { Succeed = false };
            using (var failed = new SteamService(failedClient, store)) failed.Configure(true, 54321);
            Check(store.Read().Enabled, "connection failure keeps the user's persisted enable preference");
            var retryClient = new FakeSteamClient { AppId = 54321 };
            using (var retry = new SteamService(retryClient, store))
            {
                retry.Start();
                Check(retry.Status.IsConnected, "reopening retries an enabled session after an earlier failure");
            }

            // A failed save must not change an active connection or claim that settings persisted.
            var blockedClient = new FakeSteamClient();
            using (var blocked = new SteamService(blockedClient, new SteamSettingsStore(folder)))
            {
                blocked.Configure(true, 480);
                Check(!blocked.Status.IsEnabled && blockedClient.Initializations == 0 &&
                    blocked.Status.Message.Contains("could not be saved"), "save failure reports an error without enabling Steam");
            }
            File.WriteAllText(path, "{broken");
            Check(!store.Read().Enabled && store.Read().AppId == 480, "corrupt settings safely default to Steam off");
            File.WriteAllText(path, "{\"Enabled\":true,\"AppId\":0}");
            Check(!store.Read().Enabled, "invalid saved ID safely defaults to Steam off");
        }
        finally
        {
            // Only remove files generated by this check in its unique temporary folder.
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }

    private static void RefreshOnNextUpdate(SteamService steam) =>
        typeof(SteamService).GetField("_nextStatusRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(steam, DateTime.MinValue);

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("Steam check failed: " + message);
    }

    private sealed class FakeSteamClient : ISteamClient
    {
        public bool Succeed = true;
        public bool LoggedOn = true;
        public uint AppId = 480;
        public Exception? InitError;
        public Exception? ReadError;
        public string? RequestedAppId;
        public int Initializations, Shutdowns, CallbackPumps;
        public bool IsRunning { get; set; } = true;

        public bool Initialize(out string error)
        {
            Initializations++;
            RequestedAppId = Environment.GetEnvironmentVariable("SteamAppId");
            if (InitError != null) throw InitError;
            error = "Test initialization failure.";
            return Succeed;
        }

        public void RunCallbacks() => CallbackPumps++;
        public void Shutdown() => Shutdowns++;
        public SteamConnectionStatus ReadStatus()
        {
            if (ReadError != null) throw ReadError;
            return new(true, LoggedOn, AppId, 123, "Test account", LoggedOn ? "Connected to Steam." : "Steam is offline.");
        }
    }
}
