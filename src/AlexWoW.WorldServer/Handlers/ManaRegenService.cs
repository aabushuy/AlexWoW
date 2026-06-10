using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Реген маны (M6.4, DI-сервис M7 S3 — бывший статик ManaRegen): вне «правила 5 секунд» прибавляет ману
/// в серверном тике и двигает полоску ресурса. Аналог регена ярости/энергии
/// (<see cref="CombatResourcesService"/>), но для мана-классов; вынесен из SpellHandlers по SRP.
/// Списание маны при касте — в <see cref="SpellCastCompletion"/> (зовёт <see cref="SendManaUpdateAsync"/>).
/// </summary>
internal sealed class ManaRegenService
{
    /// <summary>Реген маны вне «правила 5 секунд»: прибавка за тик регена. M6.4.</summary>
    private const uint ManaRegenPerSec = 20;
    /// <summary>Кадэнс регена маны (мс) — реже тика мира, чтобы не спамить апдейтами. M6.4.</summary>
    private const long ManaRegenIntervalMs = 1000;
    /// <summary>«Правило 5 секунд»: после каста реген маны паузится. M6.4.</summary>
    private const long FiveSecondRuleMs = 5000;

    /// <summary>
    /// Тик регена маны (M6.4): вне «правила 5 секунд» прибавляет ManaRegenPerSec раз в ManaRegenIntervalMs,
    /// апдейтит полоску. Зовётся из <see cref="World.WorldTick.UpdateAsync"/>.
    /// </summary>
    internal async Task TickAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.MaxMana == 0 || session.Mana >= session.MaxMana || session.InWorldGuid == 0)
            return;
        if (now - session.LastSpellCastMs < FiveSecondRuleMs)        // правило 5 секунд — реген на паузе
            return;
        if (now - session.LastManaRegenMs < ManaRegenIntervalMs)
            return;

        session.LastManaRegenMs = now;
        session.Mana = Math.Min(session.MaxMana, session.Mana + ManaRegenPerSec);
        await SendManaUpdateAsync(session, ct);
    }

    /// <summary>
    /// Шлёт текущую ману себе двумя путями: VALUES-апдейт <c>UNIT_FIELD_POWER1</c> (консистентность поля)
    /// + <c>SMSG_POWER_UPDATE</c> (0x480) — именно он надёжно двигает полоску ресурса у клиента 3.3.5a
    /// (как TrinityCore на каждом изменении power). Одного VALUES-апдейта собственному юниту не хватает. M6.4.
    /// </summary>
    internal async Task SendManaUpdateAsync(WorldSession session, CancellationToken ct)
    {
        var guid = (ulong)session.InWorldGuid;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject, PlayerSpawn.BuildPowerUpdate(guid, session.Mana), ct);
        await session.SendAsync(WorldOpcode.SmsgPowerUpdate, BuildPowerUpdatePacket(guid, session.Mana), ct);
    }

    /// <summary>SMSG_POWER_UPDATE (3.3.5): PackedGuid unit + u8 power(MANA=0) + u32 amount.</summary>
    private static byte[] BuildPowerUpdatePacket(ulong guid, uint amount)
    {
        var w = new ByteWriter(16);
        PackedGuid.Write(w, guid);
        w.UInt8(0);          // Power: MANA
        w.UInt32(amount);
        return w.ToArray();
    }
}
