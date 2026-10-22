# Networking from scripts

`Engine.Networking` provides the essential gameplay layer described by the
[networking architecture guide](Unity%20Networking%20Custom%20Engine%20Guide.md):
a transport seam, reliable commands/events, unreliable sequenced messages,
host-written persistent variables, bounded serialization, and local network ticks.
The Steam implementation uses the engine's existing lobby and relay transport.

Create one session in a controller script and share its `Network` reference with
other scripts. Connect Steam through Anvil's Steam settings before hosting or
joining. Creating a session works with Steam offline; `Host`, `FindAndJoin` and
`Join` return without starting a request until Steam is connected and logged on.
Call the desired operation again once Steam is ready. `Status` explains the state.
Two real players need separate signed-in Steam accounts, normally on two PCs.

## A host-authoritative script

This example defines a shared score, a client request, and a host event. Both
players run the same script. Register it in `ScriptRegistry` and rebuild as with
other [script behaviours](Script_Behaviours.md).

```csharp
using Engine.Logic;
using Engine.Networking;
using Engine.Scripting;
using Microsoft.Xna.Framework.Input;

public sealed class ScoreScript : ScriptBehaviour
{
    private NetworkVariable<int> _score;

    // Keep the controller receiving packets while a local pause menu is open.
    public override bool UpdateWhilePaused => true;

    public override void Start()
    {
        var net = CreateNetwork("my-game-score-v1", tickRate: 20);
        _score = net.CreateVariable("match/score", 0);
        _score.Changed += (before, after) => Log($"Score: {after}");
        net.PeerJoined += peer => Log($"{net.NameOf(peer)} joined");
        net.RegisterServerRpc("request-point", (sender, data) =>
        {
            data.RequireEnd();
            // Demo only: a real game must validate the sender's gameplay action
            // and apply rate limits before changing authoritative state.
            _score.Value++;
            net.CallClientRpc("point-awarded");
        });
        net.RegisterClientRpc("point-awarded", (sender, data) =>
        {
            data.RequireEnd();
            Log("The host awarded a point");
        });
    }

    public override void Update()
    {
        if (GameInput.WasPressed(Keys.H)) Network.Host(4);
        if (GameInput.WasPressed(Keys.J)) Network.FindAndJoin();
        if (GameInput.WasPressed(Keys.L)) Network.Leave();
        if (GameInput.WasPressed(Keys.Space) && Network.IsConnected)
            Network.CallServerRpc("request-point");
    }
}
```

The script owns the session. It polls automatically **before** its `Update` and
disposes the transport on Stop, disable, removal, scene teardown or a script
fault. There is no need to implement `Stop` just to release networking. A
controller that should survive scene loads can use `DontDestroyOnLoad()`.
Ordinary scripts stop polling while paused; use `UpdateWhilePaused` on the
controller if networking should continue. Callbacks run on the engine thread
inside the controller's script context. Other scripts sharing the session must
not dispose it or call `Update` themselves. They should unregister their own
message/RPC routes and unsubscribe their event handlers when they stop.

## Session functions

| API | Behavior |
| --- | --- |
| `CreateNetwork(gameKey, tickRate = 20)` | Creates the script-owned Steam session. Use a stable game/protocol key shared by all compatible builds. |
| `CreateNetwork(INetworkTransport, tickRate)` | Uses a custom transport with the same scripting API; the session takes ownership on successful creation. |
| `Host(maxPlayers = 4)` | Creates a Steam lobby; its owner is the authoritative server. |
| `FindAndJoin()` / `Join(lobbyId)` | Finds a compatible lobby, or joins a known ID. Direct joining requires both builds to use the same protocol/schema. |
| `Leave()` / `InviteFriends()` | Leaves or cancels a pending lobby operation; opens Steam's lobby invite overlay. |
| `IsConnected`, `IsServer`, `IsClient` | Lobby connection and authority; the host's `IsClient` is false. |
| `LocalId`, `ServerId`, `Peers` | Steam user IDs, with `Peers` excluding the local player. Peers update during polling. |
| `SessionId` | Steam lobby ID to share for direct `Join`. Zero while disconnected. |
| `Connected`, `Disconnected`, `PeerJoined`, `PeerLeft` | Local lifecycle and membership events, delivered during polling or explicit Leave/Dispose. |
| `NameOf(peer)`, `PingMs(peer)`, `Status` | Display name, link ping (-1 when unknown), and lobby status. |
| `TickRate`, `Tick`, `TickDelta`, `NetworkTick` | Local tick scheduling (default 20 Hz, allowed 1..120); at most four catch-up ticks per frame. Tick count resets on disconnect. |

The Steam adapter reserves channel `17185` and permits one scripting transport
per process. Share one session rather than creating a lobby on every player
object. `new NetworkSession(transport)` is also available outside behaviours;
its caller must poll `Update(realDeltaSeconds)` once per frame and dispose it.
All operations, including registration and disposal, belong on the game thread.

## Messages and RPCs

| API | Behavior |
| --- | --- |
| `RegisterMessage(name, (sender, reader) => ...)` | Registers a named message handler. Accepts current lobby peers. |
| `Send(peer, name, payload, delivery)` | Sends to one remote peer. |
| `Broadcast(name, payload, delivery)` | Sends to every remote peer, excluding the local player. |
| `RegisterServerRpc(name, handler)` / `CallServerRpc(name, payload)` | Reliable request executed only by the host. Calling on the host executes locally once. |
| `RegisterClientRpc(name, handler)` / `CallClientRpc(name, payload, peer = 0)` | Reliable host event accepted only by clients from the host. Target 0 means all remote clients. The host does not execute it locally. |

