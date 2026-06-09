using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Статические данные игрока из БД мира: стартовый набор (playercreateinfo_item/spell) и прогрессия
/// (player_levelstats/classlevelstats/xp_for_level). SRP-репозиторий (#25).
/// </summary>
public interface IPlayerDataRepository
{
    /// <summary>Стартовый набор предметов по расе/классу (playercreateinfo_item ⨝ item_template).</summary>
    Task<IReadOnlyList<StartingItem>> GetStartingItemsAsync(byte race, byte cls, CancellationToken ct = default);

    /// <summary>Стартовые спеллы по расе/классу (playercreateinfo_spell).</summary>
    Task<IReadOnlyList<uint>> GetStartSpellsAsync(byte race, byte cls, CancellationToken ct = default);

    /// <summary>Базовые HP/мана по классу+уровню (player_classlevelstats).</summary>
    Task<IReadOnlyDictionary<(byte Class, byte Level), (uint Hp, uint Mana)>>
        GetClassLevelStatsAsync(CancellationToken ct = default);

    /// <summary>Базовые статы (str/agi/sta/int/spi) по расе+классу+уровню (player_levelstats).</summary>
    Task<IReadOnlyDictionary<(byte Race, byte Class, byte Level), (uint Str, uint Agi, uint Sta, uint Int, uint Spi)>>
        GetLevelStatsAsync(CancellationToken ct = default);

    /// <summary>Кривая опыта: lvl → xp_for_next_level (player_xp_for_level).</summary>
    Task<IReadOnlyDictionary<uint, uint>> GetXpForLevelTableAsync(CancellationToken ct = default);
}
