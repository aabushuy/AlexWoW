using System;
using AlexWoW.Database;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace AlexWoW.AuthServer;

/// <summary>
/// Сборка DAL для CLI-команд (create-account / reset-all-passwords / set-admin) без DI-хоста.
/// Рефактор #24 (SRP): отдаёт focused-абстракции (account/schema-init) отдельно. Фикс. ServerVersion 8.4;
/// пул контекстов для одноразовых команд не нужен — простая фабрика.
/// </summary>
internal static class CliRepository
{
    public static IAccountRepository CreateAccountRepository(string connectionString)
        => new EfAccountRepository(BuildFactory(connectionString));

    public static ISchemaInitializer CreateSchemaInitializer(string connectionString)
        => new AuthSchemaInitializer(BuildFactory(connectionString));

    private static IDbContextFactory<AuthDbContext> BuildFactory(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(connectionString, ServerVersion.Create(new Version(8, 4, 0), ServerType.MySql))
            .Options;
        return new SimpleContextFactory(options);
    }

    private sealed class SimpleContextFactory(DbContextOptions<AuthDbContext> options)
        : IDbContextFactory<AuthDbContext>
    {
        public AuthDbContext CreateDbContext() => new(options);
    }
}
