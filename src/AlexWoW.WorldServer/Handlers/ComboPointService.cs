using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Очки серии (combo points, Фаза 2 — CP.1): серверо-авторитетный ресурс рога/друида-кошки. Очки (0..5)
/// привязаны к комбо-цели (<see cref="SessionCombatState.ComboTargetGuid"/>); генераторы (эффект 80) копят,
/// финишеры (биты FINISHING_MOVE_* в AttributesEx) расходуют. Клиенту шлём <c>SMSG_UPDATE_COMBO_POINTS</c>
/// (packed guid цели + u8). Эталон — CMaNGOS <c>Player::AddComboPoints/ClearComboPoints/SendComboPoints</c>.
/// </summary>
internal sealed class ComboPointService
{
    internal const byte MaxComboPoints = 5;

    /// <summary>Задаёт очки серии на цели и шлёт клиенту (clamp 0..5). CP.1: проверочный путь (дев-команда).</summary>
    internal Task SetAsync(WorldSession session, ulong targetGuid, byte points, CancellationToken ct)
    {
        session.Combat.ComboTargetGuid = targetGuid;
        session.Combat.ComboPoints = Math.Min(points, MaxComboPoints);
        return SendAsync(session, ct);
    }

    /// <summary>
    /// Начисляет очки серии на цели (CP.2: генератор). Смена комбо-цели обнуляет накопленное (очки
    /// привязаны к конкретной цели). Clamp 0..5. <paramref name="count"/>=0 / <paramref name="targetGuid"/>=0 — no-op.
    /// </summary>
    internal Task AddAsync(WorldSession session, ulong targetGuid, byte count, CancellationToken ct)
    {
        if (targetGuid == 0 || count == 0)
            return Task.CompletedTask;
        if (session.Combat.ComboTargetGuid != targetGuid)
        {
            session.Combat.ComboTargetGuid = targetGuid;
            session.Combat.ComboPoints = 0;
        }
        session.Combat.ComboPoints = (byte)Math.Min(session.Combat.ComboPoints + count, MaxComboPoints);
        return SendAsync(session, ct);
    }

    /// <summary>
    /// Снимает все очки серии (CP.3: финишер израсходовал) и шлёт апдейт (0). Возвращает сколько было —
    /// для скалирования эффекта финишера. Комбо-цель сбрасывается (новый цикл копится заново).
    /// </summary>
    internal async Task<byte> ConsumeAsync(WorldSession session, CancellationToken ct)
    {
        var spent = session.Combat.ComboPoints;
        if (spent > 0)
            await ClearAsync(session, ct);
        return spent;
    }

    /// <summary>Обнуляет очки серии, если они накоплены на <paramref name="targetGuid"/> (цель умерла). No-op иначе.</summary>
    internal Task ClearForTargetAsync(WorldSession session, ulong targetGuid, CancellationToken ct)
        => session.Combat.ComboTargetGuid == targetGuid && targetGuid != 0
            ? ClearAsync(session, ct)
            : Task.CompletedTask;

    /// <summary>Обнуляет очки серии и шлёт апдейт (смерть/смена цели, выход из стелса и т.п.). No-op, если уже пусто.</summary>
    internal Task ClearAsync(WorldSession session, CancellationToken ct)
    {
        if (session.Combat.ComboPoints == 0 && session.Combat.ComboTargetGuid == 0)
            return Task.CompletedTask;
        session.Combat.ComboPoints = 0;
        session.Combat.ComboTargetGuid = 0;
        return SendAsync(session, ct);
    }

    /// <summary>Текущее значение очков серии (packed guid цели + u8) себе. 0/0 — корректно сбрасывает UI.</summary>
    private static Task SendAsync(WorldSession session, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgUpdateComboPoints,
            CombatPackets.BuildUpdateComboPoints(session.Combat.ComboTargetGuid, session.Combat.ComboPoints), ct);
}
