# Steam integration

This folder contains the Windows x64 binaries from the official
[Steamworks.NET 2025.164.1 standalone release](https://github.com/rlabrecque/Steamworks.NET/releases/tag/2025.164.1).
Keep `Steamworks.NET.dll` and `steam_api64.dll` from the same release together.
The managed wrapper's MIT license is in `LICENSE.txt`; the native Steam API is
Valve's Steamworks redistributable and is subject to the Steamworks SDK terms.

Each executable imports `Steamworks.props` to reference the managed assembly
and register it in its runtime dependency manifest. The engine copies the native DLL
beside both Engine and Anvil executables. `steam_appid.txt` initially contains `480`
(Spacewar) for development and is updated in the output directory to the configured
App ID when connecting. It is copied to build outputs, but excluded from
`dotnet publish`. The DLLs are explicitly included by `.gitignore` so a fresh
checkout has the dependencies.

Open Anvil > Window > Steam. Steam is **off by default**. Enter an App ID and
click Apply, then turn on Enable Steam to connect. `480` remains the default
test ID. Applying another ID while enabled reconnects with that ID. Start the
Steam desktop client and sign in to establish an online session.

The App ID and enable preference are saved immediately to
`Engine/Content/System/SteamSettings.json` in a development checkout. This
file is copied to build/publish outputs; outside the checkout, settings use
`Content/System/SteamSettings.json` beside the executable. Reopening the editor
restores both values and attempts to connect before initializing the renderer
only when enabled. Closing the editor releases the session without changing
the saved preference. Connection failures preserve the enable preference for
the next launch; missing, invalid, or corrupt settings safely default to off.

The panel shows the account, Steam ID, online/offline status, and connection
errors. Connect enables and retries; Disconnect disables and saves that
preference. The editor remains usable when Steam is unavailable. This
integration requires a 64-bit process.

`Engine.Steam.SteamService` owns initialization, callback pumping on every game
update (including unfocused frames), and shutdown during engine disposal.
`Engine.Engine.Steam.Status` and `IEditorBridge.SteamStatus` are immutable snapshots;
editor commands go through the game-thread operation queue. Development initialization
scopes `SteamAppId`/`SteamGameId` environment overrides to the initialization call,
so startup also works when the working directory differs from the output directory.

For future features, use `Steamworks.SteamUserStats` for achievements,
`Steamworks.SteamMatchmaking` for lobby discovery/creation/joining, and
`Steamworks.SteamNetworkingSockets` or `Steamworks.SteamNetworkingMessages` for
peer traffic. Run these calls and create/dispose their `Callback<T>` and
`CallResult<T>` handlers on the game thread, after checking `Status.IsConnected`.
Online lobbies also require `Status.IsLoggedOn`. The existing service pumps
Steamworks.NET callbacks; no achievements are unlocked and no lobbies are
created by this connection panel.

Lobbies and peer-to-peer messages are already wrapped by
`Engine.Steam.SteamP2PSession` (`SteamMatchmaking` + `SteamNetworkingMessages`).
Scripts reach the engine's session through `SteamService.Current`. The
`Scenes/MultiplayerTest.obsc` sample uses both; see
`Docs/markdown/Steam_Multiplayer.md`.

Before shipping a real game, set your own Steam App ID through the editor,
configure achievements in Steamworks, and
launch the published game through Steam. Do not upload `steam_appid.txt` to a
Steam depot. See [Valve's API overview](https://partner.steamgames.com/doc/sdk/api)
and [Steamworks.NET installation](https://steamworks.github.io/installation/).

SHA-256 of the pinned Windows x64 files:

- `Steamworks.NET.dll`: `88542B6163D3BF1597E80ED626E38D9D8CEECF7A363892017DFE158B3CC0A7E2`
- `steam_api64.dll`: `EB17909A76668CF9AE0B92A618A34A50F6C73D3A6787CB4DD8CE36A8B10BFB75`
