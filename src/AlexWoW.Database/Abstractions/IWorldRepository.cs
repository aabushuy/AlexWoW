using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий статической БД мира (read-only дамп CMaNGOS-WotLK, БД <c>mangos</c>).
/// Срез 1 рефактора DAL (#23): абстракция поверх Dapper-реализации (<see cref="WorldDatabase"/>);
/// эта сторона остаётся на Dapper (дамп грузится целиком, EF-миграции не нужны).
/// </summary>
public interface IWorldRepository
{
    /// <summary>Проверка доступности БД мира при старте (есть ли таблица creature).</summary>
    Task<long> CountCreaturesAsync(CancellationToken ct = default);

    /// <summary>Спавны существ на карте в квадрате ±range от точки (грубая зона видимости).</summary>
    Task<IReadOnlyList<CreatureSpawnData>> GetCreaturesNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default);

    /// <summary>Шаблон существа по entry (для CMSG_CREATURE_QUERY).</summary>
    Task<CreatureTemplateData?> GetCreatureTemplateAsync(uint entry, CancellationToken ct = default);

    /// <summary>Стартовый набор предметов по расе/классу (playercreateinfo_item ⨝ item_template).</summary>
    Task<IReadOnlyList<StartingItem>> GetStartingItemsAsync(byte race, byte cls, CancellationToken ct = default);

    /// <summary>Стартовые спеллы по расе/классу (playercreateinfo_spell).</summary>
    Task<IReadOnlyList<uint>> GetStartSpellsAsync(byte race, byte cls, CancellationToken ct = default);

    /// <summary>Данные тренера по entry существа или null, если существо не тренер.</summary>
    Task<TrainerData?> GetTrainerAsync(uint entry, CancellationToken ct = default);

    /// <summary>displayid + InventoryType по набору entry (для paperdoll в SMSG_CHAR_ENUM).</summary>
    Task<IReadOnlyDictionary<uint, (uint DisplayId, byte InventoryType)>> GetItemDisplaysAsync(
        IReadOnlyCollection<uint> entries, CancellationToken ct = default);

    /// <summary>Ассортимент вендора по entry существа (только за золото, без условий).</summary>
    Task<IReadOnlyList<VendorItem>> GetVendorItemsAsync(uint entry, CancellationToken ct = default);

    /// <summary>Лут-определение существа (деньги + кандидаты-предметы) или null, если лута нет.</summary>
    Task<CreatureLootData?> GetCreatureLootAsync(uint creatureEntry, CancellationToken ct = default);

    /// <summary>Все реакции фракций (faction_template) для серверного авто-агро.</summary>
    Task<IReadOnlyList<FactionTemplateRow>> GetFactionTemplatesAsync(CancellationToken ct = default);

    /// <summary>Базовые HP/мана по классу+уровню (player_classlevelstats).</summary>
    Task<IReadOnlyDictionary<(byte Class, byte Level), (uint Hp, uint Mana)>>
        GetClassLevelStatsAsync(CancellationToken ct = default);

    /// <summary>Базовые статы (str/agi/sta/int/spi) по расе+классу+уровню (player_levelstats).</summary>
    Task<IReadOnlyDictionary<(byte Race, byte Class, byte Level), (uint Str, uint Agi, uint Sta, uint Int, uint Spi)>>
        GetLevelStatsAsync(CancellationToken ct = default);

    /// <summary>Кривая опыта: lvl → xp_for_next_level (player_xp_for_level).</summary>
    Task<IReadOnlyDictionary<uint, uint>> GetXpForLevelTableAsync(CancellationToken ct = default);

    /// <summary>Entry существ, дающих квесты (distinct creature_questrelation.id) — для иконок «!».</summary>
    Task<IReadOnlyList<uint>> GetQuestGiverEntriesAsync(CancellationToken ct = default);

    /// <summary>Entry существ, принимающих квесты (distinct creature_involvedrelation.id) — для иконок «?».</summary>
    Task<IReadOnlyList<uint>> GetQuestEnderEntriesAsync(CancellationToken ct = default);

    /// <summary>Все связи «дающий→квест» (creature_questrelation) — для кэша статуса иконок.</summary>
    Task<IReadOnlyList<QuestRelation>> GetQuestGiverRelationsAsync(CancellationToken ct = default);

    /// <summary>Все связи «приёмщик→квест» (creature_involvedrelation) — для кэша статуса иконок.</summary>
    Task<IReadOnlyList<QuestRelation>> GetQuestEnderRelationsAsync(CancellationToken ct = default);

    /// <summary>Квесты, которые даёт существо (creature_questrelation ⨝ quest_template).</summary>
    Task<IReadOnlyList<GiverQuest>> GetGiverQuestsAsync(uint creatureEntry, CancellationToken ct = default);

    /// <summary>Id квестов, которые ПРИНИМАЕТ существо (creature_involvedrelation). Для сдачи.</summary>
    Task<IReadOnlyList<uint>> GetEnderQuestIdsAsync(uint creatureEntry, CancellationToken ct = default);

    /// <summary>Полный шаблон квеста (quest_template) — детали/награды/цели.</summary>
    Task<QuestTemplateData?> GetQuestAsync(uint entry, CancellationToken ct = default);

    /// <summary>Полный шаблон предмета (item_template) для SMSG_ITEM_QUERY_SINGLE_RESPONSE.</summary>
    Task<ItemTemplateData?> GetItemTemplateAsync(uint entry, CancellationToken ct = default);

    /// <summary>Видимые гейм-объекты на карте в квадрате ±range (только с моделью).</summary>
    Task<IReadOnlyList<GameObjectSpawnData>> GetGameObjectsNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default);

    /// <summary>Шаблон гейм-объекта по entry (для CMSG_GAMEOBJECT_QUERY).</summary>
    Task<GameObjectTemplateData?> GetGameObjectTemplateAsync(uint entry, CancellationToken ct = default);
}
