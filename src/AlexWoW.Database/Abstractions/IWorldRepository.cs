namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Композитный фасад read-only БД мира (дамп CMaNGOS, БД <c>mangos</c>) — объединяет focused-репозитории
/// по доменам. Используется как единая точка доступа в <c>WorldSession.WorldDb</c> (чтобы не плодить
/// ~8 свойств/параметров сессии); точечные потребители (<c>*Store</c>) зависят от УЗКИХ интерфейсов.
/// Рефактор #25 (SOLID): god-класс WorldDatabase разбит на SRP-репозитории, фасад лишь делегирует.
/// </summary>
public interface IWorldRepository
    : ICreatureRepository, IGameObjectRepository, IItemTemplateRepository, IVendorRepository,
      ITrainerRepository, ILootRepository, IQuestTemplateRepository, IFactionRepository, IPlayerDataRepository,
      ISpellTemplateRepository
{
}
