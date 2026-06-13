using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Билдеры пакетов спеллов (M6.4): SMSG_SPELL_START/GO, CAST_FAILED, SPELL_FAILURE, SPELL_COOLDOWN,
/// SPELLNONMELEEDAMAGELOG, SPELLHEALLOG. Фокусные сборщики байтов (как <see cref="AuraPackets"/>) —
/// отделены от оркестрации каста (<see cref="Handlers.SpellCastService"/>). Layout сверен с reference
/// world/common.wowm + CMaNGOS/TrinityCore (эталон против клиента 3.3.5a).
/// </summary>
public static class SpellPackets
{
    // --- CastFlags: START без HAS_TRAJECTORY (0x2 держал бы каст «в полёте» у снарядов и не завершал);
    //     GO = 0x100 (UNKNOWN9, как CMaNGOS). Ни один не требует conditional-полей в 3.3.5. ---
    private const uint StartFlags = 0x0;
    private const uint GoFlags = 0x100;

    /// <summary>SPELL_CAST_TARGET_FLAG_UNIT — единственный target-флаг, который мы читаем/пишем.</summary>
    public const uint TargetFlagUnit = 0x2;

    /// <summary>SMSG_SPELL_START (3.3.5): каст-бар. flags=0, без conditional-полей.</summary>
    public static byte[] BuildSpellStart(ulong caster, uint spellId, byte castCount, uint timerMs, ulong targetGuid)
    {
        var w = new ByteWriter(48);
        PackedGuid.Write(w, caster);   // cast_item = caster (без предмета-кастера)
        PackedGuid.Write(w, caster);   // caster
        w.UInt8(castCount);
        w.UInt32(spellId);
        w.UInt32(StartFlags);
        w.UInt32(timerMs);
        WriteTargets(w, targetGuid);
        return w.ToArray();
    }

    /// <summary>
    /// SMSG_SPELL_GO (3.3.5): спелл «пошёл». flags=0x100. Байт после caster (wow_messages зовёт его
    /// «extra_casts») = <paramref name="castCount"/> — это <c>m_cast_count</c> из CMaNGOS: клиент по нему
    /// сопоставляет GO со СВОИМ pending-кастом и закрывает его. Если слать 0, клиент проигрывает визуал
    /// (снаряд/урон), но НЕ завершает каст → поза/кнопка кастера залипают (#26). Должен совпадать с
    /// cast_count из CMSG_CAST_SPELL (и из нашего SMSG_SPELL_START).
    /// </summary>
    public static byte[] BuildSpellGo(ulong caster, uint spellId, ulong targetGuid, byte castCount)
    {
        var w = new ByteWriter(48);
        PackedGuid.Write(w, caster);   // cast_item
        PackedGuid.Write(w, caster);   // caster
        w.UInt8(castCount);            // m_cast_count (сопоставление с pending-кастом клиента)
        w.UInt32(spellId);
        w.UInt32(GoFlags);
        w.UInt32((uint)Environment.TickCount); // timestamp
        if (targetGuid != 0)
        {
            w.UInt8(1);                // amount_of_hits
            w.UInt64(targetGuid);      // hits[0] (plain Guid)
        }
        else
        {
            w.UInt8(0);                // нет хитов
        }
        w.UInt8(0);                    // amount_of_misses
        WriteTargets(w, targetGuid);
        return w.ToArray();
    }

    /// <summary>SMSG_SPELL_FAILURE (3.3.5): u64 caster + u8 extra_casts + u32 spell + u8 result.</summary>
    public static byte[] BuildSpellFailure(ulong caster, uint spellId, byte result)
        => new ByteWriter(16)
            .UInt64(caster)
            .UInt8(0)
            .UInt32(spellId)
            .UInt8(result)
            .ToArray();

    /// <summary>
    /// SMSG_SPELL_FAILED_OTHER (3.3.5): прерывание каста ДРУГОГО юнита — у наблюдателей гасит каст-бар цели
    /// (клиентское событие UNIT_SPELLCAST_INTERRUPTED). В отличие от SMSG_SPELL_FAILURE (свой каст игрока),
    /// именно этот пакет останавливает каст-бар существа. Layout — CMaNGOS Spell::SendInterrupted:
    /// PackedGuid caster + u8 cast_count + u32 spell + u8 result. INT.1.
    /// </summary>
    public static byte[] BuildSpellFailedOther(ulong caster, uint spellId, byte result)
    {
        var w = new ByteWriter(16);
        PackedGuid.Write(w, caster);
        w.UInt8(0);            // cast_count
        w.UInt32(spellId);
        w.UInt8(result);
        return w.ToArray();
    }

    /// <summary>
    /// SMSG_CAST_FAILED (3.3.5): отказ каста. u8 cast_count + u32 spell + u8 result + u8 multiple_casts.
    /// Для NOT_READY/NO_POWER conditional-полей нет.
    /// </summary>
    public static byte[] BuildCastFailed(byte castCount, uint spellId, byte result)
        => new ByteWriter(8)
            .UInt8(castCount)
            .UInt32(spellId)
            .UInt8(result)
            .UInt8(0) // multiple_casts = false
            .ToArray();

