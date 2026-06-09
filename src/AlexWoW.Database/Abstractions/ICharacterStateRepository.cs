namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий прочего состояния персонажа: изученные спеллы (<c>character_spell</c>),
/// ауры-переключатели (<c>character_aura</c>), ярлыки панелей (<c>character_action</c>) и
/// account-data блобы (<c>account_data</c>). Часть DAL-фасада <see cref="ICharacterStore"/>.
/// Срез 1 рефактора DAL (#23).
/// </summary>
public interface ICharacterStateRepository
{
    /// <summary>Изученные у тренера спеллы персонажа (сверх стартового набора по классу).</summary>
    Task<IReadOnlyList<uint>> GetLearnedSpellsAsync(uint ownerGuid, CancellationToken ct = default);

    /// <summary>Сохраняет изученный спелл (идемпотентно).</summary>
    Task AddLearnedSpellAsync(uint ownerGuid, uint spell, CancellationToken ct = default);

    /// <summary>Сохранённые ауры персонажа: (spell, form, remainingMs). remainingMs=0 — перманентный
    /// переключатель; &gt;0 — временны́й бафф/HoT с остатком длительности (M10.5).</summary>
    Task<IReadOnlyList<(uint Spell, byte Form, uint RemainingMs)>> GetAurasAsync(uint ownerGuid, CancellationToken ct = default);

    /// <summary>Сохраняет активную ауру-переключатель (перманентную, идемпотентно).</summary>
    Task AddAuraAsync(uint ownerGuid, uint spell, byte form, CancellationToken ct = default);

    /// <summary>Убирает сохранённую ауру-переключатель.</summary>
    Task RemoveAuraAsync(uint ownerGuid, uint spell, CancellationToken ct = default);

    /// <summary>Перезаписывает временны́е ауры (remainingMs&gt;0) персонажа при выходе: удаляет прежние
    /// временны́е и пишет текущие (переключатели не трогает). M10.5.</summary>
    Task SaveTimedAurasAsync(uint ownerGuid, IReadOnlyList<(uint Spell, uint RemainingMs)> auras, CancellationToken ct = default);

    /// <summary>Ярлыки панелей персонажа: button → packed_data.</summary>
    Task<IReadOnlyDictionary<byte, uint>> GetActionButtonsAsync(uint ownerGuid, CancellationToken ct = default);

    /// <summary>Ставит ярлык на кнопку панели (packed=0 → снять).</summary>
    Task SetActionButtonAsync(uint ownerGuid, byte button, uint packed, CancellationToken ct = default);

    /// <summary>Сохранённый блоб account-data (сжатый, как прислал клиент) + время, или null.</summary>
    Task<(uint Time, byte[] Data)?> GetAccountDataAsync(uint ownerId, bool isChar, byte dataType, CancellationToken ct = default);

    /// <summary>Времена сохранённых блобов owner'а (для SMSG_ACCOUNT_DATA_TIMES): data_type → time.</summary>
    Task<IReadOnlyDictionary<byte, uint>> GetAccountDataTimesAsync(uint ownerId, bool isChar, CancellationToken ct = default);

    /// <summary>Сохраняет/обновляет блоб account-data.</summary>
    Task UpsertAccountDataAsync(uint ownerId, bool isChar, byte dataType, uint time, byte[] data, CancellationToken ct = default);
}
