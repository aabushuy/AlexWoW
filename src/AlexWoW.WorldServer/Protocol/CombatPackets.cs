using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Билдеры боевых пакетов (M6.3/M6.7, вынос из CombatHandlers — M7 S4): SMSG_ATTACKSTART/ATTACKSTOP,
/// SMSG_AI_REACTION, SMSG_ATTACKERSTATEUPDATE. Чистые функции «аргументы → байты» (code-style §4),
/// layout сверен с эталонами 3.3.5a.
/// </summary>
public static class CombatPackets
{
    /// <summary>HitInfo для обычного удара: AFFECTS_VICTIM (0x2) — без absorb/resist/block, простой пакет.</summary>
    private const uint HitInfoAffectsVictim = 0x2;
    /// <summary>HITINFO_BLOCK (0x2000): удар частично заблокирован → в конце пакета пишется blocked_amount.</summary>
    private const uint HitInfoBlock = 0x2000;
    /// <summary>HITINFO_FULL_ABSORB (0x20): весь урон поглощён щитом; HITINFO_PARTIAL_ABSORB (0x40): часть. ABS.1.</summary>
    private const uint HitInfoFullAbsorb = 0x20;
    private const uint HitInfoPartialAbsorb = 0x40;
    /// <summary>HITINFO_CRITICALHIT (0x200): крит. удар — клиент рисует крупное «крит» число. CRIT.2.</summary>
    private const uint HitInfoCriticalHit = 0x200;

    private const byte VictimStateHit = 1;
    private const uint SchoolMaskPhysical = 1;

    /// <summary>SMSG_ATTACKSTART (3.3.5): plain u64 attacker + u64 victim.</summary>
    public static byte[] BuildAttackStart(ulong attacker, ulong victim)
        => new ByteWriter(16).UInt64(attacker).UInt64(victim).ToArray();

    /// <summary>SMSG_ATTACKSTOP (3.3.5): packed attacker + packed victim + u32 0.</summary>
    public static byte[] BuildAttackStop(ulong attacker, ulong victim)
    {
        var w = new ByteWriter(16);
        PackedGuid.Write(w, attacker);
        PackedGuid.Write(w, victim);
        w.UInt32(0);
        return w.ToArray();
    }

    /// <summary>SMSG_AI_REACTION (3.3.5): plain u64 guid + u32 reaction.</summary>
    public static byte[] BuildAiReaction(ulong guid, uint reaction)
        => new ByteWriter(12).UInt64(guid).UInt32(reaction).ToArray();

    /// <summary>
    /// SMSG_UPDATE_COMBO_POINTS (3.3.5): PackedGuid цели + u8 очков серии. Очки серверо-авторитетны,
    /// привязаны к комбо-цели (генераторы рога/друида-кошки копят, финишеры расходуют). Сверено с
    /// wow_messages (smsg_update_combo_points) и CMaNGOS <c>Player::SendComboPoints</c>.
    /// </summary>
    public static byte[] BuildUpdateComboPoints(ulong targetGuid, byte comboPoints)
    {
        var w = new ByteWriter(10);
        PackedGuid.Write(w, targetGuid);
        w.UInt8(comboPoints);
        return w.ToArray();
    }

    /// <summary>
    /// SMSG_RESYNC_RUNES (3.3.5): <c>u32 count</c> + по руне <c>u8 currentType + u8 passedCooldown</c>, где
    /// passedCooldown = <c>255 − КД×255/maxКД</c> (доля пройденного КД 0..255; 255 = руна готова). Полный
    /// снимок состояния рун DK клиенту. Сверено с CMaNGOS <c>Player::ResyncRunes</c>. RUNE.1.
    /// </summary>
    public static byte[] BuildResyncRunes(IReadOnlyList<(byte CurrentType, int CooldownMs)> runes, int maxCooldownMs)
    {
        var w = new ByteWriter(4 + runes.Count * 2);
        w.UInt32((uint)runes.Count);
        foreach (var (type, cd) in runes)
        {
            var clamped = Math.Clamp(cd, 0, maxCooldownMs);
            var passed = maxCooldownMs > 0 ? (byte)(255 - clamped * 255 / maxCooldownMs) : (byte)255;
            w.UInt8(type).UInt8(passed);
        }
        return w.ToArray();
    }

    /// <summary>SMSG_CONVERT_RUNE (3.3.5): <c>u8 index + u8 newType</c> — слот руны сменил тип (в death и
    /// обратно). Клиент перекрашивает руну. Сверено с CMaNGOS <c>Player::ConvertRune</c>. RUNE.5.</summary>
    public static byte[] BuildConvertRune(byte index, byte newType)
        => new ByteWriter(2).UInt8(index).UInt8(newType).ToArray();

    /// <summary>SMSG_ATTACKERSTATEUPDATE (3.3.5a) — одна запись урона. <paramref name="victimState"/>:
    /// 1=удар, 2=уклонение, 3=парирование. <paramref name="blockedAmount"/>&gt;0 — выставляет HITINFO_BLOCK
    /// и пишет сумму блока в конце (клиент рисует «Блокировка (N)»). Layout сверен с эталоном 3.3.5a.</summary>
    public static byte[] BuildAttackerStateUpdate(ulong attacker, ulong target, uint damage, uint overkill,
        byte victimState = VictimStateHit, uint blockedAmount = 0, uint absorbedAmount = 0, bool crit = false)
    {
        var hitInfo = HitInfoAffectsVictim;
        if (crit) // CRIT.2: крит. удар — клиент рисует крупное число
            hitInfo |= HitInfoCriticalHit;
        if (blockedAmount > 0)
            hitInfo |= HitInfoBlock;
        // ABS.1: весь урон поглощён (damage==0) → FULL_ABSORB; часть → PARTIAL_ABSORB. Поле absorb пишется
        // после массива DamageInfo (3.3.5 layout: absorb/resist между damage_infos и victim_state).
        if (absorbedAmount > 0)
            hitInfo |= damage == 0 ? HitInfoFullAbsorb : HitInfoPartialAbsorb;

        var w = new ByteWriter(56);
        w.UInt32(hitInfo);
        PackedGuid.Write(w, attacker);
        PackedGuid.Write(w, target);
        w.UInt32(damage);     // total_damage
        w.UInt32(overkill);   // overkill
        w.UInt8(1);           // amount_of_damages
        // DamageInfo[0]
        w.UInt32(SchoolMaskPhysical)
         .Single(damage)      // damage_float
         .UInt32(damage);     // damage_uint
        if (absorbedAmount > 0)
            w.UInt32(absorbedAmount);  // absorb (только при ALL_ABSORB; до victim_state)
        w.UInt8(victimState);
        w.UInt32(0);          // unknown1
        w.UInt32(0);          // spell id
        if (blockedAmount > 0)
            w.UInt32(blockedAmount);   // blocked_amount (только при HITINFO_BLOCK)
        return w.ToArray();
    }
}
