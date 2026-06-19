using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// DEFENSE.1: управление UNIT_FIELD_AURASTATE и таймером AURA_STATE_DEFENSE.
///
/// Клиент 3.3.5a решает «можно ли кастовать спелл с CasterAuraState=X» по битам в
/// UNIT_FIELD_AURASTATE: бит (1 &lt;&lt; (state−1)) должен быть взведён. Без обновления поля кнопка
/// Revenge у игрока всегда серая, даже если сервер сам разрешил бы каст.
///
/// AURA_STATE_DEFENSE = 1 → бит 0 (mask 0x01). Выставляется на 5с при успешном dodge/parry/block
/// у игрока (см. <see cref="CreatureCombatAI"/>), снимается в <see cref="ClearDefenseAsync"/>
/// после успешного каста Revenge или по истечении таймера.
/// </summary>
internal sealed class AuraStateService
{
    /// <summary>Длительность окна Revenge: 5 секунд (эталон CMaNGOS — AuraState DEFENSE).</summary>
    internal const long DefenseStateDurationMs = 5000;

    /// <summary>Бит AURA_STATE_DEFENSE в UNIT_FIELD_AURASTATE (state=1 → 1 &lt;&lt; 0).</summary>
    private const uint AuraStateMaskDefense = 1u;

    /// <summary>Выставляет DEFENSE на DefenseStateDurationMs и отправляет UNIT_FIELD_AURASTATE клиенту
    /// (кнопка Revenge подсвечивается). Идемпотентно: вызов «обновляет» окно (5с от сейчас).</summary>
    internal Task SetDefenseAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return Task.CompletedTask;
        var wasActive = session.Combat.DefenseStateExpiresMs > now;
        session.Combat.DefenseStateExpiresMs = now + DefenseStateDurationMs;
        // Отправляем апдейт поля только если до этого бит не был взведён — иначе клиент уже подсвечивает.
        if (wasActive)
            return Task.CompletedTask;
        return SendAsync(session, AuraStateMaskDefense, ct);
    }

    /// <summary>Снимает DEFENSE (выставлен 0). Шлём UNIT_FIELD_AURASTATE=0. Вызывается после успешного
    /// каста Revenge (state «потрачен») и в WorldTick при истечении 5с.</summary>
    internal Task ClearDefenseAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || session.Combat.DefenseStateExpiresMs == 0)
            return Task.CompletedTask;
        session.Combat.DefenseStateExpiresMs = 0;
        return SendAsync(session, 0u, ct);
    }

    /// <summary>Проверка «есть ли state для каста спелла с CasterAuraState=<paramref name="needed"/>».
    /// Только DEFENSE (1) сейчас покрыт; другие states (7=Counterattack, etc.) пока false.</summary>
    internal static bool HasState(WorldSession session, uint needed, long now) => needed switch
    {
        0 => true,
        1 => session.Combat.DefenseStateExpiresMs > now,
        _ => false, // TODO: WARRIOR_VICTORY_RUSH(7), HUNTER_PARRY(7), CONFLAGRATE(10), ...
    };

    private static Task SendAsync(WorldSession session, uint mask, CancellationToken ct) =>
        session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.UnitAuraState, mask);
            }), ct);
}
