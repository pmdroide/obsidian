# Steam multiplayer (peer to peer)

`Scenes/MultiplayerTest.obsc` is a small arena for testing peer-to-peer play through Steam. Each
player is a coloured capsule with a dark visor showing which way it faces. One player hosts a Steam
lobby and the others join it. There is no dedicated server: every player sends their own capsule's
position directly to every other player.

## Run the sample

You need **two Steam accounts, each signed in to its own Steam client**. Normally that means two
PCs, because Steam runs one client per Windows session. Both copies of the engine must use the same
App ID (default `480`, Valve's Spacewar test app).

1. Start Steam and sign in.
2. Open the scene: in Anvil, open `Scenes/MultiplayerTest.obsc` and press **Play**. In the standalone
   game, pick **MultiplayerTest** from Main Menu > Scenes. It is last in the scene list.
3. Connect to Steam. Steam is off by default. Use **Anvil > Window > Steam > Connect**, or press **C**
   in the scene. Both turn Steam on in `Content/System/SteamSettings.json`, so it connects on the
   next launch too.
4. On the first PC, press **H** to host a lobby.
5. On the second PC, press **J** to find the lobby and join it. You can also press **I** on the host
   to invite a friend through the Steam overlay. Accepting the invite joins the lobby.

The panel in the top-right shows the Steam account, the lobby state and the player list: each
player's colour, name, ping (or connection state) and who is hosting. Without Steam, the local
capsule still moves, so the scene also works offline.

| Input | Action |
| --- | --- |
| W A S D / left stick | Move, relative to the camera |
| Space / gamepad A | Jump |
| Hold right mouse + move | Orbit the camera |
| H | Host a lobby (up to 4 players) |
| J | Find a lobby and join it |
| I | Invite friends (Steam overlay) |
| L | Leave the lobby |
| C | Connect to Steam |

Notes:

- On App 480 every Steamworks developer shares the same lobby list. The sample tags its lobbies
  with `obsidian_game = obsidian-capsules-v1`, and **J** only finds lobbies with that tag.
- The Steam overlay (**I**) may not appear inside the Anvil viewport. In that case, invite from the
  standalone game, or use **J**.
- Capsules don't collide with each other or with the walls. Movement is clamped to the arena.
- Names with characters outside Latin-1 show as `?`, because the UI fonts only include those characters.

## How it works

```
 PC A (host)                          PC B
 ┌──────────────────┐   Steam lobby   ┌──────────────────┐
 │ SteamP2PSession  │◄───members─────►│ SteamP2PSession  │
 │  own capsule ────┼──state 20/s────►│  remote capsule  │
 │  remote capsule ◄┼────state 20/s───┼─ own capsule     │
 └──────────────────┘  Steam relays   └──────────────────┘
```

- **Lobby = who is playing.** `SteamMatchmaking` creates and joins the lobby. Peers join and leave
  when the lobby's member list changes.
- **Messages go directly between peers.** `SteamNetworkingMessages` sends them through Valve's relay
  network, so there are no ports to open and no IP addresses to share. Sessions are accepted only
  from lobby members.
- **Each player owns their capsule.** About 20 times a second, the script sends an unreliable
  21-byte state to every peer: `[kind 1][sequence u32][x f32][y f32][z f32][yaw f32]`, little
  endian. Older packets that arrive late are dropped by sequence number. A remote capsule appears
  when its first state arrives, eases towards the newest state, and snaps after a jump of more than 4 m.
- **Colours come from the Steam ID.** So every peer shows each player in the same colour.

## Code

| File | Role |
| --- | --- |
| `Engine/Steam/SteamP2PSession.cs` | Reusable: lobby host/find/join/leave/invite, peer join/leave events, send/broadcast/receive, ping per peer. |
| `Engine/Content/Scripts/MultiplayerTestScript.cs` | The sample: local movement and camera, state packets, capsule spawning, HUD. |
| `Engine/Content/UI/MultiplayerTest.xml` / `.css` | The HUD (Vista). |
| `Engine/Content/Scenes/MultiplayerTest.obsc` | The arena. The Main Camera runs the **Multiplayer Test** script. |
| `Engine/Content/GameObjects/Default/capsule.obj` | The capsule mesh, model key `Capsule` (Z up, radius 0.5, height 2, centred on its origin). |

### Using SteamP2PSession in your own script

```csharp
SteamP2PSession session;

public override void Update()
{
    bool online = SteamService.Current?.Status is { IsConnected: true, IsLoggedOn: true };
    if (online && session == null)
    {
        session = new SteamP2PSession("my-game-v1"); // lobby tag: searches only match this
        session.PeerJoined += id => Log($"{session.NameOf(id)} joined");
        session.PeerLeft += id => Log($"{session.NameOf(id)} left");
    }
    if (session == null) return;
    session.Receive((sender, buffer, length) => { /* copy what you keep; the buffer is reused */ });
    session.Broadcast(packet, packetLength, reliable: false);
}

public override void Stop() => session?.Dispose(); // leaves the lobby
```

- Create the session on the game thread (any script) once Steam is connected and logged on.
  Dispose it if Steam disconnects. Lobby results and peer events arrive from the callback pump
  in `SteamService.Update`, which runs every frame before scripts.
- `Host(maxMembers)`, `FindAndJoin()`, `Join(lobbyId)`, `Leave()` and `InviteFriends()` change
  the lobby. Read `State`, `Status` (a one-line description for a HUD), `Peers`, `IsOwner` and `OwnerId`.
- `Send` and `Broadcast` take `reliable`. Use unreliable for state you send every frame, and
  reliable for events that must arrive once and in order. Keep messages under about 1 KB.
- Accepting a Steam invite, or choosing **Join Game** in the friends list while the game runs,
  joins that lobby automatically. Launching the game from an invite (`+connect_lobby`) isn't handled yet.

### Spawning gameobjects from scripts

The sample creates the capsules with two `ScriptBehaviour` helpers that other scripts can use too:

- `Spawn(modelKey, position, name)` adds a gameobject from a model key such as `"Capsule"`, `"Cube"`
  or `"IsoSphere"`. Add components with `entity.AddComponent(...)`, for example a `MaterialComponent`
  to colour it.
- `Destroy(entity)` removes a gameobject that this script spawned.

Spawned gameobjects only exist for the Play session. They are removed when the script stops
(including when Play stops in Anvil), and saving the scene leaves them out.

## Shipping

The sample uses App 480. A real game needs its own App ID (Anvil > Window > Steam) and should be
launched through Steam. See `Engine/thirdparty/steam/README.md`.
