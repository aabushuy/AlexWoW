using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// «Упакованный» GUID: байт-маска (бит i = байт i ненулевой) + только ненулевые байты.
/// Используется в update-пакетах WoW.
/// </summary>
public static class PackedGuid
{
    public static void Write(ByteWriter w, ulong guid)
    {
        byte mask = 0;
        Span<byte> bytes = stackalloc byte[8];
        var count = 0;
        for (var i = 0; i < 8; i++)
        {
            var b = (byte)(guid >> (i * 8));
            if (b != 0)
            {
                mask |= (byte)(1 << i);
                bytes[count++] = b;
            }
        }

        w.UInt8(mask);
        if (count > 0)
            w.Bytes(bytes[..count]);
    }
}