    /// <summary>
    /// SMSG_SPELL_COOLDOWN (3.3.5): u64 guid + u8 flags + [{u32 spell, u32 cooldown_ms}] (массив до конца).
    /// Запускает кулдаун-полоску на кнопке у клиента.
    /// </summary>
    public static byte[] BuildSpellCooldown(ulong caster, uint spellId, uint cooldownMs)
        => new ByteWriter(17)
            .UInt64(caster)
            .UInt8(0)          // flags
            .UInt32(spellId)
            .UInt32(cooldownMs)
            .ToArray();

    /// <summary>
    /// SMSG_COOLDOWN_EVENT (3.3.5): u32 spell + u64 guid. Активирует ОТЛОЖЕННЫЙ кулдаун у клиента —
    /// для спеллов SPELL_ATTR_COOLDOWN_ON_EVENT (Shadowform/Stealth) кулдаун стартует не на касте, а при
    /// СНЯТИИ ауры. Без этого пакета клиент держит кнопку «активной»/недоступной до релога. Сверено с
    /// wow_messages (smsg_cooldown_event) и CMaNGOS <c>Player::AddCooldown</c> (на fade COOLDOWN_ON_EVENT-ауры).
    /// </summary>
    public static byte[] BuildCooldownEvent(ulong caster, uint spellId)
        => new ByteWriter(12)
            .UInt32(spellId)
            .UInt64(caster)
            .ToArray();

    /// <summary>
    /// SMSG_SET_FLAT/PCT_SPELL_MODIFIER (3.3.5): u8 eff (бит 0–95 classmask) + u8 op (SPELLMOD_*) +
    /// i32 итог по биту. Клиент применяет к тултипам/стоимости/гейту кнопок (без этого пакета тултип
    /// Удара героя показывает базовые 15 ярости и кнопка не жмётся при 14). Сверено с wow_messages
    /// (smsg_set_flat_spell_modifier) и CMaNGOS <c>Player::AddSpellMod</c>. M10.6.
    /// </summary>
    public static byte[] BuildSpellModifier(byte eff, byte op, int value)
        => new ByteWriter(6).UInt8(eff).UInt8(op).Int32(value).ToArray();

    /// <summary>SMSG_SPELLNONMELEEDAMAGELOG (3.3.5): «числа урона» от спелла.</summary>
    public static byte[] BuildDamageLog(ulong target, ulong attacker, uint spellId, uint damage, uint overkill, byte school)
    {
        var w = new ByteWriter(48);
        PackedGuid.Write(w, target);
        PackedGuid.Write(w, attacker);
        w.UInt32(spellId);
        w.UInt32(damage);
        w.UInt32(overkill);
        w.UInt8(school);
        w.UInt32(0)   // absorbed
         .UInt32(0)   // resisted
         .UInt8(0)    // periodic_log
         .UInt8(0)    // unused
         .UInt32(0)   // blocked
         .UInt32(0)   // hit_info
         .UInt8(0);   // extend_flag
        return w.ToArray();
    }

    /// <summary>SMSG_SPELLHEALLOG (3.3.5): packed victim + packed caster + spell + amount + overheal +
    /// absorb(0) + crit(u8) + unused(u8). Величина — эффективный хил (овёрхил отдельным полем).</summary>
    public static byte[] BuildHealLog(ulong victim, ulong caster, uint spellId, uint amount, uint overheal)
    {
        var w = new ByteWriter(32);
        PackedGuid.Write(w, victim);
        PackedGuid.Write(w, caster);
        w.UInt32(spellId)
         .UInt32(amount)
         .UInt32(overheal)
         .UInt32(0)   // absorb
         .UInt8(0)    // critical
         .UInt8(0);   // unused
        return w.ToArray();
    }

    /// <summary>SMSG_SPELLENERGIZELOG (3.3.5): packed victim + packed caster + spell + powerType + amount.
    /// Плавающее «+ресурс» (мана=0). Сверено с CMaNGOS <c>Unit::SendEnergizeSpellLog</c>.</summary>
    public static byte[] BuildEnergizeLog(ulong victim, ulong caster, uint spellId, uint powerType, uint amount)
    {
        var w = new ByteWriter(28);
        PackedGuid.Write(w, victim);
        PackedGuid.Write(w, caster);
        w.UInt32(spellId)
         .UInt32(powerType)
         .UInt32(amount);
        return w.ToArray();
    }

    /// <summary>SpellCastTargets: только SELF (нет цели) или UNIT (packed guid цели).</summary>
    private static void WriteTargets(ByteWriter w, ulong targetGuid)
    {
        if (targetGuid != 0)
        {
            w.UInt32(TargetFlagUnit);
            PackedGuid.Write(w, targetGuid);
        }
        else
        {
            w.UInt32(0); // SELF
        }
    }
}