`NetworkDelivery.Reliable` is the default. The transport supplies reliable,
ordered delivery. Use it for commands and occasional events.
`NetworkDelivery.UnreliableSequenced` drops duplicates and older messages for
each sender/name; use it for frequently refreshed transforms or input samples.
It does not retransmit or fragment packets. Sends return whether the transport
accepted the packet, not whether the remote gameplay handler ran. Offline or
unknown peers return false. RPC names need matching registrations on receivers.
Unknown routes are ignored. Duplicate registrations throw.
`UnregisterMessage`, `UnregisterServerRpc` and `UnregisterClientRpc` release a
route when a script sharing the controller's session stops.

```csharp
var writer = new NetworkWriter(); // Store and Clear it to reuse the payload buffer.
Network.RegisterMessage("player/transform", (sender, reader) =>
{
    var position = reader.ReadVector3();
    var rotation = reader.ReadQuaternion();
    reader.RequireEnd();
    // Validate position/rotation and ownership, then apply to this sender's object.
});
Network.NetworkTick += tick =>
{
    writer.Clear();
    writer.WriteVector3(Position);
    writer.WriteQuaternion(Microsoft.Xna.Framework.Quaternion.CreateFromRotationMatrix(Rotation));
    Network.Broadcast("player/transform", writer, NetworkDelivery.UnreliableSequenced);
};
```

The sender ID comes from the transport, never from an ID inside the packet.
Host RPC handlers must still validate ownership, movement, damage, resources and
request frequency. A peer message deliberately allows any current peer; use
RPCs/variables when the host must control the result.

## Persistent variables

Create the same names and types on every peer, for example
`Network.CreateVariable("door/7/open", false)`. Names are ordinal and case
sensitive; include stable object IDs yourself for multiple objects. Initial
state is requested when a client joins or registers a variable later. Changes
are sent reliably on network ticks; unchanged values generate no further state
packets. Failed sends stay dirty for retry. Intermediate writes within one tick
coalesce to the latest value. RPC events and variables have no shared timing
guarantee; an event may arrive before the next variable update.

`variable.Value` reads the local cached value. Only a connected host can assign
it; client/offline assignments throw. `CanWrite` reports this permission.
`IsSynchronized` is false on a client until an initial snapshot arrives. A client
may see its own initializer before that snapshot. `Changed(before, after)` runs
when the local value changes, including the initial snapshot if it differs.
Disconnect invalidates synchronization but retains cached values. A reconnect
requests fresh state. Variables are not saved with scenes.

Built-in value codecs cover `bool`, `int`, `uint`, `ulong`, `float`, `double`,
`string`, `Vector2`, `Vector3` and `Quaternion`. For an immutable custom value:

```csharp
var codec = new NetworkCodec<(int Health, bool Alive)>(
    (writer, value) => { writer.WriteInt(value.Health); writer.WriteBool(value.Alive); },
    reader => (reader.ReadInt(), reader.ReadBool()));
var state = Network.CreateVariable("player/7/state", (100, true), codec);
```

Use immutable values and assign the entire value to mark it changed. Do not
mutate fields or collections inside a reference-valued variable. Both builds
must agree on field order, types, codecs and route names; change the game key
when making an incompatible schema change.

## Serialization and scope

`NetworkWriter`/`NetworkReader` use little endian fields, strict UTF-8 strings
with a two-byte byte count, and finite floating-point values. Available pairs:
`Write/ReadByte`, `Bool`, `Int`, `UInt`, `ULong`, `Float`, `Double`, `String`,
`Vector2`, `Vector3`, `Quaternion`, and `Bytes` (the caller supplies the byte
count when reading). `Clear`, `Length` and `ToArray` support payload reuse and
copying. `Remaining` and `RequireEnd` validate complete payload consumption.

The whole packet is limited to 1200 bytes. Header size is `13 + UTF8(name).Length`,
leaving the rest for payload. Names allow 1..96 UTF-8 bytes. Oversized writes
throw locally. Each session allows up to 256 handlers and 256 variables. Receive
polling consumes at most 256 packets per frame, counting malformed packets.
Truncated, oversized, invalid UTF-8, non-finite numbers and unauthorized packets
are dropped. Parse and validate all fields **before** changing gameplay state.
Readers passed to callbacks borrow the transport buffer; do not retain them.
Other callback exceptions escape to normal script fault handling and cleanup.
Custom codecs/handlers should throw `InvalidDataException` for malformed input;
the session treats that exception as a rejected packet.

This is a gameplay foundation. It does not add automatic network prefab
spawn/despawn, object ownership transfer, transform replication, snapshot delta
compression, interest management, prediction, rollback, lag compensation, a
synchronized clock, or a dedicated-server/UDP transport. Steam handles transport
reliability and relay routing. Host migration ends the scripting session;
players must explicitly host/join again. RPCs are runtime registrations rather
than attributes or generated method wrappers.

`MultiplayerTestScript` demonstrates named movement messages and local spawning
for peer-owned capsules. Its smoothing remains gameplay code. Test protocol
behavior without Steam with:

```powershell
dotnet run --project Tests/Components/Components.csproj -- --networking
```
