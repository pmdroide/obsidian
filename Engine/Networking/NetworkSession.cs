using System.Text;

namespace Engine.Networking;

/// <summary>
/// Game-thread scripting API over a lobby transport. The lobby owner is the authoritative server.
/// Update once per frame, or create through ScriptBehaviour.CreateNetwork for automatic polling/cleanup.
/// </summary>
public sealed class NetworkSession : IDisposable
{
    public const int MaxPacketSize = 1200;
    public const int MaxNameBytes = 96;
    private const uint Magic = 0x4E53424F; // OBSN, little endian
    private const byte ProtocolVersion = 1;
    private enum Kind : byte { Message, ServerRpc, ClientRpc, Variable, RequestVariable }
    private readonly INetworkTransport _transport;
    private readonly Dictionary<(Kind, string), Action<ulong, NetworkReader>> _handlers = new();
    private readonly Dictionary<string, NetworkVariable> _variables = new(StringComparer.Ordinal);
    private readonly Dictionary<(ulong, Kind, string), uint> _sequences = new();
    private readonly List<ulong> _peers = new();
    private readonly IReadOnlyList<ulong> _peerView;
    private bool _connected, _disposed, _disposing;
    private ulong _serverId;
    private uint _sequence;
    private double _accumulator;
    private int _tickRate;
    public bool IsConnected => !_disposed && _transport.IsConnected;
    public bool IsServer => IsConnected && LocalId != 0 && LocalId == ServerId;
    public bool IsClient => IsConnected && !IsServer;
    public ulong LocalId => _disposed ? 0 : _transport.LocalId;
    public ulong SessionId => IsConnected ? _transport.SessionId : 0;
    public ulong ServerId => IsConnected ? _transport.ServerId : 0;
    public IReadOnlyList<ulong> Peers => _peerView;
    public string Status => _disposed ? "Network session disposed." : _transport.Status;
    public bool IsDisposed => _disposed;
    public ulong Tick { get; private set; }
    public float TickDelta => 1f / TickRate;
    /// <summary>Local scheduling rate, 1..120. It is not a synchronized server clock.</summary>
    public int TickRate
    {
        get => _tickRate;
        set
        {
            if (value < 1 || value > 120) throw new ArgumentOutOfRangeException(nameof(value));
            _tickRate = value;
        }
    }
    public event Action Connected;
    public event Action Disconnected;
    public event Action<ulong> PeerJoined;
    public event Action<ulong> PeerLeft;
    /// <summary>At most four catch-up ticks per frame. Send unreliable movement or update host state here.</summary>
    public event Action<ulong> NetworkTick;

