using System.Buffers.Binary;
using Engine.Networking;
using Engine.Scripting;
using Microsoft.Xna.Framework;

/// <summary>Networking protocol and script lifecycle checks without Steam, sockets or a graphics device.</summary>
internal static class NetworkingChecks
{
    public static void Run()
    {
        Serialization();
        Sessions();
        Lifecycle();
    }

    private static void Serialization()
    {
        var writer = new NetworkWriter();
        writer.WriteBool(true);
        writer.WriteInt(-23);
        writer.WriteUInt(uint.MaxValue);
        writer.WriteULong(ulong.MaxValue);
        writer.WriteFloat(1.25f);
        writer.WriteDouble(-2.5);
        writer.WriteString("Olá 世界");
        writer.WriteVector2(new Vector2(2, 3));
        writer.WriteVector3(new Vector3(4, 5, 6));
        writer.WriteQuaternion(Quaternion.Identity);
        var reader = new NetworkReader(writer.ToArray());
        Check(reader.ReadBool() && reader.ReadInt() == -23 && reader.ReadUInt() == uint.MaxValue &&
              reader.ReadULong() == ulong.MaxValue && reader.ReadFloat() == 1.25f && reader.ReadDouble() == -2.5 &&
              reader.ReadString() == "Olá 世界" && reader.ReadVector2() == new Vector2(2, 3) &&
              reader.ReadVector3() == new Vector3(4, 5, 6) && reader.ReadQuaternion() == Quaternion.Identity,
            "network primitives, UTF-8 and transforms round-trip without reflection");
        reader.RequireEnd();
        Expect<InvalidDataException>(() => reader.ReadInt(), "truncated reads fail within packet bounds");
        Expect<InvalidDataException>(() => new NetworkReader(new byte[] { 2 }).ReadBool(), "invalid booleans are rejected");
        Expect<InvalidDataException>(() => new NetworkReader(new byte[] { 1, 0, 0xFF }).ReadString(), "invalid UTF-8 is rejected");
        Expect<InvalidOperationException>(() => new NetworkWriter(2).WriteInt(1), "writes cannot exceed their capacity");
        Expect<ArgumentOutOfRangeException>(() => writer.WriteFloat(float.NaN), "non-finite outgoing floats are rejected");
        Expect<InvalidDataException>(() => new NetworkReader(BitConverter.GetBytes(float.PositiveInfinity)).ReadFloat(),
            "non-finite incoming floats are rejected");
        writer.Clear();
        writer.WriteInt(9);
        Check(writer.Length == 4, "packet writers can be reused");
    }

