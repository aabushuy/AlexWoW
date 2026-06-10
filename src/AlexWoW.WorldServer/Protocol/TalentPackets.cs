using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Сборка пакетов талантов (M9.6+). <c>SMSG_TALENTS_INFO</c> (0x4C0) шлётся при входе и после изменения —
/// клиент по нему инициализирует панель талантов (деревья он рисует сам из своей DBC). Формат сверен с
/// wow_messages (3.3.5): массивы length-driven, поэтому пустые таланты/глифы = счётчик 0. rank — 0-индексный.
/// </summary>
public static class TalentPackets
{
    /// <summary>
    /// SMSG_TALENTS_INFO для игрока: <c>u8 type(0=PLAYER); u32 pointsLeft; u8 specCount(1); u8 activeSpec(0);</c>
    /// затем по спеку <c>u8 talentCount; {u32 talentId; u8 rank}[]; u8 glyphCount; u16[] glyphs</c>.
    /// Один спек, без глифов.
    /// </summary>
    public static byte[] BuildTalentsInfo(uint pointsLeft, IReadOnlyCollection<(uint Id, byte Rank)> talents)
    {
        var w = new ByteWriter(8 + talents.Count * 5 + 4);
        w.UInt8(0);                       // talent_type = PLAYER
        w.UInt32(pointsLeft);
        w.UInt8(1);                       // amount_of_specs (один спек; дуал-спек — позже)
        w.UInt8(0);                       // active_spec
        // spec 0:
        w.UInt8((byte)talents.Count);
        foreach (var (id, rank) in talents)
            w.UInt32(id).UInt8(rank);
        w.UInt8(0);                       // amount_of_glyphs
        return w.ToArray();
    }
}
