using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// SMSG_MONSTER_MOVE (3.3.5a) — сплайн движения существа (M6.7 инкр.2: преследование/возврат).
/// Простейшая форма: прямой отрезок из текущей точки в одну целевую (NORMAL, без флагов/параболы).
/// Layout по CMaNGOS-WotLK (в wow_messages типы splines/SplineFlag не раскрыты полностью):
/// packed guid + u8 0 + start(3f) + u32 splineId + u8 moveType + u32 splineFlags + u32 duration +
/// u32 pointCount(=1) + dest(3f).
/// </summary>
public static class MonsterMove
{
    private const byte MoveTypeNormal = 0;
    private const uint SplineFlagsNone = 0;

    public static byte[] Build(ulong guid, float sx, float sy, float sz,
        float dx, float dy, float dz, uint durationMs, uint splineId)
    {
        var w = new ByteWriter(48);
        PackedGuid.Write(w, guid);
        w.UInt8(0);                          // unknown (cmangos-wotlk шлёт 0)
        w.Single(sx).Single(sy).Single(sz);  // стартовая точка (текущая позиция)
        w.UInt32(splineId);
        w.UInt8(MoveTypeNormal);
        w.UInt32(SplineFlagsNone);
        w.UInt32(durationMs);
        w.UInt32(1);                         // число точек пути
        w.Single(dx).Single(dy).Single(dz);  // единственная цель
        return w.ToArray();
    }
}
