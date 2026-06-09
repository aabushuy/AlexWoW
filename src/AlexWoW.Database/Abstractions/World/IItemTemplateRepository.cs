using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Шаблоны предметов БД мира (item_template, дамп mangos). SRP-репозиторий (#25).</summary>
public interface IItemTemplateRepository
{
    /// <summary>Полный шаблон предмета (item_template) для SMSG_ITEM_QUERY_SINGLE_RESPONSE.</summary>
    Task<ItemTemplateData?> GetItemTemplateAsync(uint entry, CancellationToken ct = default);

    /// <summary>displayid + InventoryType по набору entry (для paperdoll в SMSG_CHAR_ENUM).</summary>
    Task<IReadOnlyDictionary<uint, (uint DisplayId, byte InventoryType)>> GetItemDisplaysAsync(
        IReadOnlyCollection<uint> entries, CancellationToken ct = default);
}