    private static void Sessions()
    {
        var bus = new List<MemoryTransport>();
        var hostTransport = new MemoryTransport(bus, 1);
        var clientTransport = new MemoryTransport(bus, 2);
        using var host = new NetworkSession(hostTransport);
        using var client = new NetworkSession(clientTransport);
        var health = host.CreateVariable("player/health", 100);
        var clientHealth = client.CreateVariable("player/health", -1);
        int connects = 0, joins = 0, leaves = 0, disconnects = 0;
        host.Connected += () => connects++;
        host.PeerJoined += _ => joins++;
        host.PeerLeft += _ => leaves++;
        client.Disconnected += () => disconnects++;
        host.Host();
        client.Join(host.LocalId);
        Step(host, client);
        Check(host.IsServer && client.IsClient && client.ServerId == host.LocalId && host.Peers.SequenceEqual(new ulong[] { 2 }) &&
              connects == 1 && joins == 1, "host/client roles and peer events follow the transport membership");
        Check(clientHealth.Value == 100 && clientHealth.IsSynchronized, "a joining client receives initial host variable state");
        Check(host.SessionId == 1 && client.SessionId == 1, "session IDs can be shared for direct joining");
        Expect<InvalidOperationException>(() => clientHealth.Value = 2, "clients cannot write authoritative variables");
        int changed = 0;
        clientHealth.Changed += (oldValue, newValue) => { if (oldValue == 100 && newValue == 80) changed++; };
        hostTransport.FailSends = true;
        health.Value = 80;
        Step(host, client);
        Check(clientHealth.Value == 100, "failed transport sends do not pretend state was delivered");
        hostTransport.FailSends = false;
        Step(host, client);
        Step(host, client);
        Check(clientHealth.Value == 80 && changed == 1, "dirty variables retry failed sends and notify clients once");
        int sent = hostTransport.Sent.Count;
        health.Value = 80;
        Step(host, client);
        Check(hostTransport.Sent.Count == sent, "unchanged host variables consume no additional packets");

        // Registration order differs: a late-registered variable must request its snapshot too.
        var position = host.CreateVariable("late/position", new Vector3(1, 2, 3));
        Step(host, client);
        var clientPosition = client.CreateVariable("late/position", Vector3.Zero);
        Step(client, host);
        Step(client, host);
        Check(clientPosition.IsSynchronized && clientPosition.Value == position.Value, "late variable registration requests initial state");
        using var third = new NetworkSession(new MemoryTransport(bus, 3));
        var thirdHealth = third.CreateVariable("player/health", 0);
        third.Join(1);
        Step(host, client, third);
        Step(host, client, third);
        Check(thirdHealth.Value == 80 && thirdHealth.IsSynchronized, "late-joining peers receive current state after the variable stopped changing");
        var codec = new NetworkCodec<(int Health, bool Alive)>(
            (w, v) => { w.WriteInt(v.Health); w.WriteBool(v.Alive); },
            r => (r.ReadInt(), r.ReadBool()));
        var custom = host.CreateVariable("custom/state", (80, true), codec);
        var clientCustom = client.CreateVariable("custom/state", (0, false), codec);
        Step(host, client);
        Check(clientCustom.IsSynchronized && clientCustom.Value == custom.Value, "custom immutable variable codecs replicate structured state");
        Expect<NotSupportedException>(() => host.CreateVariable("unsupported", DateTime.MinValue), "unsupported variable types require an explicit codec");

        int serverCalls = 0, clientCalls = 0;
        ulong rpcSender = 0;
        host.RegisterServerRpc("damage", (sender, data) =>
        {
            int damage = data.ReadInt();
            data.RequireEnd();
            rpcSender = sender;
            serverCalls++;
            if (damage >= 0 && damage <= 20) health.Value -= damage;
        });
        client.RegisterClientRpc("effect", (sender, data) => { data.RequireEnd(); rpcSender = sender; clientCalls++; });
        var payload = new NetworkWriter();
        payload.WriteInt(10);
        Check(client.CallServerRpc("damage", payload), "clients can send reliable server RPCs");
        Step(host, client, third);
        Check(serverCalls == 1 && rpcSender == 2 && health.Value == 70, "server RPCs receive authenticated transport sender IDs");
        Check(host.CallServerRpc("damage", payload) && serverCalls == 2 && rpcSender == 1,
            "the host invokes its server RPC locally exactly once");
        Check(host.CallClientRpc("effect", peer: 2), "the host can target one client RPC");
        Step(client);
        Check(clientCalls == 1 && rpcSender == 1 && !client.CallClientRpc("effect"), "client RPCs require host authority");
        byte[] effect = hostTransport.Sent.Last(p => p.Peer == 2).Packet;
        clientTransport.Inject(3, effect);
        Step(client);
        Check(clientCalls == 1, "client RPCs forged by another lobby member are rejected");
        byte[] authoritative = hostTransport.Sent.First(p => p.Packet[5] == 3).Packet;
        clientTransport.Inject(3, authoritative);
        clientTransport.Inject(99, authoritative);
        Step(client);
        Check(clientHealth.Value == 70, "variable updates from non-hosts and non-members are rejected");

        var moves = new List<int>();
        client.RegisterMessage("move", (_, data) =>
        {
            int move = data.ReadInt();
            data.RequireEnd();
            moves.Add(move);
        });
        payload.Clear(); payload.WriteInt(1);
        host.Send(2, "move", payload, NetworkDelivery.UnreliableSequenced);
        byte[] first = hostTransport.Sent.Last().Packet;
        payload.Clear(); payload.WriteInt(2);
        host.Send(2, "move", payload, NetworkDelivery.UnreliableSequenced);
        byte[] second = hostTransport.Sent.Last().Packet;
        clientTransport.Incoming.Clear();
        clientTransport.Inject(1, second);
        clientTransport.Inject(1, first);
        clientTransport.Inject(1, second);
        Step(client);
        Check(moves.SequenceEqual(new[] { 2 }), "unreliable messages discard reordered and duplicate packets per sender and name");
        byte[] wrapped = (byte[])second.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(wrapped.AsSpan(7), uint.MaxValue);
        client.Leave();
        client.Join(1);
        clientTransport.Inject(1, wrapped);
        byte[] afterWrap = (byte[])second.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(afterWrap.AsSpan(7), 0);
        clientTransport.Inject(1, afterWrap);
        Step(client);
        Check(moves.Count == 3, "sequence comparison handles uint wraparound and resets on reconnect");

        // Malformed packets must not advance ordering or stop the next valid message.
        int previousCount = moves.Count;
        byte[] malformed = second[..^1];
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(7), 1000);
        clientTransport.Inject(1, malformed);
        byte[] valid = (byte[])second.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(valid.AsSpan(7), 1);
        clientTransport.Inject(1, valid);
        clientTransport.Inject(1, new byte[4]);
        clientTransport.Inject(1, new byte[NetworkSession.MaxPacketSize + 1]);
        Step(client);
        Check(moves.Count == previousCount + 1, "truncated and oversized packets are dropped without poisoning sequence state");
        for (int i = 0; i < 300; i++) clientTransport.Inject(99, new byte[1]);
        client.Update(0);
        Check(clientTransport.Incoming.Count == 44, "the per-frame receive budget also counts rejected packets");
        clientTransport.Incoming.Clear();
        Expect<InvalidOperationException>(() => client.RegisterMessage("move", (_, _) => { }), "duplicate message routes fail at registration");
        Expect<ArgumentException>(() => client.Send(1, new string('x', 97)), "route names have a bounded UTF-8 size");
        payload.Clear(); payload.WriteBytes(new byte[NetworkSession.MaxPacketSize]);
        Expect<InvalidOperationException>(() => client.Send(1, "oversize", payload), "payloads must leave space for protocol headers");
        Expect<ArgumentOutOfRangeException>(() => client.Update(float.NaN), "network ticks reject invalid deltas");
        ulong beforeTick = client.Tick;
        client.Update(10f);
        Check(client.Tick - beforeTick == 4, "network tick catch-up is bounded after a stalled frame");
        Check(client.UnregisterMessage("move") && !client.UnregisterMessage("move"), "shared-session scripts can unregister their message routes");
        clientTransport.Inject(1, valid);
        previousCount = moves.Count;
        Step(client);
        Check(moves.Count == previousCount, "unregistered routes no longer execute gameplay handlers");

