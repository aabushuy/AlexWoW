using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// DEFENSE.1/.2: управление UNIT_FIELD_AURASTATE и таймерами состояний ауры кастера.
///
/// Клиент 3.3.5a решает «можно ли кастовать спелл с CasterAuraState=X» по битам в
/// UNIT_FIELD_AURASTATE: бит (1 &lt;&lt; (state−1)) должен быть взведён. Без обновления поля кнопка
/// абилки серая, даже если сервер сам разрешил бы каст.
///
/// Поддерживаемые состояния:
/// * AURA_STATE_DEFENSE = 1 (бит 0, mask 0x01) — успешный dodge/parry/block игрока (Revenge).
/// * AURA_STATE_HUNTER_PARRY = 7 (бит 6, mask 0x40) — успешный parry у Hunter (Counterattack).
///   Это же число AURA_STATE_WARRIOR_VICTORY_RUSH (после kill, Victory Rush) — разные триггеры,
///   один бит. Покрываем только parry-сторону сейчас.
///
/// Маска UNIT_FIELD_AURASTATE собирается из всех активных источников; апдейт клиенту шлётся
/// только когда маска реально меняется (избегаем флуда SMSG_UPDATE_OBJECT).
/// </summary>
internal sealed class AuraStateService
{
    /// <summary>Длительность окна Revenge / Counterattack: 5 секунд (эталон CMaNGOS).</summary>
    internal const long DefenseStateDurationMs = 5000;

    private const uint MaskDefense       = 1u << 0;  // state=1
    private const uint MaskHunterParry   = 1u << 6;  // state=7

    /// <summary>DEFENSE.1: ставит AURA_STATE_DEFENSE на 5с (Revenge-окно). Вызывается из CreatureCombatAI
    /// при успешном dodge/parry/block игрока. Идемпотентно: повторный вызов обновляет таймер.</summary>
    internal Task SetDefenseAsync(WorldSession session, long now, CancellationToken ct)
    {
        var oldMask = BuildMask(session, now);
        session.Combat.DefenseStateExpiresMs = now + DefenseStateDurationMs;
        return SendIfChangedAsync(session, oldMask, now, ct);
    }

    /// <summary>DEFENSE.2: ставит AURA_STATE_HUNTER_PARRY на 5с (Counterattack-окно). Только для Hunter
    /// (class=3) — у других классов триггер state=7 другой (Warrior Victory Rush после kill).</summary>
    internal Task SetHunterParryAsync(WorldSession session, long now, CancellationToken ct)
    {
        var oldMask = BuildMask(session, now);
        session.Combat.HunterParryStateExpiresMs = now + DefenseStateDurationMs;
        return SendIfChangedAsync(session, oldMask, now, ct);
    }

    /// <summary>Снимает DEFENSE (state «потрачен» после каста Revenge или по таймеру в WorldTick).</summary>
    internal Task ClearDefenseAsync(WorldSession session, CancellationToken ct)
    {
        if (session.Combat.DefenseStateExpiresMs == 0) return Task.CompletedTask;
        var now = Environment.TickCount64;
        var oldMask = BuildMask(session, now);
        session.Combat.DefenseStateExpiresMs = 0;
        return SendIfChangedAsync(session, oldMask, now, ct);
    }

    /// <summary>Снимает HUNTER_PARRY (после каста Counterattack или по таймеру в WorldTick).</summary>
    internal Task ClearHunterParryAsync(WorldSession session, CancellationToken ct)
    {
        if (session.Combat.HunterParryStateExpiresMs == 0) return Task.CompletedTask;
        var now = Environment.TickCount64;
        var oldMask = BuildMask(session, now);
        session.Combat.HunterParryStateExpiresMs = 0;
        return SendIfChangedAsync(session, oldMask, now, ct);
    }

    /// <summary>Снимает state после успешного каста спелла с указанным CasterAuraState (state «потрачен»).</summary>
    internal Task ClearAfterCastAsync(WorldSession session, uint state, CancellationToken ct) => state switch
    {
        1 => ClearDefenseAsync(session, ct),
        7 => ClearHunterParryAsync(session, ct),
        _ => Task.CompletedTask,
    };

    /// <summary>Проверка «есть ли state для каста спелла с CasterAuraState=<paramref name="needed"/>».
    /// state=0 — нет требования (всегда true). Прочие states — false (не покрыты).</summary>
    internal static bool HasState(WorldSession session, uint needed, long now) => needed switch
    {
        0 => true,
        1 => session.Combat.DefenseStateExpiresMs > now,
        7 => session.Combat.HunterParryStateExpiresMs > now,
        _ => false, // TODO: CONFLAGRATE(10), SWIFTMEND(11), ENRAGE(13), BLEEDING(14)...
    };

    /// <summary>Собирает UNIT_FIELD_AURASTATE маску по активным state-таймерам.</summary>
    private static uint BuildMask(WorldSession session, long now)
    {
        uint m = 0;
        if (session.Combat.DefenseStateExpiresMs > now)     m |= MaskDefense;
        if (session.Combat.HunterParryStateExpiresMs > now) m |= MaskHunterParry;
        return m;
    }

    /// <summary>Шлёт SMSG_UPDATE_OBJECT с новым UNIT_FIELD_AURASTATE только если маска реально изменилась.</summary>
    private static Task SendIfChangedAsync(WorldSession session, uint oldMask, long now, CancellationToken ct)
    {
        if (session.InWorldGuid == 0) return Task.CompletedTask;
        var newMask = BuildMask(session, now);
        if (newMask == oldMask) return Task.CompletedTask;
        return session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.UnitAuraState, newMask);
            }), ct);
    }
}
