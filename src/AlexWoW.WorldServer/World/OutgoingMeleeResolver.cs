namespace AlexWoW.WorldServer.World;

/// <summary>
/// Резолвер исходящего мили-удара игрока по существу (CMaNGOS <c>Unit::RollMeleeOutcomeAgainst</c>,
/// упрощённый single-roll вариант). Порядок проверок: miss → dodge → parry → glance (только autoattack) →
/// crit → hit. Базовые шансы зависят от разницы уровней игрока и моба; рейтинги хита и крита из аур
/// (SPELL.T1) суммируются. Чистая функция — тестируема, не зависит от состояния мира.
/// </summary>
public static class OutgoingMeleeResolver
{
    public enum Outcome : byte { Hit = 1, Miss = 4, Dodge = 2, Parry = 3, Glance = 8, Crit = 16 }

    /// <summary>
    /// Возвращает исход удара и множитель урона (1.0 для hit, 0.7 для glance, 2.0 для crit, 0 для остальных).
    /// </summary>
    /// <param name="attackerLevel">Уровень атакующего игрока.</param>
    /// <param name="targetLevel">Уровень цели-существа.</param>
    /// <param name="hitBonusPct">Сумма +% к hit от аур (MOD_HIT_CHANCE + MOD_RATING/CR_HIT_MELEE).</param>
    /// <param name="critPct">Шанс крита (статы + аура-бонусы MOD_CRIT_PERCENT + MOD_RATING/CR_CRIT_MELEE).</param>
    /// <param name="isAutoAttack">true — белая автоатака (применим glance); false — мили-абилка.</param>
    /// <param name="roll01">Случайное число [0;1) — для тестируемости передаём извне.</param>
    public static (Outcome Outcome, float Multiplier) Resolve(
        byte attackerLevel, byte targetLevel,
        float hitBonusPct, float critPct, bool isAutoAttack, double roll01)
    {
        var levelDiff = Math.Max(0, targetLevel - attackerLevel);
        // Шанс miss: 5% база + штраф за уровень (≤2 — 0.5% за уровень, >2 — 2% за уровень).
        // Эталон CMaNGOS Unit::MeleeMissChanceCalc для high-level mob skill 'wall'.
        var missPct = 5f + (levelDiff <= 2 ? levelDiff * 0.5f : 2f * 0.5f + (levelDiff - 2) * 2f);
        missPct = Math.Max(0f, missPct - hitBonusPct); // hit-рейтинг снижает miss
        missPct = Math.Clamp(missPct, 0f, 60f);

        // Dodge цели: та же шкала уровня. Expertise не моделируется — игнорируем.
        var dodgePct = 5f + (levelDiff <= 2 ? levelDiff * 0.5f : 2f * 0.5f + (levelDiff - 2) * 2f);
        dodgePct = Math.Clamp(dodgePct, 0f, 30f);

        // Parry: гуманоиды парируют (5% база) — мы не различаем тип моба, предполагаем 5% если уровень >= 10.
        // У реальных не-гуманоидов (звери) парирования нет; пока упрощение. Атака сзади — не моделируем.
        var parryPct = targetLevel >= 10 ? 5f : 0f;

        // Glancing blow: только автоатака против цели НА УРОВНЕ ИЛИ ВЫШЕ (CMaNGOS: только если targetLevel > attackerLevel).
        // Шанс: 10% × max(0, level diff), кэп 40%. Множитель урона: 0.7 (упрощение CMaNGOS skill-based).
        var glancePct = isAutoAttack && levelDiff > 0 ? Math.Min(40f, levelDiff * 10f) : 0f;

        var critEff = Math.Clamp(critPct, 0f, 100f);

        var roll = roll01 * 100.0;
        if (roll < missPct)
            return (Outcome.Miss, 0f);
        if (roll < missPct + dodgePct)
            return (Outcome.Dodge, 0f);
        if (roll < missPct + dodgePct + parryPct)
            return (Outcome.Parry, 0f);
        if (roll < missPct + dodgePct + parryPct + glancePct)
            return (Outcome.Glance, 0.7f);
        if (roll < missPct + dodgePct + parryPct + glancePct + critEff)
            return (Outcome.Crit, 2.0f);
        return (Outcome.Hit, 1.0f);
    }
}
