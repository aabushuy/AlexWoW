namespace AlexWoW.WorldServer.World;

/// <summary>
/// Ключи слотов per-session реестра dev-сущностей (<see cref="Net.WorldSession.DevNpcs"/>): на каждый слот —
/// максимум одна сущность (повторная команда заменяет прежнюю). Заведено каркасом D1; D2/D4 добавляют свои.
/// </summary>
public static class DevSlot
{
    /// <summary>Классовый тренер (<c>.trainer</c>). D1.</summary>
    public const string Trainer = "trainer";

    /// <summary>Тренер профессии (<c>.proftrainer</c>). D2.</summary>
    public const string ProfTrainer = "proftrainer";

    /// <summary>Вендор реагентов (<c>.reagentvendor</c>). D4.</summary>
    public const string ReagentVendor = "reagentvendor";

    /// <summary>Нода сбора (<c>.node</c>) — рудная жила/трава. M11.4.</summary>
    public const string Node = "node";
}