        client.Leave();
        Step(host);
        Check(disconnects == 2 && leaves == 1 && !clientHealth.IsSynchronized && !client.Send(1, "move"),
            "leave notifies peers, invalidates synchronized state and prevents further sends");
        client.Join(1);
        Step(host, client);
        hostTransport.Server = 3;
        Step(host);
        Check(!host.IsConnected, "authority changes end the session instead of silently migrating stale state");
    }

    private static void Lifecycle()
    {
        var bus = new List<MemoryTransport>();
        var transport = new MemoryTransport(bus, 1);
        var script = new NetworkScript();
        var network = script.CreateNetwork(transport);
        network.Host();
        int ticks = 0;
        network.NetworkTick += _ => ticks++;
        script.Tick(0.1f);
        Check(ticks == 2 && script.SawConnected, "script-owned sessions poll automatically before gameplay Update");
        Expect<InvalidOperationException>(() => script.CreateNetwork(transport), "one script cannot accidentally own two sessions");
        network.Disconnected += () => throw new ApplicationException("disconnect callback");
        Expect<ApplicationException>(script.Shutdown, "disconnect callback exceptions escape for script fault reporting");
        Check(transport.Disposed && network.IsDisposed, "script shutdown releases transport even if Stop and disconnect callbacks throw");
        network.Dispose();
        Expect<ObjectDisposedException>(() => network.Host(), "disposed sessions cannot be restarted");
        using var steam = new SteamNetworkTransport("networking-checks");
        Expect<InvalidOperationException>(() => new SteamNetworkTransport("another-session"),
            "Steam lobby/channel ownership prevents two scripting transports consuming each other's packets");
        steam.Dispose();
        using var replacement = new SteamNetworkTransport("replacement-session");
        Check(!replacement.IsConnected, "disposing a Steam scripting session releases its process ownership without needing Steam");
    }

    private sealed class NetworkScript : ScriptBehaviour
    {
        public bool SawConnected;
        public override void Update() => SawConnected = Network?.IsConnected == true;
        public override void Stop() => throw new ApplicationException("Stop callback");
    }

    private static void Step(params NetworkSession[] sessions)
    {
        foreach (var session in sessions) session.Update(0.05f);
    }

    private sealed class MemoryTransport : INetworkTransport
    {
        private readonly List<MemoryTransport> _bus;
        public readonly List<(ulong Sender, byte[] Packet)> Incoming = new();
        public readonly List<(ulong Peer, byte[] Packet, bool Reliable)> Sent = new();
        public bool FailSends, Disposed;
        public ulong Server;
        public bool IsConnected { get; private set; }
        public ulong LocalId { get; }
        public ulong SessionId => IsConnected ? Server : 0;
        public ulong ServerId => Server;
        public IReadOnlyList<ulong> Peers => _bus.Where(p => p != this && p.IsConnected && p.Server == Server).Select(p => p.LocalId).ToArray();
        public string Status => IsConnected ? "Connected" : "Idle";
        public MemoryTransport(List<MemoryTransport> bus, ulong id) { _bus = bus; LocalId = id; bus.Add(this); }
        public void Host(int maxPlayers) { Server = LocalId; IsConnected = true; }
        public void FindAndJoin() => Join(_bus.First(p => p.IsConnected).Server);
        public void Join(ulong sessionId) { Server = sessionId; IsConnected = true; }
        public void Leave() { IsConnected = false; Incoming.Clear(); }
        public bool InviteFriends() => IsConnected;
        public string NameOf(ulong peer) => "Peer " + peer;
        public int PingMs(ulong peer) => 0;
        public bool Send(ulong peer, byte[] data, int length, bool reliable)
        {
            if (FailSends) return false;
            var destination = _bus.FirstOrDefault(p => p.LocalId == peer && p.IsConnected);
            if (!IsConnected || destination == null) return false;
            byte[] packet = data.AsSpan(0, length).ToArray();
            Sent.Add((peer, packet, reliable));
            destination.Inject(LocalId, packet);
            return true;
        }
        public void Inject(ulong sender, byte[] packet) => Incoming.Add((sender, packet));
        public void Poll(Action<ulong, byte[], int> receive, int maxMessages)
        {
            for (int i = 0; i < maxMessages && Incoming.Count > 0 && !Disposed; i++)
            {
                var packet = Incoming[0];
                Incoming.RemoveAt(0);
                receive(packet.Sender, packet.Packet, packet.Packet.Length);
            }
        }
        public void Dispose() { Leave(); Disposed = true; }
    }

    private static void Expect<T>(Action action, string description) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, description); return; }
        throw new Exception("FAIL: " + description);
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
