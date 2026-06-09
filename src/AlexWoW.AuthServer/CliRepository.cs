using System;
using AlexWoW.Database;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace AlexWoW.AuthServer;

/// <summary>
/// Сборка DAL для CLI-команд (create-account / reset-all-passwords / set-admin) без DI-хоста.
/// Срез 5 рефактора DAL (#23): CLI тоже на EF (фикс. ServerVersion 8.4) — Dapper для нашей БД
/// больше не используется нигде. Пул контекстов для одноразовых команд не нужен — простая фабрика.
/// </summary>
internal static class CliRepository
{
    public static IAccountRepository CreateAccountRepository(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(connectionString, ServerVersion.Create(new Version(8, 4, 0), ServerType.MySql))
            .Options;
        return new EfAccountRepository(new SimpleContextFactory(options));
    }

    private sealed class SimpleContextFactory(DbContextOptions<AuthDbContext> options)
        : IDbContextFactory<AuthDbContext>
    {
        public AuthDbContext CreateDbContext() => new(options);
    }
}
