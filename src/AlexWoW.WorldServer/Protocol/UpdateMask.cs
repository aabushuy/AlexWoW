using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Блок значений объекта (values block) update-пакета: набор «индекс поля → uint32»
/// и его сериализация как маска + значения. Поля хранятся как uint32; float — bit-cast.
/// </summary>
public sealed class UpdateMask
{
    private readonly SortedDictionary<int, uint> _fields = new();

    public void SetUInt32(int index, uint value) => _fields[index] = value;

    public void SetFloat(int index, float value)
        => _fields[index] = BitConverter.SingleToUInt32Bits(value);

    public void SetUInt64(int index, ulong value)
    {
        _fields[index] = (uint)(value & 0xFFFFFFFF);
        _fields[index + 1] = (uint)(value >> 32);
    }

    /// <summary>Упаковывает 4 байта в одно uint32-поле (little-endian: b0 — младший).</summary>
    public void SetBytes(int index, byte b0, byte b1, byte b2, byte b3)
        => _fields[index] = (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));

    public void WriteTo(ByteWriter w)
    {
        var maxIndex = _fields.Keys.Max();
        var blockCount = (maxIndex / 32) + 1;

        var mask = new uint[blockCount];
        foreach (var index in _fields.Keys)
            mask[index / 32] |= 1u << (index % 32);

        w.UInt8((byte)blockCount);
        foreach (var block in mask)
            w.UInt32(block);

        // Значения — строго по возрастанию индекса (SortedDictionary это гарантирует).
        foreach (var value in _fields.Values)
            w.UInt32(value);
    }
}
