namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Фасад DAL персонажей (БД <c>alexwow_auth</c>): объединяет ядро персонажа, инвентарь, квест-статусы
/// и прочее состояние. Под этим типом <c>WorldSession.Characters</c> отдаёт всю поверхность
/// <see cref="CharactersDatabase"/> — вызовы в хендлерах не меняются, при этом точечным потребителям
/// доступны сегрегированные интерфейсы. Срез 1 рефактора DAL (#23).
/// </summary>
public interface ICharacterStore
    : ICharacterRepository, IInventoryRepository, IQuestRepository, ICharacterStateRepository
{
}
