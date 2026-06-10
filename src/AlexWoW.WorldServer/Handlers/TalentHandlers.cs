using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Таланты (M9.6+): отправка состояния (<c>SMSG_TALENTS_INFO</c>), изучение и сброс. В каркасе (M9.6) —
/// только отправка очков/изученных при входе; деревья клиент рисует сам из своей DBC. Изучение
/// (<c>CMSG_LEARN_TALENT</c>) и сброс — M9.7/M9.8.
/// </summary>
public static class TalentHandlers
{
    /// <summary>Шлёт текущее состояние талантов игрока (свободные очки + изученные таланты). M9.6.</summary>
    public static Task SendTalentsInfoAsync(WorldSession session, CancellationToken ct)
    {
        var talents = session.LearnedTalents.Select(kv => (kv.Key, kv.Value)).ToList();
        return session.SendAsync(WorldOpcode.SmsgTalentsInfo,
            TalentPackets.BuildTalentsInfo(session.TalentPoints, talents), ct);
    }

    /// <summary>Пересчитывает свободные очки талантов: MaxPoints(класс,уровень) − потрачено (Σ rank+1). M9.6.</summary>
    public static void RecomputePoints(WorldSession session, byte classId, byte level)
    {
        var max = World.TalentMath.MaxPoints(classId, level);
        uint spent = 0;
        foreach (var rank in session.LearnedTalents.Values)
            spent += (uint)(rank + 1);
        session.TalentPoints = max > spent ? max - spent : 0u;
    }
}
