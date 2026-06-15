using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Поиск предметов по <c>item_template</c> для админки (M8). SRP-репозиторий (#25), read-only.</summary>
public interface IItemSearchRepository
{
    /// <summary>Подбор предметов по фильтру (требуемый уровень/класс/тип/название), не более Limit.</summary>
    Task<IReadOnlyList<ItemTemplateData>> SearchAsync(ItemSearchFilter filter, CancellationToken ct = default);
}
