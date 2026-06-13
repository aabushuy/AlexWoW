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
    private const byte PowerRunic = 6;    // powertype силы рун (runic power) DK
    private const uint MaxRage = 1000;    // ×10 → 100 у клиента
    private const uint MaxEnergy = 100;
    private const uint MaxRunicPower = 1000; // ×10 → 100 у клиента (как ярость)
    /// <summary>Распад силы рун за тик (1 с) вне боя (×10 → 1.2 RP/с — эталон mangos 2.5/2с ≈ 1.25/с). RUNE.4.</summary>
    private const uint RunicPowerDecayPerTick = 12;

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
        if (damage == 0 || session.Combat.Rage >= MaxRage)
            return;

        float level = Math.Max((byte)1, c.Level);
        var conversion = 0.0091107836f * level * level + 3.225598133f * level + 4.2652911f;
        float add;
        if (attacker)
        {
            var speedSec = session.Combat.MainHandSpeedMs / 1000f;
            add = (damage / conversion * 7.5f + speedSec * 3.5f) / 2f; // mainhand hit factor
        }
        else
        {
            add = damage / conversion * 2.5f;
        }

        var units = (uint)MathF.Max(1f, add * 10f); // ярость хранится ×10
        session.Combat.Rage = Math.Min(MaxRage, session.Combat.Rage + units);
        await SendPowerAsync(session, PowerRage, session.Combat.Rage, ct);
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
        if (powerType != PowerRage && powerType != PowerEnergy && powerType != PowerRunic)
            return; // мана-класс — реген маны отдельно
        if (now - session.Combat.LastResourceTickMs < ResourceTickMs)
            return;
        session.Combat.LastResourceTickMs = now;

        if (powerType == PowerEnergy)
        {
            if (session.Combat.Energy >= MaxEnergy)
                return;
            session.Combat.Energy = Math.Min(MaxEnergy, session.Combat.Energy + EnergyPerTick);
            await SendPowerAsync(session, PowerEnergy, session.Combat.Energy, ct);
            return;
        }

        // RUNE.4: сила рун — вне боя (спустя паузу) распадается до нуля (как ярость).
        if (powerType == PowerRunic)
        {
            if (session.Combat.RunicPower == 0 || now - session.Combat.LastCombatMs < OutOfCombatDelayMs)
                return;
            session.Combat.RunicPower = session.Combat.RunicPower > RunicPowerDecayPerTick
                ? session.Combat.RunicPower - RunicPowerDecayPerTick : 0;
            await SendPowerAsync(session, PowerRunic, session.Combat.RunicPower, ct);
            return;
        }

        // Ярость: вне боя (спустя паузу) — распад до нуля.
        if (session.Combat.Rage == 0 || now - session.Combat.LastCombatMs < OutOfCombatDelayMs)
            return;
        session.Combat.Rage = session.Combat.Rage > RageDecayPerTick ? session.Combat.Rage - RageDecayPerTick : 0;
        await SendPowerAsync(session, PowerRage, session.Combat.Rage, ct);
    }

    /// <summary>Начисляет ресурс (ярость/энергия) от эффекта спелла (ENERGIZE/ярость Рывка) с капом
    /// и апдейтом полоски. Мана — отдельно (<see cref="SpellCastCompletion"/>). M10.6.</summary>
    internal async Task GainPowerAsync(WorldSession session, byte powerType, uint amount, CancellationToken ct)
    {
        switch (powerType)
        {
            case PowerRage:
                session.Combat.Rage = Math.Min(MaxRage, session.Combat.Rage + amount);
                await SendPowerAsync(session, PowerRage, session.Combat.Rage, ct);
                break;
            case PowerEnergy:
                session.Combat.Energy = Math.Min(MaxEnergy, session.Combat.Energy + amount);
                await SendPowerAsync(session, PowerEnergy, session.Combat.Energy, ct);
                break;
            case PowerRunic:
                session.Combat.RunicPower = Math.Min(MaxRunicPower, session.Combat.RunicPower + amount);
                await SendPowerAsync(session, PowerRunic, session.Combat.RunicPower, ct);
                break;
        }
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
                session.Combat.Rage = session.Combat.Rage > amount ? session.Combat.Rage - amount : 0;
                await SendPowerAsync(session, PowerRage, session.Combat.Rage, ct);
                break;
            case PowerEnergy:
                session.Combat.Energy = session.Combat.Energy > amount ? session.Combat.Energy - amount : 0;
                await SendPowerAsync(session, PowerEnergy, session.Combat.Energy, ct);
                break;
            case PowerRunic:
                session.Combat.RunicPower = session.Combat.RunicPower > amount ? session.Combat.RunicPower - amount : 0;
                await SendPowerAsync(session, PowerRunic, session.Combat.RunicPower, ct);
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
