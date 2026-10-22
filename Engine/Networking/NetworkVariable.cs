using Microsoft.Xna.Framework;

namespace Engine.Networking;

/// <summary>Explicit serializer for a value. Custom readers must validate their input before returning.</summary>
public sealed class NetworkCodec<T>
{
    public Action<NetworkWriter, T> Write { get; }
    public Func<NetworkReader, T> Read { get; }
    public NetworkCodec(Action<NetworkWriter, T> write, Func<NetworkReader, T> read)
    {
        Write = write ?? throw new ArgumentNullException(nameof(write));
        Read = read ?? throw new ArgumentNullException(nameof(read));
    }
}

public abstract class NetworkVariable
{
    internal string Name;
    internal NetworkSession Session;
    internal uint Version;
    internal bool HasSnapshot, Dirty = true;
    internal abstract byte[] Encode();
    internal abstract void Decode(NetworkReader reader, uint version);
    public bool CanWrite => Session.IsServer;
    /// <summary>False on clients until the host supplies the initial value, and after disconnect.</summary>
    public bool IsSynchronized => CanWrite || (Session.IsConnected && HasSnapshot);
}

/// <summary>Named, host-written persistent state. Clients receive initial state and subsequent changes reliably.</summary>
public sealed class NetworkVariable<T> : NetworkVariable
{
    private readonly NetworkCodec<T> _codec;
    private T _value;
    public event Action<T, T> Changed;
    internal NetworkVariable(NetworkSession session, string name, T initial, NetworkCodec<T> codec)
    {
        Session = session;
        Name = name;
        _value = initial;
        _codec = codec ?? DefaultCodec();
        // Fail at registration, rather than in the frame loop, for an unsupported or oversized value.
        session.ValidateVariablePayload(name, Encode());
    }
    public T Value
    {
        get => _value;
        set
        {
            Session.ThrowIfDisposed();
            if (!CanWrite) throw new InvalidOperationException("Only the connected host can write a network variable.");
            if (EqualityComparer<T>.Default.Equals(_value, value)) return;
            var writer = new NetworkWriter();
            _codec.Write(writer, value);
            Session.ValidateVariablePayload(Name, writer.ToArray());
            T previous = _value;
            _value = value;
            Version++;
            Dirty = true;
            Changed?.Invoke(previous, value);
        }
    }
    internal override byte[] Encode()
    {
        var writer = new NetworkWriter();
        _codec.Write(writer, _value);
        return writer.ToArray();
    }
    internal override void Decode(NetworkReader reader, uint version)
    {
        if (HasSnapshot && unchecked((int)(version - Version)) <= 0) return;
        T value = _codec.Read(reader);
        reader.RequireEnd();
        T previous = _value;
        _value = value;
        Version = version;
        HasSnapshot = true;
        if (!EqualityComparer<T>.Default.Equals(previous, value)) Changed?.Invoke(previous, value);
    }
    private static NetworkCodec<T> DefaultCodec()
    {
        object codec = typeof(T) == typeof(bool) ? new NetworkCodec<bool>((w, v) => w.WriteBool(v), r => r.ReadBool()) :
            typeof(T) == typeof(int) ? new NetworkCodec<int>((w, v) => w.WriteInt(v), r => r.ReadInt()) :
            typeof(T) == typeof(uint) ? new NetworkCodec<uint>((w, v) => w.WriteUInt(v), r => r.ReadUInt()) :
            typeof(T) == typeof(ulong) ? new NetworkCodec<ulong>((w, v) => w.WriteULong(v), r => r.ReadULong()) :
            typeof(T) == typeof(float) ? new NetworkCodec<float>((w, v) => w.WriteFloat(v), r => r.ReadFloat()) :
            typeof(T) == typeof(double) ? new NetworkCodec<double>((w, v) => w.WriteDouble(v), r => r.ReadDouble()) :
            typeof(T) == typeof(string) ? new NetworkCodec<string>((w, v) => w.WriteString(v), r => r.ReadString()) :
            typeof(T) == typeof(Vector2) ? new NetworkCodec<Vector2>((w, v) => w.WriteVector2(v), r => r.ReadVector2()) :
            typeof(T) == typeof(Vector3) ? new NetworkCodec<Vector3>((w, v) => w.WriteVector3(v), r => r.ReadVector3()) :
            typeof(T) == typeof(Quaternion) ? new NetworkCodec<Quaternion>((w, v) => w.WriteQuaternion(v), r => r.ReadQuaternion()) :
            throw new NotSupportedException($"Supply a NetworkCodec<{typeof(T).Name}> for this network variable.");
        return (NetworkCodec<T>)codec;
    }
}
