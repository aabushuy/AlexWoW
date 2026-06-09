using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Пакеты аур (M6.11). SMSG_AURA_UPDATE (0x496): PackedGuid unit + AuraUpdate. Формат AuraUpdate
/// (3.3.5, сверено с TrinityCore <c>AuraApplication::BuildUpdatePacket</c> — он эталон против клиента):
/// <c>u8 slot</c>, затем — если слот пуст — <c>u32 spell=0</c> и всё (снятие); иначе
/// <c>u32 spell, u8 flags, u8 level, u8 stacks</c>, далее <c>PackedGuid caster ТОЛЬКО если бит
/// SELF_CAST(0x8) НЕ выставлен</c>, и <c>u32 duration, u32 time_left если DURATION(0x20)</c>.
/// ⚠️ wow_messages зовёт бит 0x8 «NOT_CASTER» с ОБРАТНОЙ семантикой — не верить, верить TC/клиенту:
/// само-каст → ставим SELF_CAST и caster НЕ пишем (иначе клиент читает несуществующий guid → фриз).
/// </summary>
public static class AuraPackets
{
    /// <summary>Применение/обновление ауры на слоте. Поддерживает только само-каст (SELF_CAST) — caster
    /// не пишем; именно так накладываются стойки/баффы игрока на себя (M6.11).</summary>
    public static byte[] BuildApply(ulong unitGuid, byte slot, uint spellId, byte flags, byte level,
        byte stacks, int durationMs)
    {
        var w = new ByteWriter(32);
        PackedGuid.Write(w, unitGuid);
        w.UInt8(slot);
        w.UInt32(spellId);
        w.UInt8(flags);
        w.UInt8(level);
        w.UInt8(stacks);
        // caster пишется ТОЛЬКО при отсутствии SELF_CAST; мы всегда само-каст → не пишем.
        if ((flags & AuraFlags.SelfCast) == 0)
            PackedGuid.Write(w, unitGuid); // фолбэк: если когда-то снимем SELF_CAST — caster обязателен
        if ((flags & AuraFlags.Duration) != 0)
        {
            w.UInt32((uint)durationMs);
            w.UInt32((uint)durationMs); // time_left = полная длительность (только что наложили)
        }
        return w.ToArray();
    }

    /// <summary>
    /// Применение ауры на ЧУЖОЙ юнит (дебафф на существе, M10.4b): caster (игрок) ≠ target → SELF_CAST НЕ
    /// ставим, пишем реальный <paramref name="casterGuid"/> (иначе клиенту нужен guid кастера, а BuildApply
    /// подставил бы guid цели).
    /// </summary>
    public static byte[] BuildApplyByCaster(ulong unitGuid, ulong casterGuid, byte slot, uint spellId,
        byte flags, byte level, byte stacks, int durationMs)
    {
        var w = new ByteWriter(32);
        PackedGuid.Write(w, unitGuid);
        w.UInt8(slot);
        w.UInt32(spellId);
        w.UInt8(flags);
        w.UInt8(level);
        w.UInt8(stacks);
        if ((flags & AuraFlags.SelfCast) == 0)
            PackedGuid.Write(w, casterGuid); // реальный кастер (для дебаффа на цели)
        if ((flags & AuraFlags.Duration) != 0)
        {
            w.UInt32((uint)durationMs);
            w.UInt32((uint)durationMs);
        }
        return w.ToArray();
    }

    /// <summary>Снятие ауры со слота: slot + spell=0 (клиент чистит слот). TrinityCore-формат.</summary>
    public static byte[] BuildRemove(ulong unitGuid, byte slot)
    {
        var w = new ByteWriter(16);
        PackedGuid.Write(w, unitGuid);
        w.UInt8(slot);
        w.UInt32(0); // spell=0 → слот пуст
        return w.ToArray();
    }

    /// <summary>
    /// SMSG_PERIODICAURALOG (0x24E, 3.3.5): тик DoT/HoT (плавающее число у клиента). Сверено с CMaNGOS
    /// <c>Unit::SendPeriodicAuraLog</c>: packed target+caster, spell, count=1, auraType, далее по типу —
    /// урон: dmg+overkill+schoolMask(u32)+absorb+resist+crit(u8); хил: dmg+over+absorb+crit(u8).
    /// </summary>
    public static byte[] BuildPeriodicLog(ulong target, ulong caster, uint spellId, bool isHeal,
        uint amount, uint schoolMask)
    {
        var w = new ByteWriter(48);
        PackedGuid.Write(w, target);
        PackedGuid.Write(w, caster);
        w.UInt32(spellId);
        w.UInt32(1);                                  // count
        w.UInt32(isHeal ? AuraTypePeriodicHeal : AuraTypePeriodicDamage);
        w.UInt32(amount);
        w.UInt32(0);                                  // overkill/overheal
        if (!isHeal)
            w.UInt32(schoolMask);                     // только для урона
        w.UInt32(0);                                  // absorb
        if (!isHeal)
            w.UInt32(0);                              // resist (только для урона)
        w.UInt8(0);                                   // critical
        return w.ToArray();
    }

    private const uint AuraTypePeriodicDamage = 3;
    private const uint AuraTypePeriodicHeal = 8;
}

/// <summary>Флаги ауры (AFLAG, 3.3.5 — значения из TrinityCore SpellAuraDefines.h, эталон против клиента). M6.11.</summary>
public static class AuraFlags
{
    public const byte Effect1 = 0x01;   // AFLAG_EFF_INDEX_0 — присутствует эффект 0
    public const byte SelfCast = 0x08;  // AFLAG_SELF_CAST — caster == цель → caster в пакет НЕ пишется
    public const byte Positive = 0x10;  // AFLAG_POSITIVE — положительная (бафф)
    public const byte Duration = 0x20;  // AFLAG_DURATION — за полями идут duration+remaining
    public const byte Negative = 0x80;  // AFLAG_NEGATIVE — отрицательная (дебафф)
}
