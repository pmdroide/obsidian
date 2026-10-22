using System.Buffers.Binary;
using System.Text;
using Microsoft.Xna.Framework;

namespace Engine.Networking;

/// <summary>A bounded view of one payload. Do not retain a reader passed to a message/RPC handler.</summary>
public sealed class NetworkReader
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] _buffer;
    private readonly int _end;
    private int _position;
    public int Remaining => _end - _position;
    public NetworkReader(byte[] buffer) : this(buffer, 0, buffer?.Length ?? 0) { }
    public NetworkReader(byte[] buffer, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || length < 0 || offset > buffer.Length - length) throw new ArgumentOutOfRangeException(nameof(length));
        _buffer = buffer;
        _position = offset;
        _end = offset + length;
    }
    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Remaining) throw new InvalidDataException("Truncated network payload.");
        var result = _buffer.AsSpan(_position, count);
        _position += count;
        return result;
    }
    public byte ReadByte() => Take(1)[0];
    public bool ReadBool() => ReadByte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid network boolean.") };
    public int ReadInt() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint ReadUInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public ulong ReadULong() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public float ReadFloat()
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        return float.IsFinite(value) ? value : throw new InvalidDataException("Non-finite network number.");
    }
    public double ReadDouble()
    {
        double value = BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
        return double.IsFinite(value) ? value : throw new InvalidDataException("Non-finite network number.");
    }
    public string ReadString()
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        try { return Utf8.GetString(Take(count)); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid network UTF-8.", ex); }
    }
    public Vector2 ReadVector2() => new(ReadFloat(), ReadFloat());
    public Vector3 ReadVector3() => new(ReadFloat(), ReadFloat(), ReadFloat());
    public Quaternion ReadQuaternion() => new(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
    public byte[] ReadBytes(int count) => Take(count).ToArray();
    public void RequireEnd()
    {
        if (Remaining != 0) throw new InvalidDataException("Unexpected trailing network data.");
    }
}
