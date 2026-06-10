using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Боевые ресурсы (M6.12, DI-сервис M7 S3 — бывший статик CombatResources): генерация ярости (воин/друид)
/// от нанесённого/полученного мили-урона и её распад вне боя; постоянный реген энергии (разбойник).
/// Мана — отдельно (M6.4, <see cref="ManaRegenService"/>). Поле ресурса = <c>UNIT_FIELD_POWER1 + powerType</c>;
/// полоску двигает VALUES-апдейт + SMSG_POWER_UPDATE (как мана). Расход ресурса абилками —
/// <see cref="SpellCastCompletion"/> (зовёт <see cref="SpendPowerAsync"/>).
/// </summary>
internal sealed class CombatResourcesService
{
    private const byte PowerRage = 1;     // powertype ярости
    private const byte PowerEnergy = 3;   // powertype энергии
    private const uint MaxRage = 1000;    // ×10 → 100 у клиента
    private const uint MaxEnergy = 100;

    /// <summary>Кадэнс тика ресурса (мс): реген энергии / распад ярости.</summary>
    private const long ResourceTickMs = 1000;
    /// <summary>Реген энергии за тик (1 с).</summary>
    private const uint EnergyPerTick = 10;
    /// <summary>Распад ярости за тик вне боя (×10 → 2 ярости/с).</summary>
    private const uint RageDecayPerTick = 20;
    /// <summary>Задержка после боя до начала распада ярости (мс) — как внебоевой реген HP.</summary>
    private const long OutOfCombatDelayMs = 5000;

    /// <summary>
    /// Начисляет ярость от мили-урона (формула CMaNGOS <c>Player::RewardRage</c>): атакующему — больше
    /// (+ фактор скорости оружия), получившему урон — меньше. Только для ярость-классов. M6.12.
    /// </summary>
    internal async Task GainRageAsync(WorldSession session, uint damage, bool attacker, CancellationToken ct)
    {
        if (session.Character is not { } c || DisplayData.PowerTypeForClass(c.Class) != PowerRage)
            return;
        if (damage == 0 || session.Rage >= MaxRage)
            return;

        float level = Math.Max((byte)1, c.Level);
        var conversion = 0.0091107836f * level * level + 3.225598133f * level + 4.2652911f;
        float add;
        if (attacker)
        {
            var speedSec = session.MainHandSpeedMs / 1000f;
            add = (damage / conversion * 7.5f + speedSec * 3.5f) / 2f; // mainhand hit factor
        }
        else
        {
            add = damage / conversion * 2.5f;
        }

        var units = (uint)MathF.Max(1f, add * 10f); // ярость хранится ×10
        session.Rage = Math.Min(MaxRage, session.Rage + units);
        await SendPowerAsync(session, PowerRage, session.Rage, ct);
    }

    /// <summary>
    /// Тик ресурса (M6.12, из <see cref="World.WorldTick.UpdateAsync"/>): энергии — постоянный реген до
    /// максимума; ярости — распад вне боя (после паузы). Кадэнс 1 с. Мана — в <see cref="ManaRegenService"/>.
    /// </summary>
    internal async Task TickAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || session.Character is not { } c)
            return;
        var powerType = DisplayData.PowerTypeForClass(c.Class);
        if (powerType != PowerRage && powerType != PowerEnergy)
            return; // мана-класс — реген маны отдельно
        if (now - session.LastResourceTickMs < ResourceTickMs)
            return;
        session.LastResourceTickMs = now;

        if (powerType == PowerEnergy)
        {
            if (session.Energy >= MaxEnergy)
                return;
            session.Energy = Math.Min(MaxEnergy, session.Energy + EnergyPerTick);
            await SendPowerAsync(session, PowerEnergy, session.Energy, ct);
            return;
        }

        // Ярость: вне боя (спустя паузу) — распад до нуля.
        if (session.Rage == 0 || now - session.LastCombatMs < OutOfCombatDelayMs)
            return;
        session.Rage = session.Rage > RageDecayPerTick ? session.Rage - RageDecayPerTick : 0;
        await SendPowerAsync(session, PowerRage, session.Rage, ct);
    }

    /// <summary>
    /// Списывает ресурс (ярость/энергия) на каст мили-абилки и двигает полоску. Мана списывается отдельно
    /// (<see cref="SpellCastCompletion"/>, правило 5 секунд). M10.4a.
    /// </summary>
    internal async Task SpendPowerAsync(WorldSession session, byte powerType, uint amount, CancellationToken ct)
    {
        switch (powerType)
        {
            case PowerRage:
                session.Rage = session.Rage > amount ? session.Rage - amount : 0;
                await SendPowerAsync(session, PowerRage, session.Rage, ct);
                break;
            case PowerEnergy:
                session.Energy = session.Energy > amount ? session.Energy - amount : 0;
                await SendPowerAsync(session, PowerEnergy, session.Energy, ct);
                break;
        }
    }

    /// <summary>
    /// Шлёт текущее значение ресурса себе: VALUES-апдейт <c>UNIT_FIELD_POWER1+powerType</c> (консистентность
    /// поля) + SMSG_POWER_UPDATE (двигает полоску у клиента 3.3.5a). Аналог регена маны (<see cref="ManaRegenService"/>).
    /// </summary>
    private static async Task SendPowerAsync(WorldSession session, byte powerType, uint amount, CancellationToken ct)
    {
        var guid = (ulong)session.InWorldGuid;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate(guid, m => m.SetUInt32(UpdateField.UnitPower1 + powerType, amount)), ct);

        var w = new ByteWriter(16);
        PackedGuid.Write(w, guid);
        w.UInt8(powerType);
        w.UInt32(amount);
        await session.SendAsync(WorldOpcode.SmsgPowerUpdate, w.ToArray(), ct);
    }
}
