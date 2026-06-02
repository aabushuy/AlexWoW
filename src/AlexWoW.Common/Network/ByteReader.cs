using System.Buffers.Binary;
using System.Text;

namespace AlexWoW.Common.Network;

/// <summary>
/// Помощник для чтения бинарных пакетов в little-endian. Бросает при выходе за границы буфера.
/// </summary>
public ref struct ByteReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position = 0;

    public readonly int Position => _position;
    public readonly int Remaining => _data.Length - _position;

    public byte UInt8()
    {
        EnsureAvailable(1);
        return _data[_position++];
    }

    public ushort UInt16()
    {
        EnsureAvailable(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_position, 2));
        _position += 2;
        return value;
    }

    public uint UInt32()
    {
        EnsureAvailable(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    public ulong UInt64()
    {
        EnsureAvailable(8);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_position, 8));
        _position += 8;
        return value;
    }

    public ReadOnlySpan<byte> Bytes(int count)
    {
        EnsureAvailable(count);
        var slice = _data.Slice(_position, count);
        _position += count;
        return slice;
    }

    public void Skip(int count)
    {
        EnsureAvailable(count);
        _position += count;
    }

    /// <summary>Читает строку фиксированной длины и декодирует как ASCII.</summary>
    public string FixedString(int count)
        => Encoding.ASCII.GetString(Bytes(count));

    /// <summary>Читает строку, завершённую нулевым байтом (C-string), и потребляет сам ноль.</summary>
    public string CString()
    {
        var start = _position;
        while (_position < _data.Length && _data[_position] != 0)
            _position++;

        var value = Encoding.ASCII.GetString(_data.Slice(start, _position - start));
        if (_position < _data.Length)
            _position++; // пропускаем терминирующий ноль
        return value;
    }

    private readonly void EnsureAvailable(int count)
    {
        if (_position + count > _data.Length)
            throw new InvalidOperationException(
                $"Недостаточно данных: запрошено {count}, доступно {_data.Length - _position}.");
    }
}
