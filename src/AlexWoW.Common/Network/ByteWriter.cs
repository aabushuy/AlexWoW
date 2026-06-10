using System.Buffers.Binary;
using System.Text;

namespace AlexWoW.Common.Network;

/// <summary>
/// Помощник для сборки бинарных пакетов в little-endian (порядок байт протокола WoW).
/// </summary>
public sealed class ByteWriter(int capacity = 64)
{
    private readonly List<byte> _buffer = new(capacity);

    public int Length => _buffer.Count;

    public ByteWriter UInt8(byte value)
    {
        _buffer.Add(value);
        return this;
    }

    public ByteWriter UInt16(ushort value)
    {
        Span<byte> tmp = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
        _buffer.AddRange(tmp);
        return this;
    }

    public ByteWriter UInt32(uint value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
        _buffer.AddRange(tmp);
        return this;
    }

    public ByteWriter UInt64(ulong value)
    {
        Span<byte> tmp = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tmp, value);
        _buffer.AddRange(tmp);
        return this;
    }

    public ByteWriter Int32(int value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(tmp, value);
        _buffer.AddRange(tmp);
        return this;
    }

    public ByteWriter Single(float value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(tmp, value);
        _buffer.AddRange(tmp);
        return this;
    }

    public ByteWriter Bytes(ReadOnlySpan<byte> value)
    {
        _buffer.AddRange(value);
        return this;
    }

    /// <summary>
    /// Записывает строку в UTF-8 с завершающим нулевым байтом.
    /// Клиент 3.3.5a (в т.ч. ruRU) ожидает UTF-8; ASCII — его подмножество.
    /// </summary>
    public ByteWriter CString(string value)
    {
        _buffer.AddRange(Encoding.UTF8.GetBytes(value));
        _buffer.Add(0);
        return this;
    }

    public byte[] ToArray() => [.. _buffer];
}
