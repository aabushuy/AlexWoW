using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий ядра персонажа (таблица <c>characters</c> + склонения имени, БД <c>alexwow_auth</c>).
/// Часть DAL-фасада <see cref="ICharacterStore"/>. Срез 1 рефактора DAL (#23).
/// </summary>
public interface ICharacterRepository
{
    /// <summary>Максимум персонажей на аккаунт (на реалм).</summary>
    const int MaxCharactersPerAccount = 10;

    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Character>> GetByAccountAsync(uint accountId, CancellationToken ct = default);

    Task<Character?> GetByGuidAsync(uint guid, CancellationToken ct = default);

    Task<bool> NameExistsAsync(string name, CancellationToken ct = default);

    Task<int> CountByAccountAsync(uint accountId, CancellationToken ct = default);

    Task<uint> CreateAsync(Character character, CancellationToken ct = default);

    Task SavePositionAsync(uint guid, float x, float y, float z, uint map, CancellationToken ct = default);

    /// <summary>Сохраняет 5 склонений имени персонажа (ruRU). Перезаписывает существующие.</summary>
    Task SetDeclinedNamesAsync(uint ownerGuid, string[] names, CancellationToken ct = default);

    /// <summary>GUID'ы персонажей (из набора), у кого заданы непустые склонения имени.</summary>
    Task<HashSet<uint>> GetGuidsWithDeclinedNamesAsync(
        IReadOnlyCollection<uint> guids, CancellationToken ct = default);

    /// <summary>5 склонений имени персонажа или null, если не заданы.</summary>
    Task<string[]?> GetDeclinedNamesAsync(uint ownerGuid, CancellationToken ct = default);

    /// <summary>Деньги персонажа (медь).</summary>
    Task SetMoneyAsync(uint guid, uint money, CancellationToken ct = default);

    /// <summary>Меняет расу и пол персонажа (админ-правка / M8.6). Внешность не трогает.</summary>
    Task SetRaceGenderAsync(uint guid, byte race, byte gender, CancellationToken ct = default);

    /// <summary>Последняя стоимость сброса талантов (медь) — для растущей цены. M9.8.</summary>
    Task SetTalentResetCostAsync(uint guid, uint cost, CancellationToken ct = default);

    /// <summary>Сохраняет уровень и текущий опыт персонажа.</summary>
    Task SetLevelXpAsync(uint guid, byte level, uint xp, CancellationToken ct = default);

    /// <summary>Сохраняет маску видимых доп. панелей (PLAYER_FIELD_BYTES[2]).</summary>
    Task SetActionBarsAsync(uint guid, byte actionBars, CancellationToken ct = default);

    /// <summary>Помечает/снимает персонажа как тестировщика QA-доски (KB10).</summary>
    Task SetTesterAsync(uint guid, bool isTester, CancellationToken ct = default);

    /// <summary>Удаляет персонажа, принадлежащего аккаунту. Возвращает true, если строка удалена.</summary>
    Task<bool> DeleteAsync(uint guid, uint accountId, CancellationToken ct = default);
}