    /// <summary>The session owns the supplied transport and disposes it.</summary>
    public NetworkSession(INetworkTransport transport, int tickRate = 20)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        TickRate = tickRate;
        _peerView = _peers.AsReadOnly();
    }
    public void Host(int maxPlayers = 4) { ThrowIfDisposed(); _transport.Host(maxPlayers); }
    public void FindAndJoin() { ThrowIfDisposed(); _transport.FindAndJoin(); }
    public void Join(ulong sessionId)
    {
        ThrowIfDisposed();
        if (sessionId == 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
        Leave();
        _transport.Join(sessionId);
    }
    public void Leave()
    {
        if (_disposed) return;
        _transport.Leave();
        SynchronizeConnection();
    }
    public bool InviteFriends() => !_disposed && _transport.InviteFriends();
    public string NameOf(ulong peer) => _disposed ? peer.ToString() : _transport.NameOf(peer);
    public int PingMs(ulong peer) => IsConnected ? _transport.PingMs(peer) : -1;

    public void RegisterMessage(string name, Action<ulong, NetworkReader> handler) => Register(Kind.Message, name, handler);
    /// <summary>Runs only on the host. The sender ID comes from the transport; validate gameplay permissions in the handler.</summary>
    public void RegisterServerRpc(string name, Action<ulong, NetworkReader> handler) => Register(Kind.ServerRpc, name, handler);
    /// <summary>Runs only on clients, and accepts calls only from the current host.</summary>
    public void RegisterClientRpc(string name, Action<ulong, NetworkReader> handler) => Register(Kind.ClientRpc, name, handler);
    public bool UnregisterMessage(string name) => Unregister(Kind.Message, name);
    public bool UnregisterServerRpc(string name) => Unregister(Kind.ServerRpc, name);
    public bool UnregisterClientRpc(string name) => Unregister(Kind.ClientRpc, name);
    private bool Unregister(Kind kind, string name)
    {
        ThrowIfDisposed();
        ValidateName(name);
        if (!_handlers.Remove((kind, name))) return false;
        foreach (var key in _sequences.Keys.Where(k => k.Item2 == kind && k.Item3 == name).ToArray()) _sequences.Remove(key);
        return true;
    }
    private void Register(Kind kind, string name, Action<ulong, NetworkReader> handler)
    {
        ThrowIfDisposed();
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers.Count >= 256) throw new InvalidOperationException("Too many network handlers.");
        if (!_handlers.TryAdd((kind, name), handler)) throw new InvalidOperationException($"Network handler '{name}' is already registered.");
    }
    public NetworkVariable<T> CreateVariable<T>(string name, T initialValue = default, NetworkCodec<T> codec = null)
    {
        ThrowIfDisposed();
        ValidateName(name);
        if (_variables.ContainsKey(name)) throw new InvalidOperationException($"Network variable '{name}' is already registered.");
        if (_variables.Count >= 256) throw new InvalidOperationException("Too many network variables.");
        var variable = new NetworkVariable<T>(this, name, initialValue, codec);
        _variables.Add(name, variable);
        return variable;
    }
    /// <summary>Sends to a remote peer. Payload may be null for an empty message.</summary>
    public bool Send(ulong peer, string name, NetworkWriter payload = null, NetworkDelivery delivery = NetworkDelivery.Reliable) =>
        SendPacket(peer, Build(Kind.Message, name, payload, delivery), delivery);
    public bool Broadcast(string name, NetworkWriter payload = null, NetworkDelivery delivery = NetworkDelivery.Reliable) =>
        BroadcastPacket(Build(Kind.Message, name, payload, delivery), delivery);
    /// <summary>Reliable command to the host; on the host it invokes the registered handler locally once.</summary>
    public bool CallServerRpc(string name, NetworkWriter payload = null)
    {
        byte[] packet = Build(Kind.ServerRpc, name, payload, NetworkDelivery.Reliable);
        if (!IsConnected) return false;
        if (!IsServer) return SendPacket(ServerId, packet, NetworkDelivery.Reliable);
        Receive(LocalId, packet, packet.Length);
        return true;
    }
    /// <summary>Host-only reliable event to all remote clients, or one client. The host does not execute it locally.</summary>
    public bool CallClientRpc(string name, NetworkWriter payload = null, ulong peer = 0)
    {
        byte[] packet = Build(Kind.ClientRpc, name, payload, NetworkDelivery.Reliable);
        if (!IsServer) return false;
        return peer == 0 ? BroadcastPacket(packet, NetworkDelivery.Reliable) : SendPacket(peer, packet, NetworkDelivery.Reliable);
    }

    private byte[] Build(Kind kind, string name, NetworkWriter payload, NetworkDelivery delivery, uint? version = null)
    {
        ThrowIfDisposed();
        ValidateName(name);
        if (delivery != NetworkDelivery.Reliable && delivery != NetworkDelivery.UnreliableSequenced)
            throw new ArgumentOutOfRangeException(nameof(delivery));
        var packet = new NetworkWriter();
        packet.WriteUInt(Magic);
        packet.WriteByte(ProtocolVersion);
        packet.WriteByte((byte)kind);
        packet.WriteByte((byte)delivery);
        packet.WriteUInt(version ?? ++_sequence);
        packet.WriteString(name);
        if (payload != null) packet.WriteBytes(payload.Buffer.AsSpan(0, payload.Length));
        return packet.ToArray();
    }
    internal void ValidateVariablePayload(string name, byte[] data) =>
        Build(Kind.Variable, name, Payload(data), NetworkDelivery.Reliable, 0);
    private static NetworkWriter Payload(byte[] data)
    {
        var writer = new NetworkWriter();
        writer.WriteBytes(data);
        return writer;
    }
    private bool SendPacket(ulong peer, byte[] packet, NetworkDelivery delivery) =>
        IsConnected && peer != LocalId && _transport.Peers.Contains(peer) &&
        _transport.Send(peer, packet, packet.Length, delivery == NetworkDelivery.Reliable);
    private bool BroadcastPacket(byte[] packet, NetworkDelivery delivery)
    {
        if (!IsConnected) return false;
        bool sent = true;
        foreach (ulong peer in _transport.Peers.ToArray()) sent &= SendPacket(peer, packet, delivery);
        return sent;
    }

    /// <summary>Polls up to 256 packets, delivers peer events, then advances the local network tick.</summary>
    public void Update(float deltaTime)
    {
        if (_disposed) return;
        if (!float.IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        SynchronizeConnection();
        if (_disposed) return;
        _transport.Poll(Receive, 256);
        SynchronizeConnection();
        if (!IsConnected) return;
        _accumulator += deltaTime;
        double interval = 1.0 / TickRate;
        int steps = 0;
        while (_accumulator + 1e-8 >= interval && steps++ < 4 && IsConnected)
        {
            _accumulator = Math.Max(0, _accumulator - interval);
            Tick++;
            NetworkTick?.Invoke(Tick);
            if (!IsConnected) break;
            foreach (var variable in _variables.Values.ToArray())
            {
                if (IsServer && variable.Dirty)
                {
                    uint version = variable.Version;
                    bool sent = BroadcastPacket(Build(Kind.Variable, variable.Name, Payload(variable.Encode()), NetworkDelivery.Reliable, version), NetworkDelivery.Reliable);
                    if (variable.Version == version) variable.Dirty = !sent;
                }
                else if (IsClient && !variable.HasSnapshot)
                    SendPacket(ServerId, Build(Kind.RequestVariable, variable.Name, null, NetworkDelivery.Reliable), NetworkDelivery.Reliable);
                if (!IsConnected) break;
            }
        }
        // Bound work after a stalled frame; do not burst an unbounded backlog.
        if (_accumulator >= interval) _accumulator %= interval;
    }
    private void SynchronizeConnection()
    {
        // Host migration requires a separate authority-transfer protocol. End this session instead.
        if (_connected && IsConnected && _serverId != _transport.ServerId) _transport.Leave();
        if (_connected && !IsConnected)
        {
            _connected = false;
            _serverId = 0;
            _sequences.Clear();
            _accumulator = 0;
            Tick = 0;
            foreach (var variable in _variables.Values) { variable.HasSnapshot = false; variable.Dirty = true; }
            var previous = _peers.ToArray();
            _peers.Clear();
            foreach (ulong peer in previous) PeerLeft?.Invoke(peer);
            Disconnected?.Invoke();
        }
        if (!IsConnected) return;
        if (!_connected)
        {
            _connected = true;
            _serverId = ServerId;
            Connected?.Invoke();
        }
        if (!IsConnected) return;
        foreach (ulong peer in _peers.ToArray())
            if (!_transport.Peers.Contains(peer))
            {
                _peers.Remove(peer);
                foreach (var key in _sequences.Keys.Where(k => k.Item1 == peer).ToArray()) _sequences.Remove(key);
                PeerLeft?.Invoke(peer);
            }
        foreach (ulong peer in _transport.Peers.ToArray())
        {
            if (!IsConnected) break;
            if (_peers.Contains(peer)) continue;
            _peers.Add(peer);
            PeerJoined?.Invoke(peer);
        }
    }
    private void Receive(ulong sender, byte[] buffer, int length)
    {
        SynchronizeConnection();
        if (!IsConnected || (sender != LocalId && !_transport.Peers.Contains(sender)) ||
            buffer == null || length < 13 || length > MaxPacketSize || length > buffer.Length) return;
        try
        {
            var reader = new NetworkReader(buffer, 0, length);
            if (reader.ReadUInt() != Magic || reader.ReadByte() != ProtocolVersion) return;
            var kind = (Kind)reader.ReadByte();
            var delivery = (NetworkDelivery)reader.ReadByte();
            if (delivery != NetworkDelivery.Reliable && delivery != NetworkDelivery.UnreliableSequenced) return;
            uint sequence = reader.ReadUInt();
            string name = reader.ReadString();
            if (string.IsNullOrWhiteSpace(name) || Encoding.UTF8.GetByteCount(name) > MaxNameBytes) return;
            if (kind == Kind.Variable || kind == Kind.RequestVariable)
            {
                if (delivery != NetworkDelivery.Reliable || !_variables.TryGetValue(name, out var variable)) return;
                if (kind == Kind.Variable)
                {
                    if (IsClient && sender == ServerId) variable.Decode(reader, sequence);
                }
                else if (IsServer && sender != LocalId)
                {
                    reader.RequireEnd();
                    SendPacket(sender, Build(Kind.Variable, name, Payload(variable.Encode()), NetworkDelivery.Reliable, variable.Version), NetworkDelivery.Reliable);
                }
                return;
            }
            if (!_handlers.TryGetValue((kind, name), out var handler)) return;
            if (kind == Kind.ServerRpc && (!IsServer || delivery != NetworkDelivery.Reliable)) return;
            if (kind == Kind.ClientRpc && (!IsClient || sender != ServerId || delivery != NetworkDelivery.Reliable)) return;
            var key = (sender, kind, name);
            if (delivery == NetworkDelivery.UnreliableSequenced && _sequences.TryGetValue(key, out uint last) &&
                unchecked((int)(sequence - last)) <= 0) return;
            handler(sender, reader);
            if (delivery == NetworkDelivery.UnreliableSequenced && IsConnected &&
                _handlers.TryGetValue((kind, name), out var registered) && registered == handler)
                _sequences[key] = sequence;
        }
        catch (InvalidDataException) { /* Bad/truncated remote payloads must not stop a script. */ }
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Encoding.UTF8.GetByteCount(name) > MaxNameBytes)
            throw new ArgumentException($"Network names must contain 1..{MaxNameBytes} UTF-8 bytes.", nameof(name));
    }
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        if (_disposed || _disposing) return;
        _disposing = true;
        // Cleanup must run even when the user's disconnect handler throws.
        try { Leave(); }
        finally
        {
            _disposed = true;
            try { _transport.Dispose(); }
            finally
            {
                _peers.Clear();
                _handlers.Clear();
                _variables.Clear();
                _sequences.Clear();
                Connected = null;
                Disconnected = null;
                PeerJoined = null;
                PeerLeft = null;
                NetworkTick = null;
            }
        }
    }
}
