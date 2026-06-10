using System.Buffers.Binary;
using System.Text;

namespace AlexWoW.Common.Network;

/// <summary>
/// Помощник для чтения бинарных пакетов в little-endian. Бросает при выходе за границы буфера.
/// </summary>
public ref struct ByteReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;

    public int Position { get; private set; } = 0;
    public readonly int Remaining => _data.Length - Position;

    public byte UInt8()
    {
        EnsureAvailable(1);
        return _data[Position++];
    }

    public ushort UInt16()
    {
        EnsureAvailable(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(Position, 2));
        Position += 2;
        return value;
    }

    public uint UInt32()
    {
        EnsureAvailable(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(Position, 4));
        Position += 4;
        return value;
    }

    public ulong UInt64()
    {
        EnsureAvailable(8);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(Position, 8));
        Position += 8;
        return value;
    }

    public float Single()
    {
        EnsureAvailable(4);
        var value = BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(Position, 4));
        Position += 4;
        return value;
    }

    /// <summary>Читает «упакованный» GUID: байт-маска + только ненулевые байты.</summary>
    public ulong PackedGuid()
    {
        var mask = UInt8();
        ulong guid = 0;
        for (var i = 0; i < 8; i++)
        {
            if ((mask & (1 << i)) != 0)
                guid |= (ulong)UInt8() << (i * 8);
        }

        return guid;
    }

    public ReadOnlySpan<byte> Bytes(int count)
    {
        EnsureAvailable(count);
        var slice = _data.Slice(Position, count);
        Position += count;
        return slice;
    }

    public void Skip(int count)
    {
        EnsureAvailable(count);
        Position += count;
    }

    /// <summary>Читает строку фиксированной длины и декодирует как ASCII.</summary>
    public string FixedString(int count)
        => Encoding.ASCII.GetString(Bytes(count));

    /// <summary>
    /// Читает строку, завершённую нулевым байтом (C-string), и потребляет сам ноль.
    /// Декодирование — UTF-8 (клиент 3.3.5a, в т.ч. ruRU, шлёт текст в UTF-8; ASCII — подмножество).
    /// Важно для кириллических имён персонажей и прочего пользовательского ввода.
    /// </summary>
    public string CString()
    {
        var start = Position;
        while (Position < _data.Length && _data[Position] != 0)
            Position++;

        var value = Encoding.UTF8.GetString(_data[start..Position]);
        if (Position < _data.Length)
            Position++; // пропускаем терминирующий ноль
        return value;
    }

    private readonly void EnsureAvailable(int count)
    {
        if (Position + count > _data.Length)
        {
            throw new InvalidOperationException(
                $"Недостаточно данных: запрошено {count}, доступно {_data.Length - Position}.");
        }
    }
}
