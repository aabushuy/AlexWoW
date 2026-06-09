using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий списка реалмов (таблица realmlist, БД <c>alexwow_auth</c>). Выделен из
/// <see cref="IAccountRepository"/> по SRP (рефактор #24).
/// </summary>
public interface IRealmRepository
{
    Task<IReadOnlyList<Realm>> GetRealmsAsync(CancellationToken ct = default);
}
