using System.Buffers.Binary;
using System.Text;
using Microsoft.Xna.Framework;

namespace Engine.Networking;

/// <summary>Bounded little-endian packet builder. Reuse with Clear; strings have a UTF-8 byte length.</summary>
public sealed class NetworkWriter
{
    internal byte[] Buffer { get; }
    public int Length { get; private set; }
    public NetworkWriter(int capacity = NetworkSession.MaxPacketSize)
    {
        if (capacity < 1 || capacity > NetworkSession.MaxPacketSize) throw new ArgumentOutOfRangeException(nameof(capacity));
        Buffer = new byte[capacity];
    }
    public void Clear() => Length = 0;
    public byte[] ToArray() => Buffer.AsSpan(0, Length).ToArray();
    private Span<byte> Reserve(int count)
    {
        if (count < 0 || count > Buffer.Length - Length) throw new InvalidOperationException("Network packet exceeds its capacity.");
        var result = Buffer.AsSpan(Length, count);
        Length += count;
        return result;
    }
    public void WriteByte(byte value) => Reserve(1)[0] = value;
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);
    public void WriteInt(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
    public void WriteUInt(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
    public void WriteULong(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
    public void WriteFloat(float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        BinaryPrimitives.WriteSingleLittleEndian(Reserve(4), value);
    }
    public void WriteDouble(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        BinaryPrimitives.WriteDoubleLittleEndian(Reserve(8), value);
    }
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int count = Encoding.UTF8.GetByteCount(value);
        if (count > ushort.MaxValue || count + 2 > Buffer.Length - Length)
            throw new InvalidOperationException("Network string exceeds its capacity.");
        BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), (ushort)count);
        Encoding.UTF8.GetBytes(value, Reserve(count));
    }
    public void WriteVector2(Vector2 value) { WriteFloat(value.X); WriteFloat(value.Y); }
    public void WriteVector3(Vector3 value) { WriteFloat(value.X); WriteFloat(value.Y); WriteFloat(value.Z); }
    public void WriteQuaternion(Quaternion value) { WriteFloat(value.X); WriteFloat(value.Y); WriteFloat(value.Z); WriteFloat(value.W); }
    public void WriteBytes(ReadOnlySpan<byte> value) => value.CopyTo(Reserve(value.Length));
}
