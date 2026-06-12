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

    /// <summary>SMSG_ATTACKERSTATEUPDATE (3.3.5a) — одна запись урона, без absorb/resist/block.
    /// <paramref name="victimState"/>: 1=удар, 2=уклонение, 3=парирование (клиент рисует плавающий текст).</summary>
    public static byte[] BuildAttackerStateUpdate(ulong attacker, ulong target, uint damage, uint overkill,
        byte victimState = VictimStateHit)
    {
        var w = new ByteWriter(48);
        w.UInt32(HitInfoAffectsVictim);
        PackedGuid.Write(w, attacker);
        PackedGuid.Write(w, target);
        w.UInt32(damage);     // total_damage
        w.UInt32(overkill);   // overkill
        w.UInt8(1);           // amount_of_damages
        // DamageInfo[0]
        w.UInt32(SchoolMaskPhysical)
         .Single(damage)      // damage_float
         .UInt32(damage);     // damage_uint
        w.UInt8(victimState);
        w.UInt32(0);          // unknown1
        w.UInt32(0);          // unknown2
        return w.ToArray();
    }
}
