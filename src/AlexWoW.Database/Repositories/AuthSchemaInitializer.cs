using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;
using ModelRealm = AlexWoW.Database.Models.Realm;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// Инициализация схемы alexwow_auth (<see cref="ISchemaInitializer"/>): применяет EF-миграции
/// (на чистой БД создаёт схему; на проде после baseline — no-op) и сидирует реалм по умолчанию,
/// если список пуст. Отдельная ответственность (SRP, #24). Вызывает один мигратор (auth-сервер/CLI).
/// </summary>
public sealed class AuthSchemaInitializer(IDbContextFactory<AuthDbContext> factory) : ISchemaInitializer
{
    public async Task EnsureSchemaAsync(ModelRealm defaultRealm, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        if (!await db.Realms.AnyAsync(ct))
        {
            db.Realms.Add(new Realm
            {
                Name = defaultRealm.Name,
                Address = defaultRealm.Address,
                Port = defaultRealm.Port,
                Type = defaultRealm.Type,
                Flags = defaultRealm.Flags,
                Timezone = defaultRealm.Timezone,
                Population = defaultRealm.Population,
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
