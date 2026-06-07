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

    private const byte MoveTypeFacingTarget = 3;

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

    /// <summary>
    /// Доворот к цели без перемещения (M6.7): move_type = FACING_TARGET (3) + guid цели. Клиент
    /// разворачивает существо «лицом» к юниту — нужно при страфе игрока в мили. ВАЖНО: клиент 3.3.5
    /// всегда читает ≥1 точку пути после счётчика, поэтому шлём count=1 с ТЕКУЩЕЙ позицией (нулевой
    /// отрезок — без движения); count=0 приводил к чтению мусора → телепорт в текстуры.
    /// </summary>
    public static byte[] BuildFaceTarget(ulong guid, float cx, float cy, float cz, ulong targetGuid, uint splineId)
    {
        var w = new ByteWriter(44);
        PackedGuid.Write(w, guid);
        w.UInt8(0);
        w.Single(cx).Single(cy).Single(cz);  // текущая позиция (старт)
        w.UInt32(splineId);
        w.UInt8(MoveTypeFacingTarget);
        w.UInt64(targetGuid);                 // цель доворота
        w.UInt32(SplineFlagsNone);
        w.UInt32(0);                          // duration = 0 (мгновенно)
        w.UInt32(1);                          // одна точка пути...
        w.Single(cx).Single(cy).Single(cz);  // ...равная текущей позиции (нулевое перемещение)
        return w.ToArray();
    }
}
