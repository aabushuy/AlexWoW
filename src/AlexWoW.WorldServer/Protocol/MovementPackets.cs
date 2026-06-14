using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Пакеты телепорта игрока на той же карте (Blink/Shadowstep, M7 #33). CMaNGOS `Unit::SendTeleportPacket`:
/// игроку — `MSG_MOVE_TELEPORT_ACK` (packed guid + u32 counter + MovementInfo), клиент применяет позицию и
/// отвечает тем же опкодом; наблюдателям — `MSG_MOVE_TELEPORT` (packed guid + MovementInfo).
/// MovementInfo (flags=0): u32 flags + u16 flags2 + u32 time + Vector3 + f32 orientation + f32 fall_time
/// (сверено с reference/wow_messages `MovementInfo` и нашим парсером MSG_MOVE_*).
/// </summary>
public static class MovementPackets
{
    private static void WriteMovementInfo(ByteWriter w, float x, float y, float z, float o)
    {
        w.UInt32(0);                            // movement flags
        w.UInt16(0);                            // movement flags2
        w.UInt32((uint)Environment.TickCount);  // timestamp
        w.Single(x).Single(y).Single(z);
        w.Single(o);
        w.Single(0f);                           // fall_time
    }

    /// <summary>MSG_MOVE_TELEPORT_ACK игроку: телепортирует его персонаж в точку. M7 #33.</summary>
    public static byte[] BuildTeleportAck(ulong guid, uint counter, float x, float y, float z, float o)
    {
        var w = new ByteWriter(48);
        PackedGuid.Write(w, guid);
        w.UInt32(counter);
        WriteMovementInfo(w, x, y, z, o);
        return w.ToArray();
    }

    /// <summary>MSG_MOVE_TELEPORT наблюдателям: показать прыжок чужого игрока. M7 #33.</summary>
    public static byte[] BuildTeleport(ulong guid, float x, float y, float z, float o)
    {
        var w = new ByteWriter(44);
        PackedGuid.Write(w, guid);
        WriteMovementInfo(w, x, y, z, o);
        return w.ToArray();
    }

    /// <summary>SMSG_TRANSFER_PENDING: анонс перехода на карту <paramref name="map"/> (без транспорта). #79.</summary>
    public static byte[] BuildTransferPending(uint map)
        => new ByteWriter(4).UInt32(map).ToArray();

    /// <summary>SMSG_NEW_WORLD: загрузить карту <paramref name="map"/> и поставить персонажа в точку. #79.</summary>
    public static byte[] BuildNewWorld(uint map, float x, float y, float z, float o)
        => new ByteWriter(20).UInt32(map).Single(x).Single(y).Single(z).Single(o).ToArray();

    /// <summary>SMSG_FORCE_MOVE_ROOT (0xE8): обездвижить юнита (Ice Block). PackedGuid + счётчик движения. IMMUNITY.1</summary>
    public static byte[] BuildForceMoveRoot(ulong guid, uint counter)
    {
        var w = new ByteWriter(16);
        PackedGuid.Write(w, guid);
        return w.UInt32(counter).ToArray();
    }

    /// <summary>SMSG_FORCE_MOVE_UNROOT (0xEA): снять обездвиживание. PackedGuid + счётчик движения. IMMUNITY.1</summary>
    public static byte[] BuildForceMoveUnroot(ulong guid, uint counter)
    {
        var w = new ByteWriter(16);
        PackedGuid.Write(w, guid);
        return w.UInt32(counter).ToArray();
    }
}
