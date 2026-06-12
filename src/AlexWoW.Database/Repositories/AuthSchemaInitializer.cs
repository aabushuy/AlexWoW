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

        // Идемпотентный сид настроек по умолчанию: добавляем только отсутствующие ключи
        // (существующие значения не трогаем — их можно править в рантайме). M8.6.
        var existing = await db.ServerSettings.Select(x => x.Key).ToListAsync(ct);
        var missing = ServerSettingKeys.Defaults
            .Where(kv => !existing.Contains(kv.Key))
            .Select(kv => new ServerSetting { Key = kv.Key, Value = kv.Value })
            .ToList();
        if (missing.Count > 0)
        {
            db.ServerSettings.AddRange(missing);
            await db.SaveChangesAsync(ct);
        }
    }
}
