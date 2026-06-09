using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Инициализация схемы БD <c>alexwow_auth</c>: применяет EF-миграции (на чистой БД создаёт схему,
/// на проде после baseline — no-op) и сидирует реалм по умолчанию. Отдельная ответственность,
/// выделенная из репозиториев по SRP (рефактор #24). Вызывается одним мигратором (auth-сервер/CLI).
/// </summary>
public interface ISchemaInitializer
{
    Task EnsureSchemaAsync(Realm defaultRealm, CancellationToken ct = default);
}
